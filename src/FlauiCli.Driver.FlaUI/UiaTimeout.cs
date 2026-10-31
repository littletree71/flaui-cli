using FlaUI.Core;

namespace FlauiCli.Drivers;

/// <summary>
/// Bounds single UI Automation calls. UIA waits up to its transaction timeout (20 seconds by default) for a provider to
/// answer one call, however long the command's <c>--timeout</c> is; a search that walks the rows of a large table can
/// take all of that and then fail with UIA_E_TIMEOUT. Lowering the timeout for a call keeps it within the time the
/// command has left.
/// </summary>
internal static class UiaTimeout
{
    /// <summary>Shortest timeout given to a call, so a call near the deadline can still answer.</summary>
    private static readonly TimeSpan Minimum = TimeSpan.FromSeconds(1);

    /// <summary>Lowers the transaction timeout of <paramref name="automation"/> to <paramref name="limit"/> until disposed.</summary>
    public static IDisposable Limit(AutomationBase automation, TimeSpan limit)
    {
        if (limit < Minimum) limit = Minimum;
        TimeSpan previous;
        try
        {
            previous = automation.TransactionTimeout;
            if (limit >= previous) return Unchanged.Instance;
            automation.TransactionTimeout = limit;
        }
        catch
        {
            return Unchanged.Instance;   // IUIAutomation2 is not available (it is on Windows 8 and later)
        }
        return new Restore(automation, previous);
    }

    private sealed class Restore(AutomationBase automation, TimeSpan previous) : IDisposable
    {
        public void Dispose()
        {
            try { automation.TransactionTimeout = previous; }
            catch { /* ignore */ }
        }
    }

    private sealed class Unchanged : IDisposable
    {
        public static readonly Unchanged Instance = new();

        public void Dispose() { }
    }
}
