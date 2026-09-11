using System.Text;
using System.Text.RegularExpressions;
using FlauiCli.Core.Abstractions;
using FlauiCli.Core.Snapshot;
using FlauiCli.Core.Targeting;

namespace FlauiCli.Core.Engine;

// 視窗與檢視相關指令
public sealed partial class CommandDispatcher
{
    private IReadOnlyList<IUiElement> WindowList()
    {
        var current = _s.RequireWindow();
        var list = _s.GetTopLevelWindows().ToList();
        if (!list.Any(current.Equals)) list.Insert(0, current);
        foreach (var child in ChildWindows())
        {
            if (!list.Any(child.Equals)) list.Add(child);
        }
        return list;
    }

    private string Windows(CommandContext ctx)
    {
        var current = _s.RequireWindow();
        var sb = new StringBuilder("### Windows\n");
        var list = WindowList();
        for (var i = 0; i < list.Count; i++)
        {
            var w = list[i];
            sb.AppendLine($"- [{i}] {Label(w)}{(w.Equals(current) ? "（目前）" : "")}");
        }
        return sb.ToString();
    }

    private string SwitchWindow(CommandContext ctx)
    {
        var key = ctx.Call.Require("window");
        var list = WindowList();
        IUiElement? target;
        if (int.TryParse(key, out var index))
        {
            target = index >= 0 && index < list.Count ? list[index] : throw new CliException($"視窗索引超出範圍：{index}（共 {list.Count} 個）");
        }
        else if (Selector.LooksLikeRef(key))
        {
            target = Resolve(ctx, key);
        }
        else
        {
            target = list.FirstOrDefault(w => string.Equals(w.Name, key, StringComparison.OrdinalIgnoreCase))
                     ?? list.FirstOrDefault(w => w.Name.Contains(key, StringComparison.OrdinalIgnoreCase))
                     ?? FindTopLevelByTitle(key, _s.CurrentWindow?.ProcessId)
                     ?? throw new CliException($"找不到視窗：{key}");
        }

        ActivateWindow(target);
        return $"### Result\n已切換視窗\n{WindowSection()}";
    }

    private string Focus(CommandContext ctx)
    {
        if (ctx.Call.Get("target") is { } raw)
        {
            var el = Resolve(ctx, raw);
            BeforeAction(ctx, el, $"將焦點移到{Friendly(el)}");
            EnsureInteractable(ctx, el);
            el.Focus();
            return $"### Result\n{Label(el)} 已取得焦點";
        }

        var window = _s.RequireWindow();
        window.SetForeground();
        return $"### Result\n已將視窗帶到前景\n{WindowSection()}";
    }

    private string SetWindowState(CommandContext ctx, WindowStateKind state)
    {
        var window = _s.RequireWindow();
        if (!window.TrySetWindowState(state)) throw new CliException("此視窗不支援 WindowPattern，無法變更狀態");
        return $"### Result\n視窗已{state switch { WindowStateKind.Maximized => "最大化", WindowStateKind.Minimized => "最小化", _ => "還原" }}";
    }

    private string Resize(CommandContext ctx)
    {
        var w = ctx.Call.GetInt("width") ?? throw new CliException("缺少 width");
        var h = ctx.Call.GetInt("height") ?? throw new CliException("缺少 height");
        var window = _s.RequireWindow();
        window.TrySetWindowState(WindowStateKind.Normal);
        if (!window.TryResize(w, h)) throw new CliException("此視窗不允許調整大小");
        return $"### Result\n視窗大小已調整為 {w}x{h}";
    }

    private string MoveWindow(CommandContext ctx)
    {
        var x = ctx.Call.GetInt("x") ?? throw new CliException("缺少 x");
        var y = ctx.Call.GetInt("y") ?? throw new CliException("缺少 y");
        var window = _s.RequireWindow();
        window.TrySetWindowState(WindowStateKind.Normal);
        if (!window.TryMove(x, y)) throw new CliException("此視窗不允許移動");
        return $"### Result\n視窗已移動到 ({x}, {y})";
    }

