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

/// <summary>Context of a single command execution.</summary>
internal sealed class CommandContext(CommandCall call, CliConfig config)
{
    public CommandCall Call { get; } = call;

    public CliConfig Config { get; } = config;

    public int Timeout => Call.GetInt("timeout") ?? Config.Timeouts.Action;

    /// <summary>Stable selectors for the refs used by this command (for recording and documents).</summary>
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
/// Command dispatcher: turns a <see cref="CommandCall"/> into driver operations.
/// Shared by the daemon and the script runner. Must be called on the thread that created the
/// <see cref="AutomationSession"/>.
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

    /// <summary>Whether action commands save a snapshot file afterwards (disabled when running scripts).</summary>
    public bool AutoSnapshot { get; set; } = true;

    public CommandResult Execute(CommandCall call)
    {
        try
        {
            var ctx = new CommandContext(call, CliConfig.Load(call.Cwd));
            if (!_handlers.TryGetValue(call.Command, out var handler))
                throw new CliException($"Unknown command: {call.Command} (run flaui-cli --help for the list of commands)");

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
            return CommandResult.Failure($"{ex.GetType().Name}: {ex.Message}");
        }
    }

    // ───────────────────────── Shared helpers ─────────────────────────

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

    /// <summary>Short description of an element, for example <c>button "One" [ref=e21]</c>.</summary>
    private string Label(IUiElement el)
    {
        var name = el.Name;
        var r = _s.Refs.GetOrAssign(el);
        return name.Length > 0
            ? $"{el.Kind.Role()} \"{SnapshotFormatter.Escape(name)}\" [ref={r}]"
            : $"{el.Kind.Role()} [ref={r}]";
    }

    /// <summary>Human-friendly description used in documents, for example <c>the "One" button</c>.</summary>
    internal static string Friendly(IUiElement? el)
    {
        if (el is null) return "the focused element";
        var kind = el.Kind switch
        {
            ControlKind.Button or ControlKind.SplitButton => "button",
            ControlKind.Edit => "text box",
            ControlKind.CheckBox => "check box",
            ControlKind.ComboBox => "combo box",
            ControlKind.MenuItem => "menu item",
            ControlKind.TabItem => "tab",
            ControlKind.ListItem => "list item",
            ControlKind.TreeItem => "tree item",
            ControlKind.RadioButton => "radio button",
            ControlKind.Hyperlink => "link",
            ControlKind.Window => "window",
            ControlKind.List => "list",
            ControlKind.Tree => "tree",
            ControlKind.DataGrid or ControlKind.Table => "table",
            _ => "",
        };
        var name = el.Name.Length > 0 ? el.Name : el.AutomationId;
        if (name.Length > 40) name = name[..40] + "…";
        if (name.Length > 0) return kind.Length > 0 ? $"the \"{name}\" {kind}" : $"\"{name}\"";
        return kind.Length > 0 ? $"the {kind}" : "the element";
    }

    private void TryForeground(IUiElement window)
    {
        try
        {
            if (NativeMethods.GetForegroundWindow() != window.WindowHandle) window.SetForeground();
        }
        catch
        {
            // Keep going even if the window cannot be brought to the foreground
        }
    }

    private void ActivateWindow(IUiElement window)
    {
        _s.CurrentWindow = window;
        TryForeground(window);
    }

    /// <summary>Waits until the element can be operated (enabled), scrolls it into view if needed and brings the window forward.</summary>
    private void EnsureInteractable(CommandContext ctx, IUiElement el)
    {
        if (!Poll(ctx.Timeout, () => el.IsEnabled))
            throw new CliException($"{Label(el)} stayed disabled for {ctx.Timeout}ms and cannot be operated");
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
        return $"### Window\n- Title: {w.Name}\n- PID: {w.ProcessId}\n";
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
            return $"\n### Snapshot\n(Unable to take a snapshot: {ex.Message})";
        }
    }

    /// <summary>The command with refs replaced by stable selectors (for recording, documents and reports).</summary>
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

        // When recording started without an application, open / attach becomes the script's app section
        if (cmd is "open" or "attach" && recorder.App is null)
        {
            recorder.App = _s.AppInfo;
            return;
        }

        recorder.Add(Recordable(ctx));
    }

    /// <summary>In document mode, takes a screenshot before the action and outlines the target element.</summary>
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
