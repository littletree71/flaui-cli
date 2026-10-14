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

    /// <summary>Sets the deadline to <paramref name="timeoutMs"/> from now until the returned scope is disposed.</summary>
    public static IDisposable Begin(int timeoutMs)
    {
        var previous = _deadline;
        _deadline = DateTime.UtcNow.AddMilliseconds(Math.Max(0, timeoutMs));
        return new Scope(previous);
    }

    /// <summary>Time left until the deadline (at least <see cref="Minimum"/>), or <see cref="Default"/> without one.</summary>
    public static TimeSpan Remaining =>
        _deadline is { } d ? (d - DateTime.UtcNow is var left && left > Minimum ? left : Minimum) : Default;

    private sealed class Scope(DateTime? previous) : IDisposable
    {
        public void Dispose() => _deadline = previous;
    }
}
