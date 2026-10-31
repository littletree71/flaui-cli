namespace FlauiCli.Core.Abstractions;

/// <summary>
/// Time left for the element searches of the current command. Drivers use it to bound slow fallback searches (for
/// example when UI Automation cuts a search short on a large tree), so a lookup does not run past the command's
/// <c>--timeout</c>. Per thread: commands run on the session thread.
/// </summary>
public static class SearchBudget
{
    /// <summary>Budget when no command set one (driver used directly, for example by tests).</summary>
    public static readonly TimeSpan Default = TimeSpan.FromSeconds(10);

    /// <summary>Lower bound, so a search that started just before the deadline still gets a short fallback.</summary>
    public static readonly TimeSpan Minimum = TimeSpan.FromSeconds(1);

    [ThreadStatic]
    private static DateTime? _deadline;

    [ThreadStatic]
    private static CancellationToken _cancel;

    [ThreadStatic]
    private static string? _incomplete;

    [ThreadStatic]
    private static (int Skip, int Take) _rows;

    /// <summary>
    /// The rows a search inside a table reads (<c>--skip-rows</c>, <c>--max-rows</c>): rows to skip first, and how many to
    /// read (0 = until the time runs out). Reset by <see cref="Begin"/>.
    /// </summary>
    public static (int Skip, int Take) Rows => _rows;

    /// <summary>Sets <see cref="Rows"/> for the current command.</summary>
    public static void LimitRows(int skip, int take) => _rows = (skip, take);

    /// <summary>
    /// Why the last search did not cover everything it should have, or null when it did. A search that stops early returns
    /// what it found, so callers that need every match, or that report "not found", check this.
    /// </summary>
    public static string? Incomplete => _incomplete;

    /// <summary>Called by drivers when a search stops before it has read everything.</summary>
    public static void ReportIncomplete(string reason) => _incomplete = reason;

    /// <summary>Forgets <see cref="Incomplete"/>, before a new search.</summary>
    public static void ClearIncomplete() => _incomplete = null;

    /// <summary>
    /// Sets the deadline to <paramref name="timeoutMs"/> from now until the returned scope is disposed. When
    /// <paramref name="cancel"/> is cancelled (the client went away), searches stop and <see cref="ThrowIfCancelled"/> throws.
    /// </summary>
    public static IDisposable Begin(int timeoutMs, CancellationToken cancel = default)
    {
        var previous = (_deadline, _cancel, _rows);
        _incomplete = null;
        _rows = default;
        _deadline = DateTime.UtcNow.AddMilliseconds(Math.Max(0, timeoutMs));
        if (cancel.CanBeCanceled) _cancel = cancel;
        return new Scope(previous);
    }

    /// <summary>
    /// Time left until the deadline (at least <see cref="Minimum"/>), or <see cref="Default"/> without one; zero once
    /// the command was cancelled.
    /// </summary>
    public static TimeSpan Remaining =>
        IsCancelled ? TimeSpan.Zero
        : _deadline is { } d ? (d - DateTime.UtcNow is var left && left > Minimum ? left : Minimum) : Default;

    /// <summary>Whether the deadline has passed or the command was cancelled: work that only refines a result should stop.</summary>
    public static bool IsExpired => IsCancelled || (_deadline is { } d && DateTime.UtcNow >= d);

    /// <summary>Whether the current command was cancelled (its client disconnected).</summary>
    public static bool IsCancelled => _cancel.IsCancellationRequested;

    /// <summary>Stops a long-running command (polling, waiting) whose client disconnected.</summary>
    public static void ThrowIfCancelled()
    {
        if (IsCancelled) throw new OperationCanceledException("The command was cancelled because the client disconnected", _cancel);
    }

    private sealed class Scope((DateTime? Deadline, CancellationToken Cancel, (int Skip, int Take) Rows) previous) : IDisposable
    {
        public void Dispose() => (_deadline, _cancel, _rows) = previous;
    }
}
