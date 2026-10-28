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

    /// <summary>Whether the result ends with a timing breakdown (<c>--verbose</c>).</summary>
    public bool Verbose => Call.GetBool(CommandCall.VerboseKey);

    /// <summary>Time spent resolving targets (selectors and refs), for the timing breakdown.</summary>
    public TimeSpan FindTime { get; set; }

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
            ["close-window"] = CloseWindow,
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
        var total = Stopwatch.StartNew();
        CommandContext? ctx = null;
        var handlerTime = TimeSpan.Zero;
        TimeSpan? snapshotTime = null;
        try
        {
            ctx = new CommandContext(call, CliConfig.Load(call.Cwd));
            if (!_handlers.TryGetValue(call.Command, out var handler))
                throw new CliException($"Unknown command: {call.Command} (run flaui-cli --help for the list of commands)");

            ThrowIfInvokePending(call);
            string text;
            // Fallback searches on large trees share the command's timeout
            using (SearchBudget.Begin(ctx.Timeout))
            {
                try { text = handler(ctx); }
                finally { handlerTime = total.Elapsed; }
            }
            RecordIfNeeded(ctx);

            if (AutoSnapshot && ctx.Config.AutoSnapshot && _s.CurrentWindow is not null && !IsInvokePending
                && CommandCatalog.ActionCommands.Contains(call.Command))
            {
                var start = total.Elapsed;
                text += AutoSnapshotSection(ctx);
                snapshotTime = total.Elapsed - start;
            }

            text = text.TrimEnd();
            if (ctx.Verbose) text += "\n\n" + Timing(ctx, handlerTime, snapshotTime, total.Elapsed);
            return CommandResult.Success(text, ctx.Data.Count > 0 ? ctx.Data : null);
        }
        catch (AssertionFailedException ex)
        {
            return CommandResult.Failure(ex.Message, ExitCodes.AssertionFailed, FailureTiming(ctx, handlerTime, total.Elapsed));
        }
        catch (CliException ex)
        {
            return CommandResult.Failure(ex.Message, text: FailureTiming(ctx, handlerTime, total.Elapsed));
        }
        catch (Exception ex)
        {
            return CommandResult.Failure($"{ex.GetType().Name}: {ex.Message}", text: FailureTiming(ctx, handlerTime, total.Elapsed));
        }
    }

    /// <summary>
    /// The <c>--verbose</c> breakdown: finding the target element(s), the rest of the command (the action itself), the
    /// snapshot saved after actions, and the total inside the daemon. Values are also added to the result data.
    /// </summary>
    private static string Timing(CommandContext ctx, TimeSpan handler, TimeSpan? snapshot, TimeSpan total)
    {
        var action = handler - ctx.FindTime;
        var sb = new StringBuilder("### Timing\n");
        sb.AppendLine($"- find: {Seconds(ctx.FindTime)}");
        sb.AppendLine($"- action: {Seconds(action < TimeSpan.Zero ? TimeSpan.Zero : action)}");
        if (snapshot is { } s) sb.AppendLine($"- snapshot: {Seconds(s)}");
        sb.Append($"- total: {Seconds(total)}");

        ctx.Data["timing.findMs"] = ((long)ctx.FindTime.TotalMilliseconds).ToString();
        ctx.Data["timing.actionMs"] = ((long)Math.Max(0, action.TotalMilliseconds)).ToString();
        if (snapshot is { } sn) ctx.Data["timing.snapshotMs"] = ((long)sn.TotalMilliseconds).ToString();
        ctx.Data["timing.totalMs"] = ((long)total.TotalMilliseconds).ToString();
        return sb.ToString();
    }

    /// <summary>The breakdown for a failed command (a lookup that timed out is exactly what a performance report needs).</summary>
    private static string FailureTiming(CommandContext? ctx, TimeSpan handler, TimeSpan total) =>
        ctx is { Verbose: true } ? Timing(ctx, handler == TimeSpan.Zero ? total : handler, null, total) : "";

    internal static string Seconds(TimeSpan t) => $"{t.TotalSeconds.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)} s";

    // ───────────────────────── Pending invoke ─────────────────────────

    /// <summary>An InvokePattern call that did not return within its timeout (it opened a modal dialog).</summary>
    private sealed record PendingInvoke(Task Task, string Label);

    private PendingInvoke? _pendingInvoke;

    /// <summary>Whether an earlier invoke is still blocked; clears the state once it has returned.</summary>
    private bool IsInvokePending
    {
        get
        {
            if (_pendingInvoke is { Task.IsCompleted: true }) _pendingInvoke = null;
            return _pendingInvoke is not null;
        }
    }

    /// <summary>
    /// While an invoke is pending, UI Automation calls to the application wait until the modal dialog closes.
    /// Fail fast instead, except for commands that work without UI Automation.
    /// </summary>
    private void ThrowIfInvokePending(CommandCall call)
    {
        if (!IsInvokePending) return;
        var withoutUia = call.Command is "status" or "sleep" || (call.Command == "press" && call.Get("target") is null);
        if (withoutUia) return;
        throw new CliException(
            $"InvokePattern on {_pendingInvoke!.Label} has not returned yet (a modal dialog it opened is probably still open), " +
            "so UI Automation calls to this application would wait. Close the dialog first, for example with press Escape or press Enter");
    }

    // ───────────────────────── Shared helpers ─────────────────────────

    private IUiElement Resolve(CommandContext ctx, string raw)
    {
        var sw = Stopwatch.StartNew();
        IUiElement el;
        try { el = _resolver.Resolve(raw, ctx.Timeout); }
        finally { ctx.FindTime += sw.Elapsed; }
        if ((_s.Recorder is not null || _s.Doc is not null) && Selector.LooksLikeRef(raw) && !ctx.RefSelectors.ContainsKey(raw))
            ctx.RefSelectors[raw] = StableSelector(el);
        return el;
    }

    private IUiElement ResolveArg(CommandContext ctx, string arg = "target") => Resolve(ctx, ctx.Call.Require(arg));

    /// <summary>One lookup without waiting (wait / assert poll with it); counted as find time.</summary>
    private IUiElement? TryResolveOnce(CommandContext ctx, string raw)
    {
        var sw = Stopwatch.StartNew();
        try { return _resolver.TryResolveOnce(raw); }
        finally { ctx.FindTime += sw.Elapsed; }
    }

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

    /// <summary>
    /// Screen area of a window for screenshots: the visible frame reported by DWM when it is available, so the
    /// invisible resize borders of Windows 10/11 (included in UIA's bounds) do not show the background.
    /// </summary>
    private static Rectangle WindowBounds(IUiElement window)
    {
        var bounds = window.Bounds;
        try
        {
            if (NativeMethods.TryGetVisibleFrame(window.WindowHandle, out var frame) && bounds.Contains(frame)) return frame;
        }
        catch
        {
            // Fall back to the UIA bounds
        }
        return bounds;
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

    private string BuildSnapshot(IUiElement root, SnapshotOptions options, bool includePopups, bool complete = true)
    {
        var depth = SnapshotFormatter.CaptureDepthFor(options);
        var others = new List<ElementNode>();
        ElementNode? rootNode = null;
        string? rootId = null;
        if (includePopups)
        {
            rootId = root.RuntimeId;
            foreach (var w in _s.GetTopLevelWindows())
            {
                if (w.Equals(root) || w.IsOffscreen) continue;
                var tree = w.CaptureTree(depth, complete);
                // A window inside another top-level window (an MDI child) is already part of that window's tree:
                // reuse it instead of walking it a second time (only when the whole tree was captured)
                if (rootNode is null && depth is null && rootId is not null)
                    rootNode = tree.DescendantsAndSelf().FirstOrDefault(n => n.RuntimeId == rootId);
                others.Add(tree);
            }
        }

        var sb = new StringBuilder(SnapshotFormatter.Format(rootNode ?? root.CaptureTree(depth, complete), _s.Refs, options));
        foreach (var tree in others)
        {
            sb.AppendLine();
            // ... and print it only once, at the top
            sb.Append(SnapshotFormatter.Format(tree, _s.Refs, options, collapse: rootId));
        }
        return sb.ToString();
    }

    private string AutoSnapshotSection(CommandContext ctx)
    {
        try
        {
            var window = _s.RequireWindow();
            var options = new SnapshotOptions(ctx.Config.Snapshot.Depth, false, ctx.Config.Snapshot.IncludeOffscreen);
            // Not completed when UIA cuts a large tree: this runs after every action and is only a record
            var text = BuildSnapshot(window, options, includePopups: true, complete: false);
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
        var rec = ctx.Call.Clone().Remove(CommandCall.VerboseKey);
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

    /// <summary>In document mode, takes a screenshot before the action (shows what to operate) and outlines the target element.</summary>
    private void BeforeAction(CommandContext ctx, IUiElement? el, string description) => AddDocStep(ctx, el, description);

    /// <summary>
    /// In document mode, takes a screenshot after the action and outlines the target element. Used by commands
    /// that change a value (fill, type, select, check...), so the screenshot shows the result.
    /// </summary>
    private void AfterAction(CommandContext ctx, IUiElement? el, string description) => AddDocStep(ctx, el, description);

    private void AddDocStep(CommandContext ctx, IUiElement? el, string description)
    {
        var doc = _s.Doc;
        if (doc is null) return;

        var text = ctx.Call.Get("note") ?? description;
        var window = _s.RequireWindow();
        TryForeground(window);

        // A tooltip left over from an earlier hover belongs to another step
        try
        {
            if (NativeMethods.HideTooltips(window.ProcessId)) Thread.Sleep(100);
        }
        catch
        {
            // Best effort
        }

        var region = WindowBounds(window);
        var target = el?.Bounds ?? Rectangle.Empty;
        if (!target.IsEmpty) region = Rectangle.Union(region, target);

        using var bmp = Screenshotter.CaptureRegion(_s.Driver.Screen, region, out var captured);
        if (!target.IsEmpty) Screenshotter.Annotate(bmp, captured.Location, [target], doc.NextNumber, ctx.Config.Highlight.Padding, ctx.Config.Highlight.Outline);
        doc.AddStep(text, bmp, CommandFormatter.ToCli(Recordable(ctx)));
    }
}
