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
/// Depth limit for <see cref="IUiElement.CaptureTree"/>, counted the way a snapshot prints levels: nodes for which
/// <see cref="IsTransparent"/> returns true (unnamed containers that are not printed) do not count as a level.
/// Drivers may capture more than this (the formatter applies the limit again), but never less.
/// </summary>
public sealed record CaptureDepth(int MaxDepth, Func<ElementNode, bool> IsTransparent)
{
    /// <summary>
    /// Whether the children of a node on printed level <paramref name="level"/> (the root is level 0) need their own
    /// children. A printed node on the last level only needs to know that it has children (the snapshot shows "…").
    /// </summary>
    public bool ExpandsChildren(int level, bool transparent) => transparent || level + 1 < MaxDepth;

    /// <summary>Printed level of the children of a node: a transparent node is not printed, so they stay on its level.</summary>
    public static int ChildLevel(int level, bool transparent) => transparent ? level : level + 1;
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

    /// <summary>First descendant (in tree order) that matches, or null. Stops searching at the first match.</summary>
    IUiElement? FindFirst(ElementQuery query);

    IReadOnlyList<IUiElement> FindByXPath(string xpath);

    /// <summary>XPath from <paramref name="root"/> to this element, or null when it cannot be built.</summary>
    string? GetXPathFrom(IUiElement root);

    /// <summary>
    /// Fetches a property snapshot of the subtree (Control View); with <paramref name="depth"/>, only as deep as a
    /// snapshot limited to that depth prints, so large trees are not walked completely. With <paramref name="complete"/>
    /// false, a capture the UI Automation provider cut short is returned as is instead of being filled in (faster on
    /// large trees; used for the snapshot saved after every action).
    /// </summary>
    ElementNode CaptureTree(CaptureDepth? depth = null, bool complete = true);

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

    /// <summary>
    /// The item texts of a ComboBox without changing its selection (a collapsed WinForms combo box exposes no item
    /// elements), or null when they cannot be read.
    /// </summary>
    IReadOnlyList<string>? TryGetComboBoxItems();

    /// <summary>Full text of the TextPattern, or null when unsupported.</summary>
    string? TryGetDocumentText();

    bool TrySetWindowState(WindowStateKind state);
    bool TryResize(int width, int height);
    bool TryMove(int x, int y);
    bool TryClose();
}