    private string TakeSnapshot(CommandContext ctx)
    {
        var window = _s.RequireWindow();
        var root = ctx.Call.Get("root") is { } r ? Resolve(ctx, r) : window;
        var options = new SnapshotOptions(
            ctx.Call.GetInt("depth") ?? ctx.Config.Snapshot.Depth,
            ctx.Call.GetBool("boxes"),
            ctx.Call.GetBool("all") || ctx.Config.Snapshot.IncludeOffscreen);
        var text = BuildSnapshot(root, options, includePopups: root.Equals(window));

        if (ctx.Call.Get("filename") is { } file)
        {
            var path = ctx.Call.ResolvePath(file);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
            ctx.Data["path"] = path;
            return $"### Result\n已儲存 snapshot：{ctx.Relative(path)}";
        }

        return $"{WindowSection()}### Snapshot\n```yaml\n{text}\n```";
    }

    private string Find(CommandContext ctx)
    {
        const int limit = 50;
        var text = ctx.Call.Require("text");
        Regex? re = null;
        if (ctx.Call.GetBool("regex"))
        {
            try { re = new Regex(text, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)); }
            catch (ArgumentException ex) { throw new CliException($"正規表示式錯誤：{ex.Message}", ex); }
        }

        bool Match(string? s) => !string.IsNullOrEmpty(s) && (re?.IsMatch(s) ?? s.Contains(text, StringComparison.OrdinalIgnoreCase));

        var matches = _s.SearchRoots()
            .SelectMany(root => root.CaptureTree().DescendantsAndSelf())
            .Where(n => Match(n.Name) || Match(n.AutomationId) || Match(n.Value))
            .ToList();

        if (matches.Count == 0) return $"### Result\n找不到符合「{text}」的元素";

        var sb = new StringBuilder($"### Result\n找到 {matches.Count} 個元素{(matches.Count > limit ? $"（只列出前 {limit} 個）" : "")}\n");
        foreach (var n in matches.Take(limit))
            sb.AppendLine("- " + SnapshotFormatter.Line(n, _s.Refs.Assign(n), boxes: false));
        return sb.ToString();
    }

    private string Inspect(CommandContext ctx)
    {
        var el = ResolveArg(ctx);
        var b = el.Bounds;
        var sb = new StringBuilder("### Element\n");
        sb.AppendLine($"- ref：{_s.Refs.GetOrAssign(el)}");
        sb.AppendLine($"- 建議 selector：{StableSelector(el)}");
        sb.AppendLine($"- ControlType：{el.Kind}");
        sb.AppendLine($"- Name：{el.Name}");
        sb.AppendLine($"- AutomationId：{el.AutomationId}");
        sb.AppendLine($"- ClassName：{el.ClassName}");
        sb.AppendLine($"- FrameworkId：{el.FrameworkId}");
        sb.AppendLine($"- IsEnabled：{el.IsEnabled}");
        sb.AppendLine($"- IsOffscreen：{el.IsOffscreen}");
        sb.AppendLine($"- HasKeyboardFocus：{el.HasFocus}");
        sb.AppendLine($"- BoundingRectangle：{b.X},{b.Y},{b.Width},{b.Height}");
        sb.AppendLine($"- ProcessId：{el.ProcessId}");
        if (el.Value is { } v) sb.AppendLine($"- Value：{v}{(el.IsReadOnly == true ? "（唯讀）" : "")}");
        if (el.Toggle is { } t) sb.AppendLine($"- ToggleState：{t}");
        if (el.Expand is { } e) sb.AppendLine($"- ExpandCollapseState：{e}");
        if (el.IsSelected is { } s) sb.AppendLine($"- IsSelected：{s}");
        foreach (var name in new[] { "HelpText", "ItemStatus", "LocalizedControlType", "AcceleratorKey", "AccessKey" })
        {
            if (el.GetProperty(name) is { Length: > 0 } pv) sb.AppendLine($"- {name}：{pv}");
        }
        sb.AppendLine($"- 支援的 Pattern：{string.Join(", ", el.SupportedPatterns)}");
        return sb.ToString();
    }
}
