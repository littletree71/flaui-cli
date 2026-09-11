using FlauiCli.Core.Abstractions;
using FlauiCli.Core.Docs;
using FlauiCli.Core.Native;
using FlauiCli.Core.Recording;
using FlauiCli.Core.Snapshot;

namespace FlauiCli.Core.Engine;

/// <summary>應用程式是如何開啟的（錄製成腳本時寫入 app 區段）。</summary>
public sealed record AppInfo(string? Launch, string? Args, string? Window, string? Attach);

/// <summary>
/// 一個自動化 session 的所有狀態。除了 <see cref="DriverFactory"/> 之外，所有成員都必須在同一條執行緒上存取。
/// </summary>
public sealed class AutomationSession : IDisposable
{
    /// <param name="name">session 名稱。</param>
    /// <param name="driverFactory">建立驅動程式的工廠；真人操作錄製會在另一條執行緒上建立獨立的驅動程式。</param>
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

    /// <summary>由本工具啟動的程序 PID（只有這種程序才可能在 close 時被強制結束）。</summary>
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
            // 記住標題與 PID：元素失效後（例如 UWP 重建視窗）仍能找回同一個視窗
            WindowTitle = value.Name;
            WindowProcessId = value.ProcessId;
            IsSharedHostWindow = value.ClassName == SharedHostWindowClass;
        }
    }

    /// <summary>UWP 程式的外框視窗類別；其程序（ApplicationFrameHost）同時承載所有 UWP 程式的視窗。</summary>
    internal const string SharedHostWindowClass = "ApplicationFrameWindow";

    /// <summary>目前視窗是否由共用宿主程序承載（此時同 PID 的其他頂層視窗屬於別的程式，不可納入）。</summary>
    public bool IsSharedHostWindow { get; private set; }

    /// <summary>目前視窗最後已知的標題。</summary>
    public string? WindowTitle { get; private set; }

    /// <summary>目前視窗最後已知的 PID（元素失效後仍保留）。</summary>
    public int WindowProcessId { get; private set; }

    public ScriptRecorder? Recorder { get; internal set; }

    public InputRecorder? InputCapture { get; internal set; }

    public DocBuilder? Doc { get; internal set; }

    public IUiElement RequireWindow()
    {
        if (CurrentWindow is null)
            throw new CliException("尚未開啟或附加任何應用程式。請先執行 open <app> 或 attach <process>");

        if (CurrentWindow.IsAlive) return CurrentWindow;

        if (FindReplacementWindow() is { } replacement)
        {
            CurrentWindow = replacement;
            return replacement;
        }

        throw new CliException("目前的視窗已關閉。請用 windows / window 切換視窗，或重新 open");
    }

    /// <summary>
    /// 目前視窗失效時找替代視窗：先找同 PID、同標題的視窗；
    /// 只有 PID 是本 session 的應用程式程序時才退而求其次取任一視窗
    /// （避免在 ApplicationFrameHost 這類共用程序中誤抓其他程式的視窗）。
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

    /// <summary>目前應用程式相關程序的所有頂層視窗（包含選單、下拉清單等 popup）。</summary>
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
                // 共用宿主程序：只保留目前視窗本身
                if (IsSharedHostWindow && p == WindowProcessId && CurrentWindow is not null)
                    windows = [.. windows.Where(CurrentWindow.Equals)];
                result.AddRange(windows);
            }
            catch
            {
                // 程序可能剛結束
            }
        }
        return result;
    }

    /// <summary>元素搜尋範圍：目前視窗 + 同程序的其他頂層視窗（popup）。</summary>
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

    /// <summary>忘記目前的應用程式（不關閉它）。</summary>
    internal void ResetApp()
    {
        try { App?.Dispose(); } catch { /* 忽略 */ }
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
        try { InputCapture?.Dispose(); } catch { /* 忽略 */ }
        try { App?.Dispose(); } catch { /* 忽略 */ }
        Driver.Dispose();
    }
}
