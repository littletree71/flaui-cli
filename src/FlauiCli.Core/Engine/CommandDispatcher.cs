using System.Diagnostics;
using System.Drawing;
using System.Text;
using FlauiCli.Core.Abstractions;
using FlauiCli.Core.Commands;
using FlauiCli.Core.Imaging;
using FlauiCli.Core.Native;
using FlauiCli.Core.Protocol;
using FlauiCli.Core.Snapshot;
using FlauiCli.Core.Targeting;

namespace FlauiCli.Core.Engine;

/// <summary>單次指令執行的上下文。</summary>
internal sealed class CommandContext(CommandCall call, CliConfig config)
{
    public CommandCall Call { get; } = call;

    public CliConfig Config { get; } = config;

    public int Timeout => Call.GetInt("timeout") ?? Config.Timeouts.Action;

    /// <summary>本次用到的 ref 對應的穩定 selector（錄製與文件用）。</summary>
    public Dictionary<string, string> RefSelectors { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> Data { get; } = [];

    public string OutputDir => Config.ResolveOutputDir(Call.Cwd);

    public string Relative(string path)
    {
        var rel = Path.GetRelativePath(Call.Cwd ?? Environment.CurrentDirectory, path);
        return rel.Replace('\\', '/');
    }
}

/// <summary>
/// 指令分派器：把 <see cref="CommandCall"/> 轉成對驅動程式的操作。
/// Daemon 與腳本執行器共用。必須在建立 <see cref="AutomationSession"/> 的同一條執行緒上呼叫。
/// </summary>
public sealed partial class CommandDispatcher
{
    private readonly AutomationSession _s;
    private readonly TargetResolver _resolver;
    private readonly Dictionary<string, Func<CommandContext, string>> _handlers;

    public CommandDispatcher(AutomationSession session)
    {
        _s = session;
        _resolver = new TargetResolver(session);
        _handlers = new(StringComparer.OrdinalIgnoreCase)
        {
            ["open"] = Open,
            ["attach"] = Attach,
            ["close"] = Close,
            ["status"] = Status,
            ["windows"] = Windows,
            ["window"] = SwitchWindow,
            ["focus"] = Focus,
            ["maximize"] = c => SetWindowState(c, WindowStateKind.Maximized),
            ["minimize"] = c => SetWindowState(c, WindowStateKind.Minimized),
            ["restore"] = c => SetWindowState(c, WindowStateKind.Normal),
            ["resize"] = Resize,
            ["move"] = MoveWindow,
            ["snapshot"] = TakeSnapshot,
            ["find"] = Find,
            ["inspect"] = Inspect,
            ["click"] = Click,
            ["dblclick"] = DoubleClick,
            ["hover"] = Hover,
            ["fill"] = Fill,
            ["type"] = TypeText,
            ["press"] = Press,
            ["select"] = Select,
            ["check"] = c => SetChecked(c, true),
            ["uncheck"] = c => SetChecked(c, false),
            ["expand"] = c => ExpandCollapse(c, true),
            ["collapse"] = c => ExpandCollapse(c, false),
            ["invoke"] = Invoke,
            ["scroll"] = Scroll,
            ["drag"] = Drag,
            ["get"] = Get,
            ["wait"] = WaitFor,
            ["wait-window"] = WaitWindow,
            ["assert"] = Assert,
            ["sleep"] = Sleep,
            ["screenshot"] = Screenshot,
            ["record"] = Record,
            ["doc"] = Doc,
        };
    }

    public AutomationSession Session => _s;

    /// <summary>動作後是否自動存 snapshot 檔（腳本執行時關閉）。</summary>
    public bool AutoSnapshot { get; set; } = true;

