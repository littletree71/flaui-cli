using System.Drawing;

namespace FlauiCli.Core.Abstractions;

/// <summary>UI 樹節點的屬性快照（由 <see cref="IUiElement.CaptureTree"/> 一次取回）。</summary>
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
    /// <summary>snapshot 中使用的小寫角色名稱。</summary>
    public static string Role(this ControlKind kind) => kind.ToString().ToLowerInvariant();
}

public static class UiElementExtensions
{
    /// <summary>取得可點擊座標，失敗時退回元素中心點。</summary>
    public static Point ClickPoint(this IUiElement el)
    {
        if (el.TryGetClickablePoint() is { } p) return p;
        var r = el.Bounds;
        if (r.IsEmpty) throw new CliException("元素沒有可點擊的位置（可能不可見或已最小化）");
        return r.Center();
    }
}
