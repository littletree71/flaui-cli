using FlauiCli.Core.Abstractions;
using FlauiCli.Core.Docs;
using FlauiCli.Core.Native;
using FlauiCli.Core.Recording;
using FlauiCli.Core.Snapshot;

namespace FlauiCli.Core.Engine;

/// <summary>How the application was opened (written to the app section of recorded scripts).</summary>
public sealed record AppInfo(string? Launch, string? Args, string? Window, string? Attach);

/// <summary>
/// All state of one automation session. Except for <see cref="DriverFactory"/>, every member must be
/// accessed from the same thread.
/// </summary>
public sealed class AutomationSession : IDisposable
{
    /// <param name="name">Session name.</param>
    /// <param name="driverFactory">Creates drivers.</param>
    public AutomationSession(string name, Func<IUiDriver> driverFactory)
    {
        NativeMethods.EnsureDpiAware();
        Name = name;
        DriverFactory = driverFactory;
        Driver = driverFactory();
    }

    public string Name { get; }

    public IUiDriver Driver { get; }

    public Func<IUiDriver> DriverFactory { get; }

    public RefRegistry Refs { get; } = new();

    public DateTime StartedAt { get; } = DateTime.Now;

    public IAppProcess? App { get; internal set; }

    /// <summary>PID of the process this tool launched (the only kind of process that may be killed on close).</summary>
    public int? LaunchedPid { get; internal set; }

    public AppInfo? AppInfo { get; internal set; }

    private IUiElement? _currentWindow;

    public IUiElement? CurrentWindow
    {
        get => _currentWindow;
        internal set
        {
            _currentWindow = value;
            if (value is null) return;
            // Remember title and PID so the window can be found again after its element goes stale
            // (for example when a UWP app recreates its window).
            WindowTitle = value.Name;
            WindowProcessId = value.ProcessId;
            IsSharedHostWindow = value.ClassName == SharedHostWindowClass;
        }
    }

    /// <summary>Frame window class of UWP apps; its process (ApplicationFrameHost) hosts the windows of every UWP app.</summary>
    internal const string SharedHostWindowClass = "ApplicationFrameWindow";

    /// <summary>
    /// Whether the current window is hosted by a shared host process. In that case other top-level
    /// windows with the same PID belong to other apps and must not be included.
    /// </summary>
    public bool IsSharedHostWindow { get; private set; }

    /// <summary>Last known title of the current window.</summary>
    public string? WindowTitle { get; private set; }

    /// <summary>Last known PID of the current window (kept after its element goes stale).</summary>
    public int WindowProcessId { get; private set; }

    public ScriptRecorder? Recorder { get; internal set; }

    public InputCapture? InputCapture { get; internal set; }

    public DocBuilder? Doc { get; internal set; }

    public IUiElement RequireWindow()
    {
        if (CurrentWindow is null)
            throw new CliException("No application is open. Run open <app> or attach <process> first");

        if (CurrentWindow.IsAlive) return CurrentWindow;

        if (FindReplacementWindow() is { } replacement)
        {
            CurrentWindow = replacement;
            return replacement;
        }

        throw new CliException("The current window has been closed. Use windows / window to switch, or run open again");
    }

    /// <summary>
    /// Finds a replacement when the current window went stale: first a window with the same PID and title;
    /// any window is only accepted when the PID belongs to this session's application
    /// (so we never pick another app's window from a shared host such as ApplicationFrameHost).
    /// </summary>
    private IUiElement? FindReplacementWindow()
    {
        if (WindowProcessId == 0) return null;
        var candidates = Driver.GetTopLevelWindows(WindowProcessId).Where(w => w.IsAlive).ToList();
        var sameTitle = candidates.FirstOrDefault(w => w.Name == WindowTitle);
        if (sameTitle is not null) return sameTitle;
        return App is { HasExited: false } && App.ProcessId == WindowProcessId
            ? candidates.FirstOrDefault(w => w.Kind == ControlKind.Window)
            : null;
    }

    /// <summary>All top-level windows of the application's processes (including popups such as menus and drop-downs).</summary>
    public IReadOnlyList<IUiElement> GetTopLevelWindows()
    {
        var pids = new HashSet<int>();
        if (WindowProcessId != 0) pids.Add(WindowProcessId);
        if (App is { HasExited: false }) pids.Add(App.ProcessId);

        var result = new List<IUiElement>();
        foreach (var p in pids)
        {
            try
            {
                var windows = Driver.GetTopLevelWindows(p);
                // Shared host process: only keep the current window itself
                if (IsSharedHostWindow && p == WindowProcessId && CurrentWindow is not null)
                    windows = [.. windows.Where(CurrentWindow.Equals)];
                result.AddRange(windows);
            }
            catch
            {
                // The process may have just exited
            }
        }
        return result;
    }

    /// <summary>Search scope for elements: the current window plus the other top-level windows of the process (popups).</summary>
    public IReadOnlyList<IUiElement> SearchRoots()
    {
        var window = RequireWindow();
        var roots = new List<IUiElement> { window };
        foreach (var w in GetTopLevelWindows())
        {
            if (!w.Equals(window)) roots.Add(w);
        }
        return roots;
    }

    /// <summary>Forgets the current application (without closing it).</summary>
    internal void ResetApp()
    {
        try { App?.Dispose(); } catch { /* ignore */ }
        App = null;
        LaunchedPid = null;
        AppInfo = null;
        CurrentWindow = null;
        WindowTitle = null;
        WindowProcessId = 0;
        IsSharedHostWindow = false;
        Refs.Clear();
    }

    public void Dispose()
    {
        try { InputCapture?.Dispose(); } catch { /* ignore */ }
        try { App?.Dispose(); } catch { /* ignore */ }
        Driver.Dispose();
    }
}
