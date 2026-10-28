using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using FlauiCli.Core.Abstractions;
using FlauiCli.Core.Commands;
using FlauiCli.Core.Native;
using FlauiCli.Core.Protocol;

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
    /// How a window title is matched. By default an exact title wins over one that starts with the text, which wins
    /// over one that only contains it (so an MDI child is preferred over the MDI parent, whose title contains the
    /// child's title). <see cref="ExactOnly"/> and <see cref="Pattern"/> (a regular expression) narrow that down.
    /// </summary>
    internal sealed record TitleMatch(string Text, bool ExactOnly = false, Regex? Pattern = null)
    {
        public static TitleMatch FromCall(CommandCall call, string title)
        {
            var exact = call.GetBool("exact");
            if (!call.GetBool("regex")) return new TitleMatch(title, exact);
            if (exact) throw new CliException("Use either --exact or --regex, not both");
            try { return new TitleMatch(title, Pattern: new Regex(title, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))); }
            catch (ArgumentException ex) { throw new CliException($"Invalid regular expression: {ex.Message}", ex); }
        }

        /// <summary>Predicates tried in order; the first one that matches any window decides.</summary>
        public IEnumerable<Func<string, bool>> Passes()
        {
            if (Pattern is not null)
            {
                yield return t => Pattern.IsMatch(t);
                yield break;
            }
            yield return t => string.Equals(t, Text, StringComparison.OrdinalIgnoreCase);
            if (ExactOnly) yield break;
            yield return t => t.StartsWith(Text, StringComparison.OrdinalIgnoreCase);
            yield return t => t.Contains(Text, StringComparison.OrdinalIgnoreCase);
        }

        public override string ToString() => Pattern is not null ? $"matching /{Text}/" : ExactOnly ? $"titled exactly \"{Text}\"" : $"titled \"{Text}\"";
    }

    private IUiElement? FindTopLevelByTitle(string title, int? preferPid, HashSet<nint>? existing = null) =>
        FindTopLevelByTitle(new TitleMatch(title), preferPid, existing);

    /// <summary>
    /// Finds a window by title on the desktop, among the application's owned dialogs and among the windows only Win32
    /// lists, trying each pass of <paramref name="match"/> on all of them before the next (looser) pass.
    /// Among equal matches, new windows (not in <paramref name="existing"/>) win, then the preferred PID.
    /// <para>
    /// Windows in <paramref name="existing"/> (open before the application was launched) only match on the first pass, the
    /// exact title: while a slow application (a UWP app) has not shown its window yet, a looser pass would otherwise accept
    /// an unrelated window whose title merely starts with or contains the text, such as an editor showing
    /// "calculator.flow.yaml", and a later close would close that window.
    /// </para>
    /// </summary>
    private IUiElement? FindTopLevelByTitle(TitleMatch match, int? preferPid, HashSet<nint>? existing = null)
    {
        var all = _s.Driver.GetTopLevelWindows()
            .Where(w => w.Kind is ControlKind.Window or ControlKind.Pane && w.Name.Length > 0)
            .ToList();
        int Rank(IUiElement w) =>
            (existing is not null && existing.Contains(w.WindowHandle) ? 2 : 0) + (w.ProcessId == preferPid ? 0 : 1);

        // Both are only needed when the desktop has no match, and are fetched once for all passes
        IReadOnlyList<IUiElement>? owned = null;
        List<NativeMethods.TopLevelWindow>? win32 = null;

        var pass = 0;
        foreach (var matches in match.Passes())
        {
            var loose = pass++ > 0;
            bool Allowed(nint hwnd) => !loose || existing is null || !existing.Contains(hwnd);

            var found = all.Where(w => matches(w.Name) && Allowed(w.WindowHandle)).OrderBy(Rank).FirstOrDefault();
            if (found is not null) return found;

            // Owned dialogs of WPF / WinForms apps live under their owner window in the UIA tree, not on the desktop
            owned ??= ChildWindows();
            found = owned.FirstOrDefault(w => matches(w.Name) && Allowed(w.WindowHandle));
            if (found is not null) return found;

            // Last resort: windows Win32 sees but UIA does not list (for example ShowDialog dialogs of .NET Framework
            // WinForms MDI apps). Match on the Win32 title first so only candidates are turned into UIA elements.
            win32 ??= [.. NativeMethods.VisibleTopLevelWindows()
                .Where(w => w.Title.Length > 0)
                .OrderBy(w => (existing is not null && existing.Contains(w.Handle) ? 2 : 0) + (w.ProcessId == preferPid ? 0 : 1))];
            var hit = win32.FirstOrDefault(w => matches(w.Title) && Allowed(w.Handle));
            if (hit.Handle != 0) return _s.Driver.FromHandle(hit.Handle);
        }
        return null;
    }

    /// <summary>
    /// Owned dialogs of the application: windows inside the current window, inside the other top-level windows
    /// of the application (an MDI child's dialog is owned by the MDI parent), and titled windows of the
    /// application that Win32 sees but UIA does not list. Internal window elements of a UWP shared host do not count.
    /// </summary>
    private IReadOnlyList<IUiElement> ChildWindows()
    {
        if (_s.IsSharedHostWindow || _s.CurrentWindow is not { IsAlive: true } current) return [];
        var result = new List<IUiElement>();
        void Add(IUiElement w)
        {
            if (w.Name.Length > 0 && !w.Equals(current) && !result.Any(w.Equals)) result.Add(w);
        }

        var topLevel = _s.GetTopLevelWindows();
        foreach (var root in topLevel.Prepend(current).Distinct())
        {
            try
            {
                foreach (var w in root.FindAll(new ElementQuery(Kind: ControlKind.Window))) Add(w);
            }
            catch
            {
                // The window may have just closed
            }
        }

        var known = topLevel.Concat(result).Append(current).Select(w => w.WindowHandle).Where(h => h != 0).ToHashSet();
        foreach (var w in NativeMethods.VisibleTopLevelWindows(_s.WindowProcessId))
        {
            if (w.Title.Length == 0 || known.Contains(w.Handle)) continue;
            if (_s.Driver.FromHandle(w.Handle) is { } el) Add(el);
        }
        return result;
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
        var invokePending = IsInvokePending;
        if (invokePending)
        {
            // Do not touch UI Automation: it waits for the pending invoke
            sb.AppendLine($"- Current window: {_s.WindowTitle} (PID {_s.WindowProcessId})");
            sb.AppendLine($"- Waiting: InvokePattern on {_pendingInvoke!.Label} has not returned (a modal dialog is probably open)");
        }
        else if (_s.CurrentWindow is { } w)
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
        var unavailable = DesktopUnavailable();
        sb.AppendLine($"- Input: {(unavailable is null ? "available" : $"unavailable ({unavailable})")}");
        ctx.Data["inputAvailable"] = unavailable is null ? "true" : "false";

        ctx.Data["recording"] = recording ? "true" : "false";
        ctx.Data["capturing"] = capturing ? "true" : "false";
        ctx.Data["steps"] = (_s.Recorder?.Count ?? 0).ToString();
        if (invokePending)
        {
            ctx.Data["invokePending"] = "true";
            if (_s.WindowTitle is { } title) ctx.Data["window"] = title;
        }
        else if (_s.CurrentWindow is { IsAlive: true } cw)
        {
            ctx.Data["window"] = cw.Name;
        }
        return sb.ToString();
    }
}
