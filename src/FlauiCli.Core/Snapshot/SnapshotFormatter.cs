using System.Text;
using FlauiCli.Core.Abstractions;
using FlauiCli.Core.Targeting;

namespace FlauiCli.Core.Snapshot;

public sealed record SnapshotOptions(int MaxDepth = 0, bool Boxes = false, bool All = false);

/// <summary>
/// Formats a UI tree as YAML-like text modelled on Playwright's aria snapshots:
/// <code>- button "One" [ref=e21] id=num1Button</code>
/// </summary>
internal static class SnapshotFormatter
{
    private const int MaxTextLength = 120;

    private static readonly HashSet<ControlKind> ContainerKinds =
        [ControlKind.Pane, ControlKind.Group, ControlKind.Custom, ControlKind.Unknown];

    private static readonly HashSet<ControlKind> ValueKinds =
        [ControlKind.Edit, ControlKind.ComboBox, ControlKind.Document, ControlKind.Spinner, ControlKind.Slider, ControlKind.ProgressBar];

    /// <param name="collapse">RuntimeId of a node printed elsewhere (a window shown on its own): printed as one line without its children.</param>
    public static string Format(ElementNode root, RefRegistry refs, SnapshotOptions options, string? collapse = null)
    {
        var sb = new StringBuilder();
        Emit(sb, root, refs, options, collapse, indent: 0, depth: 0, isRoot: true);
        return sb.ToString().TrimEnd();
    }

    /// <summary>Single-line description of a node (also used by the find command).</summary>
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

    /// <summary>
    /// Depth limit for <see cref="IUiElement.CaptureTree"/> that matches what <see cref="Format"/> prints with these
    /// options (unprinted containers do not count as a level), or null when the depth is unlimited.
    /// </summary>
    public static CaptureDepth? CaptureDepthFor(SnapshotOptions o) =>
        o.MaxDepth > 0 ? new CaptureDepth(o.MaxDepth, o.All ? _ => false : IsPureContainer) : null;

    private static void Emit(StringBuilder sb, ElementNode n, RefRegistry refs, SnapshotOptions o, string? collapse, int indent, int depth, bool isRoot)
    {
        if (!o.All && !isRoot)
        {
            if (n.IsOffscreen) return;
            if (IsNoise(n)) return;
            if (IsPureContainer(n))
            {
                // Unnamed pure containers are not printed; their children move up one level
                foreach (var c in n.Children) Emit(sb, c, refs, o, collapse, indent, depth, isRoot: false);
                return;
            }
        }

        var r = refs.Assign(n);
        sb.Append(' ', indent * 2).Append("- ").Append(Line(n, r, o.Boxes));

        if (!isRoot && collapse is not null && n.RuntimeId == collapse)
        {
            sb.AppendLine(" (current window, shown above)");
            return;
        }

        if (o.MaxDepth > 0 && depth + 1 >= o.MaxDepth)
        {
            if (n.Children.Count > 0) sb.Append(" …");
            sb.AppendLine();
            return;
        }

        sb.AppendLine();
        foreach (var c in n.Children) Emit(sb, c, refs, o, collapse, indent + 1, depth + 1, isRoot: false);
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