    public CommandResult Execute(CommandCall call)
    {
        try
        {
            var ctx = new CommandContext(call, CliConfig.Load(call.Cwd));
            if (!_handlers.TryGetValue(call.Command, out var handler))
                throw new CliException($"未知的指令：{call.Command}（執行 flaui-cli --help 查看可用指令）");

            var text = handler(ctx);
            RecordIfNeeded(ctx);

            if (AutoSnapshot && ctx.Config.AutoSnapshot && _s.CurrentWindow is not null
                && CommandCatalog.ActionCommands.Contains(call.Command))
            {
                text += AutoSnapshotSection(ctx);
            }

            return CommandResult.Success(text.TrimEnd(), ctx.Data.Count > 0 ? ctx.Data : null);
        }
        catch (AssertionFailedException ex)
        {
            return CommandResult.Failure(ex.Message, ExitCodes.AssertionFailed);
        }
        catch (CliException ex)
        {
            return CommandResult.Failure(ex.Message);
        }
        catch (Exception ex)
        {
            return CommandResult.Failure($"{ex.GetType().Name}：{ex.Message}");
        }
    }

    // ───────────────────────── 共用工具 ─────────────────────────

    private IUiElement Resolve(CommandContext ctx, string raw)
    {
        var el = _resolver.Resolve(raw, ctx.Timeout);
        if ((_s.Recorder is not null || _s.Doc is not null) && Selector.LooksLikeRef(raw) && !ctx.RefSelectors.ContainsKey(raw))
            ctx.RefSelectors[raw] = StableSelector(el);
        return el;
    }

    private IUiElement ResolveArg(CommandContext ctx, string arg = "target") => Resolve(ctx, ctx.Call.Require(arg));

    private string StableSelector(IUiElement el)
    {
        try { return SelectorGenerator.Generate(el, _s.SearchRoots()); }
        catch { return $"type={el.Kind}"; }
    }

    /// <summary>元素的簡短描述，例如 <c>button "One" [ref=e21]</c>。</summary>
    private string Label(IUiElement el)
    {
        var name = el.Name;
        var r = _s.Refs.GetOrAssign(el);
        return name.Length > 0
            ? $"{el.Kind.Role()} \"{SnapshotFormatter.Escape(name)}\" [ref={r}]"
            : $"{el.Kind.Role()} [ref={r}]";
    }

    /// <summary>給操作文件用的中文描述，例如「One」按鈕。</summary>
    internal static string Friendly(IUiElement? el)
    {
        if (el is null) return "目前焦點";
        var kind = el.Kind switch
        {
            ControlKind.Button or ControlKind.SplitButton => "按鈕",
            ControlKind.Edit => "文字方塊",
            ControlKind.CheckBox => "核取方塊",
            ControlKind.ComboBox => "下拉選單",
            ControlKind.MenuItem => "選單項目",
            ControlKind.TabItem => "索引標籤",
            ControlKind.ListItem => "清單項目",
            ControlKind.TreeItem => "樹狀節點",
            ControlKind.RadioButton => "選項按鈕",
            ControlKind.Hyperlink => "連結",
            ControlKind.Window => "視窗",
            ControlKind.List => "清單",
            ControlKind.Tree => "樹狀清單",
            ControlKind.DataGrid or ControlKind.Table => "表格",
            _ => "",
        };
        var name = el.Name.Length > 0 ? el.Name : el.AutomationId;
        if (name.Length > 40) name = name[..40] + "…";
        return name.Length > 0 ? $"「{name}」{kind}" : kind.Length > 0 ? kind : "元素";
    }

    private void TryForeground(IUiElement window)
    {
        try
        {
            if (NativeMethods.GetForegroundWindow() != window.WindowHandle) window.SetForeground();
        }
        catch
        {
            // 帶到前景失敗時仍繼續操作
        }
    }

    private void ActivateWindow(IUiElement window)
    {
        _s.CurrentWindow = window;
        TryForeground(window);
    }

    /// <summary>等待元素可操作（啟用），必要時捲動到可見範圍，並把視窗帶到前景。</summary>
    private void EnsureInteractable(CommandContext ctx, IUiElement el)
    {
        if (!Poll(ctx.Timeout, () => el.IsEnabled))
            throw new CliException($"{Label(el)} 在 {ctx.Timeout}ms 內一直是停用狀態，無法操作");
        if (el.IsOffscreen) el.TryScrollIntoView();
        TryForeground(_s.RequireWindow());
    }

