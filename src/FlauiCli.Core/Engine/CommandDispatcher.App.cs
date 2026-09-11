using System.Diagnostics;
using System.Text;
using FlauiCli.Core.Abstractions;
using FlauiCli.Core.Commands;

namespace FlauiCli.Core.Engine;

// Application and session commands
public sealed partial class CommandDispatcher
{
    private string Open(CommandContext ctx)
    {
        var app = ctx.Call.Require("app");
        var args = JoinArgs(ctx.Call.GetList("args"));
        var windowHint = ctx.Call.Get("window");
        var timeout = ctx.Call.GetInt("timeout") ?? ctx.Config.Timeouts.Launch;

        // Switch to the new application when one is already open (the old one keeps running)
        if (_s.CurrentWindow is not null) _s.ResetApp();

        var isStoreApp = app.Contains('!') && !app.Contains('\\') && !app.Contains('/');
        // Remember the windows that already exist so a newly created one is preferred
        // (for example when a Calculator is already open and another one is launched)
        var existing = windowHint is null ? null : _s.Driver.GetTopLevelWindows().Select(w => w.WindowHandle).ToHashSet();
        IAppProcess process;
        try
        {
            process = isStoreApp
                ? _s.Driver.LaunchStoreApp(app, args)
                : _s.Driver.Launch(ResolveExecutable(ctx, app), args, ctx.Call.Cwd);
        }
        catch (Exception ex) when (ex is not CliException)
        {
            throw new CliException($"Cannot launch {app}: {ex.Message}", ex);
        }

        _s.App = process;
        _s.LaunchedPid = process.ProcessId;
        var window = WaitForWindow(process, windowHint, timeout, existing);
        ActivateWindow(window);
        _s.AppInfo = new AppInfo(app, args, windowHint, null);

        return $"### Result\nLaunched {app} (PID {process.ProcessId})\n{WindowSection()}";
    }

    private static string? JoinArgs(IReadOnlyList<string> args) =>
        args.Count == 0 ? null : string.Join(' ', args.Select(CommandFormatter.QuoteArg));

    private static string ResolveExecutable(CommandContext ctx, string app) =>
        app.Contains('\\') || app.Contains('/') || app.StartsWith('.') ? ctx.Call.ResolvePath(app) : app;

    /// <summary>A window must stay alive this long before it is accepted (UWP apps may show a transient window first).</summary>
    internal int WindowStableMs { get; init; } = 300;

