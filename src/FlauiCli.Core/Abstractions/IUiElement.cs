using System.Drawing;

namespace FlauiCli.Core.Abstractions;

/// <summary>搜尋條件（所有非 null 欄位需同時符合）。</summary>
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
/// UI 元素抽象。屬性為即時讀取；讀取失敗（元素消失、不支援）時回傳預設值而不擲出例外。
/// 名稱以 Try 開頭的操作在元素不支援對應 pattern 時回傳 false。
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

    /// <summary>UIA RuntimeId（以 . 串接），用來辨識同一個元素。</summary>
    string? RuntimeId { get; }

    int ProcessId { get; }
    nint WindowHandle { get; }
    bool IsPassword { get; }

    /// <summary>元素是否仍存在。</summary>
    bool IsAlive { get; }

    /// <summary>父元素（Control View），沒有或讀取失敗時為 null。</summary>
    IUiElement? Parent { get; }

    ToggleValue? Toggle { get; }

    /// <summary>ValuePattern 的值；不可編輯的 ComboBox 則為目前選取項目的名稱。</summary>
    string? Value { get; }
    bool? IsReadOnly { get; }
    ExpandValue? Expand { get; }
    bool? IsSelected { get; }

    /// <summary>支援的 pattern 名稱（例如 Invoke、Value、Toggle）。</summary>
    IReadOnlyList<string> SupportedPatterns { get; }

    /// <summary>以名稱讀取其他屬性（HelpText、ItemStatus、LocalizedControlType…），不支援回傳 null。</summary>
    string? GetProperty(string name);

    Point? TryGetClickablePoint();

    IReadOnlyList<IUiElement> FindAll(ElementQuery query, SearchScope scope = SearchScope.Descendants);

    IReadOnlyList<IUiElement> FindByXPath(string xpath);

    /// <summary>從 <paramref name="root"/> 到此元素的 XPath，無法產生時回傳 null。</summary>
    string? GetXPathFrom(IUiElement root);

    /// <summary>一次取回整棵子樹的屬性快照（Control View）。</summary>
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

    /// <summary>ComboBox 選取項目（依文字或索引），成功時回傳選到的文字。</summary>
    bool TrySelectComboBoxItem(string? text, int? index, out string? selected);

    /// <summary>TextPattern 的全文，不支援回傳 null。</summary>
    string? TryGetDocumentText();

    bool TrySetWindowState(WindowStateKind state);
    bool TryResize(int width, int height);
    bool TryMove(int x, int y);
    bool TryClose();
}
