using System.Drawing;

namespace FlauiCli.Core.Abstractions;

/// <summary>Property snapshot of a UI tree node (fetched in one go by <see cref="IUiElement.CaptureTree"/>).</summary>
public sealed class ElementNode
{
    public required IUiElement Element { get; init; }
    public string Name { get; init; } = "";
    public string AutomationId { get; init; } = "";
    public ControlKind Kind { get; init; }
    public string ClassName { get; init; } = "";
    public bool IsEnabled { get; init; } = true;
    public bool IsOffscreen { get; init; }
    public Rectangle Bounds { get; init; }
    public string? RuntimeId { get; init; }
    public ToggleValue? Toggle { get; init; }
    public string? Value { get; set; }
    public ExpandValue? Expand { get; init; }
    public bool? IsSelected { get; init; }
    public bool HasFocus { get; init; }
    public List<ElementNode> Children { get; } = [];

    public IEnumerable<ElementNode> DescendantsAndSelf()
    {
        yield return this;
        foreach (var c in Children)
        foreach (var d in c.DescendantsAndSelf())
            yield return d;
    }
}

public static class GeometryExtensions
{
    public static Point Center(this Rectangle r) => new(r.X + r.Width / 2, r.Y + r.Height / 2);
}

public static class ControlKindExtensions
{
    /// <summary>Lower-case role name used in snapshots.</summary>
    public static string Role(this ControlKind kind) => kind.ToString().ToLowerInvariant();
}

public static class UiElementExtensions
{
    /// <summary>Clickable point of the element, falling back to the center of its bounds.</summary>
    public static Point ClickPoint(this IUiElement el)
    {
        if (el.TryGetClickablePoint() is { } p) return p;
        var r = el.Bounds;
        if (r.IsEmpty) throw new CliException("The element has no clickable point (it may be hidden or minimized)");
        return r.Center();
    }
}