    private IUiElement WaitForWindow(IAppProcess process, string? hint, int timeoutMs, HashSet<nint>? existing)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (hint is not null)
            {
                if (FindTopLevelByTitle(hint, process.ProcessId, existing) is { } w)
                {
                    Thread.Sleep(WindowStableMs);
                    if (w.IsAlive) return w;
                    continue;
                }
            }
            else
            {
                if (process.HasExited)
                    throw new CliException(
                        $"The process (PID {process.ProcessId}) has exited; it may be a UWP app or a launcher that hands off to another process. Add --window <window title>");
                if (process.GetMainWindow(TimeSpan.FromMilliseconds(500)) is { } mw) return mw;
            }
            Thread.Sleep(200);
        }

        throw new CliException(hint is null
            ? $"Timed out after {timeoutMs}ms waiting for the application window"
            : $"Timed out after {timeoutMs}ms waiting for the application window: no window titled \"{hint}\"");
    }

    /// <summary>
    /// Finds a top-level desktop window by title: exact matches first, then "contains".
    /// Among equal titles, new windows (not in <paramref name="existing"/>) win, then the preferred PID.
    /// </summary>
    private IUiElement? FindTopLevelByTitle(string title, int? preferPid, HashSet<nint>? existing = null)
    {
        var all = _s.Driver.GetTopLevelWindows()
            .Where(w => w.Kind is ControlKind.Window or ControlKind.Pane && w.Name.Length > 0)
            .ToList();
        int Rank(IUiElement w) =>
            (existing is not null && existing.Contains(w.WindowHandle) ? 2 : 0) + (w.ProcessId == preferPid ? 0 : 1);
        var found = MatchTitle(all, title, Rank);
        if (found is not null) return found;

        // Owned dialogs of WPF / WinForms apps live under their owner window in the UIA tree, not on the desktop
        return MatchTitle(ChildWindows(), title, _ => 0);
    }

    private static IUiElement? MatchTitle(IEnumerable<IUiElement> windows, string title, Func<IUiElement, int> rank)
    {
        var list = windows.ToList();
        return list.Where(w => string.Equals(w.Name, title, StringComparison.OrdinalIgnoreCase)).OrderBy(rank).FirstOrDefault()
               ?? list.Where(w => w.Name.Contains(title, StringComparison.OrdinalIgnoreCase)).OrderBy(rank).FirstOrDefault();
    }

    /// <summary>Windows inside the current window (owned dialogs). Internal window elements of a UWP shared host do not count.</summary>
    private IReadOnlyList<IUiElement> ChildWindows()
    {
        if (_s.IsSharedHostWindow || _s.CurrentWindow is not { IsAlive: true } current) return [];
        return [.. current.FindAll(new ElementQuery(Kind: ControlKind.Window)).Where(w => w.Name.Length > 0)];
    }

    private string Attach(CommandContext ctx)
    {
        var proc = ctx.Call.Get("process");
        var title = ctx.Call.Get("title");
        var timeout = ctx.Call.GetInt("timeout") ?? ctx.Config.Timeouts.Action;
        if (proc is null && title is null) throw new CliException("attach needs a PID / process name, or --title <window title>");

        if (_s.CurrentWindow is not null) _s.ResetApp();

        IAppProcess? process = null;
        if (proc is not null)
        {
            try
            {
                process = int.TryParse(proc, out var pid) ? _s.Driver.Attach(pid) : _s.Driver.Attach(proc);
            }
            catch (Exception ex)
            {
                throw new CliException($"Process not found: {proc}: {ex.Message}", ex);
            }
        }

        IUiElement? window = null;
        Poll(timeout, () =>
        {
            window = title is not null
                ? FindTopLevelByTitle(title, process?.ProcessId)
                : process!.GetMainWindow(TimeSpan.FromMilliseconds(300));
            return window is not null;
        });
        if (window is null)
            throw new CliException(title is null ? $"Process {proc} has no main window" : $"No window titled \"{title}\"");

        process ??= _s.Driver.Attach(window.ProcessId);
        _s.App = process;
        _s.LaunchedPid = null;
        _s.AppInfo = new AppInfo(null, null, title, proc ?? ProcessName(window.ProcessId));
        ActivateWindow(window);

        return $"### Result\nAttached to PID {window.ProcessId}\n{WindowSection()}";
    }

    private static string ProcessName(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return p.ProcessName;
        }
        catch
        {
            return pid.ToString();
        }
    }

    private string Close(CommandContext ctx)
    {
        var sb = new StringBuilder("### Result\n");
        if (_s.InputCapture is { } capture)
        {
            capture.Dispose();
            _s.InputCapture = null;
        }

        if (!ctx.Call.GetBool("keep-app") && _s.CurrentWindow is { } window)
        {
            var windowPid = window.ProcessId;
            try
            {
                if (window.IsAlive) window.TryClose();
            }
            catch
            {
                // The window may already have closed itself
            }

            // Only a process we launched ourselves, and which owns the window, is killed when it does not exit
            // (so shared processes such as ApplicationFrameHost are never killed by mistake)
            if (_s.LaunchedPid is int pid && _s.App is { HasExited: false } app && app.ProcessId == pid && pid == windowPid)
            {
                if (!app.WaitForExit(TimeSpan.FromSeconds(3)))
                {
                    app.Kill();
                    sb.AppendLine($"Process {pid} did not exit within 3 seconds and was killed");
                }
            }
            sb.AppendLine("Closed the application window");
        }

        _s.ResetApp();
        ctx.Data["shutdown"] = "true";
        sb.AppendLine($"Session '{_s.Name}' ended");
        return sb.ToString();
    }

    private string Status(CommandContext ctx)
    {
        var sb = new StringBuilder("### Session\n");
        sb.AppendLine($"- Name: {_s.Name}");
        sb.AppendLine($"- Driver: {_s.Driver.Description}");
        sb.AppendLine($"- Started: {_s.StartedAt:yyyy-MM-dd HH:mm:ss}");
        if (_s.AppInfo is { } app) sb.AppendLine($"- Application: {app.Launch ?? app.Attach}");
        if (_s.CurrentWindow is { } w)
        {
            var alive = w.IsAlive;
            sb.AppendLine($"- Current window: {(alive ? $"{w.Name} (PID {w.ProcessId})" : "(closed)")}");
        }
        else
        {
            sb.AppendLine("- Current window: (none)");
        }
        var recording = _s.Recorder is not null;
        var capturing = _s.InputCapture?.IsRunning == true;
        sb.AppendLine($"- Recording: {(recording ? $"in progress ({_s.Recorder!.Count} steps)" : "no")}{(capturing ? ", capturing input" : "")}");
        if (_s.InputCapture?.LastError is { } err) sb.AppendLine($"- Recording error: {err}");
        sb.AppendLine($"- Document: {(_s.Doc is { } d ? $"in progress ({d.Steps.Count} steps)" : "no")}");
        sb.AppendLine($"- Refs: {_s.Refs.Count}");

        ctx.Data["recording"] = recording ? "true" : "false";
        ctx.Data["capturing"] = capturing ? "true" : "false";
        ctx.Data["steps"] = (_s.Recorder?.Count ?? 0).ToString();
        if (_s.CurrentWindow is { IsAlive: true } cw) ctx.Data["window"] = cw.Name;
        return sb.ToString();
    }
}
