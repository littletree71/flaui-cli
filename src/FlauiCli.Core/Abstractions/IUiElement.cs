using System.Drawing;

namespace FlauiCli.Core.Abstractions;

/// <summary>Search criteria (every non-null field must match).</summary>
public sealed record ElementQuery(
    string? AutomationId = null,
    string? Name = null,
    string? ClassName = null,
    ControlKind? Kind = null,
    int? ProcessId = null)
{
    public static readonly ElementQuery Any = new();

    public bool IsEmpty => AutomationId is null && Name is null && ClassName is null && Kind is null && ProcessId is null;
}

/// <summary>
/// Abstraction of a UI element. Properties are read live; when a read fails (element gone,
/// property not supported) a default value is returned instead of throwing.
/// Operations prefixed with Try return false when the element does not support the pattern.
/// </summary>
public interface IUiElement : IEquatable<IUiElement>
{
    string Name { get; }
    string AutomationId { get; }
    ControlKind Kind { get; }
    string ClassName { get; }
    string FrameworkId { get; }
    bool IsEnabled { get; }
    bool IsOffscreen { get; }
    bool HasFocus { get; }
    Rectangle Bounds { get; }

    /// <summary>UIA RuntimeId joined with dots; identifies the same element across lookups.</summary>
    string? RuntimeId { get; }

    int ProcessId { get; }
    nint WindowHandle { get; }
    bool IsPassword { get; }

    /// <summary>Whether the element still exists.</summary>
    bool IsAlive { get; }

    /// <summary>Parent element (Control View); null when there is none or it cannot be read.</summary>
    IUiElement? Parent { get; }

    ToggleValue? Toggle { get; }

    /// <summary>ValuePattern value; for a non-editable ComboBox, the name of the selected item.</summary>
    string? Value { get; }

    bool? IsReadOnly { get; }
    ExpandValue? Expand { get; }
    bool? IsSelected { get; }

    /// <summary>Names of the supported patterns (for example Invoke, Value, Toggle).</summary>
    IReadOnlyList<string> SupportedPatterns { get; }

    /// <summary>Reads another property by name (HelpText, ItemStatus, LocalizedControlType...); null when unsupported.</summary>
    string? GetProperty(string name);

    Point? TryGetClickablePoint();

    IReadOnlyList<IUiElement> FindAll(ElementQuery query, SearchScope scope = SearchScope.Descendants);

    IReadOnlyList<IUiElement> FindByXPath(string xpath);

    /// <summary>XPath from <paramref name="root"/> to this element, or null when it cannot be built.</summary>
    string? GetXPathFrom(IUiElement root);

    /// <summary>Fetches a property snapshot of the whole subtree in one go (Control View).</summary>
    ElementNode CaptureTree();

    void Focus();
    void SetForeground();

    bool TryInvoke();
    bool TrySetValue(string value);
    bool TryToggle();
    bool TrySelectItem();
    bool TryExpand();
    bool TryCollapse();
    bool TryScrollIntoView();
    bool TryScroll(ScrollDirection direction);

    /// <summary>Selects a ComboBox item by text or index; on success returns the selected text.</summary>
    bool TrySelectComboBoxItem(string? text, int? index, out string? selected);

    /// <summary>Full text of the TextPattern, or null when unsupported.</summary>
    string? TryGetDocumentText();

    bool TrySetWindowState(WindowStateKind state);
    bool TryResize(int width, int height);
    bool TryMove(int x, int y);
    bool TryClose();
}
