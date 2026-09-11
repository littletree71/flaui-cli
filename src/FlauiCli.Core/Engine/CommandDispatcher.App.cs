using System.Diagnostics;
using System.Text;
using FlauiCli.Core.Abstractions;
using FlauiCli.Core.Commands;

namespace FlauiCli.Core.Engine;

// 應用程式與 session 相關指令
public sealed partial class CommandDispatcher
{
    private string Open(CommandContext ctx)
    {
        var app = ctx.Call.Require("app");
        var args = JoinArgs(ctx.Call.GetList("args"));
        var windowHint = ctx.Call.Get("window");
        var timeout = ctx.Call.GetInt("timeout") ?? ctx.Config.Timeouts.Launch;

        // 已有應用程式時切換到新的（不關閉舊的）
        if (_s.CurrentWindow is not null) _s.ResetApp();

        var isStoreApp = app.Contains('!') && !app.Contains('\\') && !app.Contains('/');
        // 記下啟動前已存在的視窗，之後優先選新出現的（例如已開著一個小算盤時再開一個）
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
            throw new CliException($"無法啟動 {app}：{ex.Message}", ex);
        }

        _s.App = process;
        _s.LaunchedPid = process.ProcessId;
        var window = WaitForWindow(process, windowHint, timeout, existing);
        ActivateWindow(window);
        _s.AppInfo = new AppInfo(app, args, windowHint, null);

        return $"### Result\n已啟動 {app}（PID {process.ProcessId}）\n{WindowSection()}";
    }

    private static string? JoinArgs(IReadOnlyList<string> args) =>
        args.Count == 0 ? null : string.Join(' ', args.Select(CommandFormatter.QuoteArg));

    private static string ResolveExecutable(CommandContext ctx, string app) =>
        app.Contains('\\') || app.Contains('/') || app.StartsWith('.') ? ctx.Call.ResolvePath(app) : app;

    /// <summary>視窗需持續存在這麼久才採用（UWP 啟動時可能先出現隨即被取代的過渡視窗）。</summary>
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
                        $"程序（PID {process.ProcessId}）已結束，可能是 UWP 或把工作轉交給其他程序的啟動器。請加上 --window <視窗標題>");
                if (process.GetMainWindow(TimeSpan.FromMilliseconds(500)) is { } mw) return mw;
            }
            Thread.Sleep(200);
        }

        throw new CliException(hint is null
            ? $"等待應用程式視窗逾時（{timeoutMs}ms）"
            : $"等待應用程式視窗逾時（{timeoutMs}ms）：找不到標題為「{hint}」的視窗");
    }

    /// <summary>
    /// 在桌面頂層視窗中依標題尋找：完全相符優先，其次包含；
    /// 同名時「不在 <paramref name="existing"/> 內的新視窗」優先，其次同 PID。
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

        // WPF / WinForms 的附屬對話框在 UIA 樹中位於擁有者視窗之下，而不是桌面頂層
        return MatchTitle(ChildWindows(), title, _ => 0);
    }

    private static IUiElement? MatchTitle(IEnumerable<IUiElement> windows, string title, Func<IUiElement, int> rank)
    {
        var list = windows.ToList();
        return list.Where(w => string.Equals(w.Name, title, StringComparison.OrdinalIgnoreCase)).OrderBy(rank).FirstOrDefault()
               ?? list.Where(w => w.Name.Contains(title, StringComparison.OrdinalIgnoreCase)).OrderBy(rank).FirstOrDefault();
    }

    /// <summary>目前視窗之內的子視窗（附屬對話框）。UWP 共用宿主的內部 window 元素不算。</summary>
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
        if (proc is null && title is null) throw new CliException("attach 需要 PID / 程序名稱，或 --title <視窗標題>");

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
                throw new CliException($"找不到程序 {proc}：{ex.Message}", ex);
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
            throw new CliException(title is null ? $"程序 {proc} 沒有可用的主視窗" : $"找不到標題為「{title}」的視窗");

        process ??= _s.Driver.Attach(window.ProcessId);
        _s.App = process;
        _s.LaunchedPid = null;
        _s.AppInfo = new AppInfo(null, null, title, proc ?? ProcessName(window.ProcessId));
        ActivateWindow(window);

        return $"### Result\n已附加到 PID {window.ProcessId}\n{WindowSection()}";
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
                // 視窗可能已自行關閉
            }

            // 只有自己啟動、且擁有該視窗的程序，才會在未自行結束時強制結束（避免誤殺 ApplicationFrameHost 等共用程序）
            if (_s.LaunchedPid is int pid && _s.App is { HasExited: false } app && app.ProcessId == pid && pid == windowPid)
            {
                if (!app.WaitForExit(TimeSpan.FromSeconds(3)))
                {
                    app.Kill();
                    sb.AppendLine($"程序 {pid} 未在 3 秒內結束，已強制結束");
                }
            }
            sb.AppendLine("已關閉應用程式視窗");
        }

        _s.ResetApp();
        ctx.Data["shutdown"] = "true";
        sb.AppendLine($"session「{_s.Name}」已結束");
        return sb.ToString();
    }

    private string Status(CommandContext ctx)
    {
        var sb = new StringBuilder("### Session\n");
        sb.AppendLine($"- 名稱：{_s.Name}");
        sb.AppendLine($"- 驅動程式：{_s.Driver.Description}");
        sb.AppendLine($"- 啟動時間：{_s.StartedAt:yyyy-MM-dd HH:mm:ss}");
        if (_s.AppInfo is { } app) sb.AppendLine($"- 應用程式：{app.Launch ?? app.Attach}");
        if (_s.CurrentWindow is { } w)
        {
            var alive = w.IsAlive;
            sb.AppendLine($"- 目前視窗：{(alive ? w.Name : "（已關閉）")}{(alive ? $"（PID {w.ProcessId}）" : "")}");
        }
        else
        {
            sb.AppendLine("- 目前視窗：（無）");
        }
        var recording = _s.Recorder is not null;
        var capturing = _s.InputCapture?.IsRunning == true;
        sb.AppendLine($"- 錄製：{(recording ? $"進行中（{_s.Recorder!.Count} 步）" : "否")}{(capturing ? "，正在捕捉真人操作" : "")}");
        if (_s.InputCapture?.LastError is { } err) sb.AppendLine($"- 錄製錯誤：{err}");
        sb.AppendLine($"- 操作文件：{(_s.Doc is { } d ? $"進行中（{d.Steps.Count} 步）" : "否")}");
        sb.AppendLine($"- refs：{_s.Refs.Count}");

        ctx.Data["recording"] = recording ? "true" : "false";
        ctx.Data["capturing"] = capturing ? "true" : "false";
        ctx.Data["steps"] = (_s.Recorder?.Count ?? 0).ToString();
        if (_s.CurrentWindow is { IsAlive: true } cw) ctx.Data["window"] = cw.Name;
        return sb.ToString();
    }
}