    internal static bool Poll(int timeoutMs, Func<bool> condition, int intervalMs = 150)
    {
        var sw = Stopwatch.StartNew();
        while (true)
        {
            if (condition()) return true;
            if (sw.ElapsedMilliseconds >= timeoutMs) return false;
            Thread.Sleep(intervalMs);
        }
    }

    private static string Stamp() => DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");

    private string WindowSection()
    {
        var w = _s.CurrentWindow;
        if (w is null) return "";
        return $"### Window\n- 標題：{w.Name}\n- PID：{w.ProcessId}\n";
    }

    private string BuildSnapshot(IUiElement root, SnapshotOptions options, bool includePopups)
    {
        var sb = new StringBuilder(SnapshotFormatter.Format(root.CaptureTree(), _s.Refs, options));
        if (includePopups)
        {
            foreach (var w in _s.GetTopLevelWindows())
            {
                if (w.Equals(root) || w.IsOffscreen) continue;
                sb.AppendLine();
                sb.Append(SnapshotFormatter.Format(w.CaptureTree(), _s.Refs, options));
            }
        }
        return sb.ToString();
    }

    private string AutoSnapshotSection(CommandContext ctx)
    {
        try
        {
            var window = _s.RequireWindow();
            var options = new SnapshotOptions(ctx.Config.Snapshot.Depth, false, ctx.Config.Snapshot.IncludeOffscreen);
            var text = BuildSnapshot(window, options, includePopups: true);
            var path = Path.Combine(ctx.OutputDir, $"snapshot-{Stamp()}.yml");
            File.WriteAllText(path, text);
            return $"\n{WindowSection()}### Snapshot\n[Snapshot]({ctx.Relative(path)})";
        }
        catch (Exception ex)
        {
            return $"\n### Snapshot\n（無法取得 snapshot：{ex.Message}）";
        }
    }

    /// <summary>把 ref 換成穩定 selector 後的指令（錄製、文件、報告用）。</summary>
    private static CommandCall Recordable(CommandContext ctx)
    {
        var rec = ctx.Call.Clone();
        rec.Cwd = null;
        foreach (var arg in CommandCatalog.TargetArgNames)
        {
            if (rec.Get(arg) is null) continue;
            var items = rec.GetList(arg).Select(i => Selector.LooksLikeRef(i) ? ctx.RefSelectors.GetValueOrDefault(i, i) : i);
            rec.Set(arg, string.Join(CommandCall.MultiValueSeparator, items));
        }
        return rec;
    }

    private void RecordIfNeeded(CommandContext ctx)
    {
        var recorder = _s.Recorder;
        if (recorder is null) return;
        var cmd = ctx.Call.Command;
        if (CommandCatalog.ReadOnlyCommands.Contains(cmd) || cmd is "close" or "status") return;

        // 錄製開始前沒有應用程式資訊時，把 open / attach 當成腳本的 app 區段
        if (cmd is "open" or "attach" && recorder.App is null)
        {
            recorder.App = _s.AppInfo;
            return;
        }

        recorder.Add(Recordable(ctx));
    }

    /// <summary>操作文件模式下，在動作執行前截圖並以紅框標註目標元素。</summary>
    private void BeforeAction(CommandContext ctx, IUiElement? el, string description)
    {
        var doc = _s.Doc;
        if (doc is null) return;

        var text = ctx.Call.Get("note") ?? description;
        var window = _s.RequireWindow();
        TryForeground(window);

        var region = window.Bounds;
        var target = el?.Bounds ?? Rectangle.Empty;
        if (!target.IsEmpty) region = Rectangle.Union(region, target);

        using var bmp = Screenshotter.CaptureRegion(_s.Driver.Screen, region, out var captured);
        if (!target.IsEmpty) Screenshotter.Annotate(bmp, captured.Location, [target], doc.NextNumber);
        doc.AddStep(text, bmp, CommandFormatter.ToCli(Recordable(ctx)));
    }
}
