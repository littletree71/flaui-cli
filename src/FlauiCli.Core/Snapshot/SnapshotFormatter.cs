using System.Text;
using FlauiCli.Core.Abstractions;
using FlauiCli.Core.Targeting;

namespace FlauiCli.Core.Snapshot;

public sealed record SnapshotOptions(int MaxDepth = 0, bool Boxes = false, bool All = false);

/// <summary>
/// 將 UI 樹輸出成仿 Playwright aria snapshot 的 YAML 風格文字：
/// <code>- button "One" [ref=e21] id=num1Button</code>
/// </summary>
internal static class SnapshotFormatter
{
    private const int MaxTextLength = 120;

    private static readonly HashSet<ControlKind> ContainerKinds =
        [ControlKind.Pane, ControlKind.Group, ControlKind.Custom, ControlKind.Unknown];

    private static readonly HashSet<ControlKind> ValueKinds =
        [ControlKind.Edit, ControlKind.ComboBox, ControlKind.Document, ControlKind.Spinner, ControlKind.Slider, ControlKind.ProgressBar];

    public static string Format(ElementNode root, RefRegistry refs, SnapshotOptions options)
    {
        var sb = new StringBuilder();
        Emit(sb, root, refs, options, indent: 0, depth: 0, isRoot: true);
        return sb.ToString().TrimEnd();
    }

    /// <summary>產生單行描述（find 指令也會用到）。</summary>
    public static string Line(ElementNode n, string r, bool boxes)
    {
        var sb = new StringBuilder();
        sb.Append(n.Kind.Role());
        if (n.Name.Length > 0) sb.Append(" \"").Append(Escape(n.Name)).Append('"');
        sb.Append(" [ref=").Append(r).Append(']');
        if (n.AutomationId.Length > 0 && n.AutomationId != n.Name) sb.Append(" id=").Append(Selector.Quote(n.AutomationId));
        if (!n.IsEnabled) sb.Append(" [disabled]");
        switch (n.Toggle)
        {
            case ToggleValue.On: sb.Append(" [checked]"); break;
            case ToggleValue.Indeterminate: sb.Append(" [mixed]"); break;
        }
        switch (n.Expand)
        {
            case ExpandValue.Expanded: sb.Append(" [expanded]"); break;
            case ExpandValue.Collapsed: sb.Append(" [collapsed]"); break;
        }
        if (n.IsSelected == true) sb.Append(" [selected]");
        if (n.HasFocus) sb.Append(" [focused]");
        if (!string.IsNullOrEmpty(n.Value) && n.Value != n.Name && ValueKinds.Contains(n.Kind))
            sb.Append(" value=\"").Append(Escape(n.Value)).Append('"');
        if (boxes && !n.Bounds.IsEmpty)
            sb.Append($" [box={n.Bounds.X},{n.Bounds.Y},{n.Bounds.Width},{n.Bounds.Height}]");
        return sb.ToString();
    }

    private static void Emit(StringBuilder sb, ElementNode n, RefRegistry refs, SnapshotOptions o, int indent, int depth, bool isRoot)
    {
        if (!o.All && !isRoot)
        {
            if (n.IsOffscreen) return;
            if (IsNoise(n)) return;
            if (IsPureContainer(n))
            {
                // 無名稱的純容器不輸出，子元素提升到同一層
                foreach (var c in n.Children) Emit(sb, c, refs, o, indent, depth, isRoot: false);
                return;
            }
        }

        var r = refs.Assign(n);
        sb.Append(' ', indent * 2).Append("- ").Append(Line(n, r, o.Boxes));

        if (o.MaxDepth > 0 && depth + 1 >= o.MaxDepth)
        {
            if (n.Children.Count > 0) sb.Append(" …");
            sb.AppendLine();
            return;
        }

        sb.AppendLine();
        foreach (var c in n.Children) Emit(sb, c, refs, o, indent + 1, depth + 1, isRoot: false);
    }

    internal static bool IsPureContainer(ElementNode n) =>
        ContainerKinds.Contains(n.Kind) && n.Name.Length == 0 && n.AutomationId.Length == 0;

    internal static bool IsNoise(ElementNode n) =>
        n.Kind is ControlKind.Separator or ControlKind.Thumb
        || (n.Kind is ControlKind.Image or ControlKind.Text && n.Name.Length == 0 && n.AutomationId.Length == 0 && n.Children.Count == 0);

    public static string Escape(string s)
    {
        var t = s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "\\n");
        return t.Length > MaxTextLength ? t[..MaxTextLength] + "…" : t;
    }
}
