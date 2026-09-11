using System.Drawing;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Identifiers;

namespace FlauiCli.Drivers;

/// <summary>Safe UIA property reads: null when unsupported or the element is gone; cached values while a CacheRequest is active.</summary>
internal static class Props
{
    public static T? Ref<T>(AutomationElement el, PropertyId id) where T : class
    {
        try { return el.FrameworkAutomationElement.TryGetPropertyValue<T>(id, out var v) ? v : null; }
        catch { return null; }
    }

    public static T? Val<T>(AutomationElement el, PropertyId id) where T : struct
    {
        try { return el.FrameworkAutomationElement.TryGetPropertyValue<T>(id, out var v) ? v : null; }
        catch { return null; }
    }

    public static object? Raw(AutomationElement el, PropertyId id)
    {
        try { return el.FrameworkAutomationElement.TryGetPropertyValue(id, out var v) ? v : null; }
        catch { return null; }
    }

    public static string? RuntimeId(AutomationElement el) =>
        Ref<int[]>(el, el.Automation.PropertyLibrary.Element.RuntimeId) is { Length: > 0 } ids ? string.Join('.', ids) : null;

    public static Rectangle Bounds(AutomationElement el) =>
        Val<Rectangle>(el, el.Automation.PropertyLibrary.Element.BoundingRectangle) ?? Rectangle.Empty;
}
