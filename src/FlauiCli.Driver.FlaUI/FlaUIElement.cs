using System.Drawing;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Definitions;
using FlaUI.Core.Identifiers;
using FlauiCli.Core;
using FlauiCli.Core.Abstractions;

namespace FlauiCli.Drivers;

/// <summary>以 FlaUI <see cref="AutomationElement"/> 實作 <see cref="IUiElement"/>。</summary>
public sealed class FlaUIElement : IUiElement
{
    /// <summary>GetProperty 可讀取的屬性（明確對應，不使用反射，FlaUI 改版時由編譯器發現差異）。</summary>
    private static readonly Dictionary<string, Func<IPropertyLibrary, PropertyId>> ExtraProperties =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Name"] = l => l.Element.Name,
            ["AutomationId"] = l => l.Element.AutomationId,
            ["ClassName"] = l => l.Element.ClassName,
            ["FrameworkId"] = l => l.Element.FrameworkId,
            ["HelpText"] = l => l.Element.HelpText,
            ["ItemStatus"] = l => l.Element.ItemStatus,
            ["ItemType"] = l => l.Element.ItemType,
            ["LocalizedControlType"] = l => l.Element.LocalizedControlType,
            ["AcceleratorKey"] = l => l.Element.AcceleratorKey,
            ["AccessKey"] = l => l.Element.AccessKey,
            ["IsEnabled"] = l => l.Element.IsEnabled,
            ["IsOffscreen"] = l => l.Element.IsOffscreen,
            ["IsKeyboardFocusable"] = l => l.Element.IsKeyboardFocusable,
            ["HasKeyboardFocus"] = l => l.Element.HasKeyboardFocus,
            ["IsPassword"] = l => l.Element.IsPassword,
            ["IsRequiredForForm"] = l => l.Element.IsRequiredForForm,
            ["ProcessId"] = l => l.Element.ProcessId,
            ["NativeWindowHandle"] = l => l.Element.NativeWindowHandle,
            ["BoundingRectangle"] = l => l.Element.BoundingRectangle,
            ["Value"] = l => l.Value.Value,
            ["IsReadOnly"] = l => l.Value.IsReadOnly,
            ["ToggleState"] = l => l.Toggle.ToggleState,
            ["ExpandCollapseState"] = l => l.ExpandCollapse.ExpandCollapseState,
            ["IsSelected"] = l => l.SelectionItem.IsSelected,
            ["RangeValue"] = l => l.RangeValue.Value,
            ["RangeMinimum"] = l => l.RangeValue.Minimum,
            ["RangeMaximum"] = l => l.RangeValue.Maximum,
        };

    public static IReadOnlyCollection<string> ExtraPropertyNames => ExtraProperties.Keys;

    internal FlaUIElement(AutomationElement inner) => Inner = inner;

    internal AutomationElement Inner { get; }

    private IPropertyLibrary Lib => Inner.Automation.PropertyLibrary;

    public string Name => Props.Ref<string>(Inner, Lib.Element.Name) ?? "";
    public string AutomationId => Props.Ref<string>(Inner, Lib.Element.AutomationId) ?? "";

    public ControlKind Kind =>
        Props.Val<ControlType>(Inner, Lib.Element.ControlType) is { } t ? Mapping.ToKind(t) : ControlKind.Unknown;

    public string ClassName => Props.Ref<string>(Inner, Lib.Element.ClassName) ?? "";
    public string FrameworkId => Props.Ref<string>(Inner, Lib.Element.FrameworkId) ?? "";
    public bool IsEnabled => Props.Val<bool>(Inner, Lib.Element.IsEnabled) ?? true;
    public bool IsOffscreen => Props.Val<bool>(Inner, Lib.Element.IsOffscreen) ?? false;
    public bool HasFocus => Props.Val<bool>(Inner, Lib.Element.HasKeyboardFocus) ?? false;
    public Rectangle Bounds => Props.Bounds(Inner);
    public string? RuntimeId => Props.RuntimeId(Inner);
    public int ProcessId => Props.Val<int>(Inner, Lib.Element.ProcessId) ?? 0;
    public nint WindowHandle => Props.Val<nint>(Inner, Lib.Element.NativeWindowHandle) ?? 0;
    public bool IsPassword => Props.Val<bool>(Inner, Lib.Element.IsPassword) ?? false;

    public bool IsAlive
    {
        get
        {
            try { return Inner.IsAvailable; }
            catch { return false; }
        }
    }

    public IUiElement? Parent
    {
        get
        {
            try { return Inner.Parent is { } p ? new FlaUIElement(p) : null; }
            catch { return null; }
        }
    }

    public ToggleValue? Toggle =>
        Props.Val<ToggleState>(Inner, Lib.Toggle.ToggleState) is { } s ? Mapping.ToToggle(s) : null;

    public string? Value =>
        Props.Ref<string>(Inner, Lib.Value.Value) ?? (Kind == ControlKind.ComboBox ? SelectedItemName(Inner) : null);

    /// <summary>不可編輯的 ComboBox（例如 WPF）沒有 ValuePattern，改用 SelectionPattern 取得選取項目。</summary>
    internal static string? SelectedItemName(AutomationElement element)
    {
        try
        {
            var selection = element.Patterns.Selection.PatternOrDefault?.Selection.ValueOrDefault;
            return selection is { Length: > 0 } ? selection[0].Name : null;
        }
        catch
        {
            return null;
        }
    }
    public bool? IsReadOnly => Props.Val<bool>(Inner, Lib.Value.IsReadOnly);

    public ExpandValue? Expand =>
        Props.Val<ExpandCollapseState>(Inner, Lib.ExpandCollapse.ExpandCollapseState) is { } s ? Mapping.ToExpand(s) : null;

    public bool? IsSelected => Props.Val<bool>(Inner, Lib.SelectionItem.IsSelected);

    public IReadOnlyList<string> SupportedPatterns
    {
        get
        {
            try
            {
                return [.. Inner.GetSupportedPatterns()
                    .Select(p => p.Name.EndsWith("Pattern", StringComparison.Ordinal) ? p.Name[..^7] : p.Name)
                    .Order(StringComparer.Ordinal)];
            }
            catch { return []; }
        }
    }

    public string? GetProperty(string name)
    {
        if (!ExtraProperties.TryGetValue(name, out var id)) return null;
        var v = Props.Raw(Inner, id(Lib));
        return v switch
        {
            null => null,
            Rectangle r => $"{r.X},{r.Y},{r.Width},{r.Height}",
            _ => Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture),
        };
    }

    public Point? TryGetClickablePoint()
    {
        try { return Inner.TryGetClickablePoint(out var p) ? p : null; }
        catch { return null; }
    }

    public IReadOnlyList<IUiElement> FindAll(ElementQuery query, SearchScope scope = SearchScope.Descendants)
    {
        var condition = BuildCondition(query);
        var found = scope == SearchScope.Children
            ? Inner.FindAllChildren(condition)
            : Inner.FindAllDescendants(condition);
        return [.. found.Select(e => new FlaUIElement(e))];
    }

    private ConditionBase BuildCondition(ElementQuery q)
    {
        var cf = Inner.Automation.ConditionFactory;
        var list = new List<ConditionBase>();
        if (q.AutomationId is not null) list.Add(cf.ByAutomationId(q.AutomationId));
        if (q.Name is not null) list.Add(cf.ByName(q.Name));
        if (q.ClassName is not null) list.Add(cf.ByClassName(q.ClassName));
        if (q.Kind is { } k) list.Add(cf.ByControlType(Mapping.ToControlType(k)));
        if (q.ProcessId is { } pid) list.Add(cf.ByProcessId(pid));
        return list.Count switch
        {
            0 => TrueCondition.Default,
            1 => list[0],
            _ => new AndCondition([.. list]),
        };
    }

    public IReadOnlyList<IUiElement> FindByXPath(string xpath)
    {
        try { return [.. Inner.FindAllByXPath(xpath).Select(e => new FlaUIElement(e))]; }
        catch (Exception ex) { throw new CliException($"XPath 錯誤：{xpath}：{ex.Message}", ex); }
    }

    /// <summary>
    /// 自行產生 XPath，而不使用 FlaUI.Core.Debug.GetXPathToElement：
    /// FlaUI 5.0.0 的版本會漏掉根元素下的第一層（例如產生 /TabItem[1]/Button[1] 而非 /Tab/TabItem[1]/Button[1]），
    /// 導致無法用 FindAllByXPath 反查（由 DriverContractTests.XPath往返 抓到）。
    /// 這裡用與 FlaUI XPath 查詢相同的 Control View walker 列舉兄弟元素，確保可以往返。
    /// </summary>
    public string? GetXPathFrom(IUiElement root)
    {
        if (root is not FlaUIElement r) return null;
        try
        {
            var walker = Inner.Automation.TreeWalkerFactory.GetControlViewWalker();
            var segments = new List<string>();
            var current = Inner;
            while (true)
            {
                var parent = walker.GetParent(current);
                if (parent is null) return null; // root 不是祖先
                var type = current.ControlType;

                var index = 1;
                for (var sibling = walker.GetFirstChild(parent); sibling is not null; sibling = walker.GetNextSibling(sibling))
                {
                    if (sibling.Equals(current)) break;
                    if (sibling.ControlType == type) index++;
                }

                segments.Insert(0, $"{type}[{index}]");
                if (parent.Equals(r.Inner)) break;
                current = parent;
            }
            return "/" + string.Join('/', segments);
        }
        catch
        {
            return null;
        }
    }

    public ElementNode CaptureTree() => TreeCapture.Capture(Inner);

    public void Focus() => Inner.Focus();

    public void SetForeground() => Inner.SetForeground();

    public bool TryInvoke() => Try(Inner.Patterns.Invoke.PatternOrDefault, p => p.Invoke());

    public bool TrySetValue(string value)
    {
        var p = Inner.Patterns.Value.PatternOrDefault;
        if (p is null || p.IsReadOnly.ValueOrDefault) return false;
        p.SetValue(value);
        return true;
    }

    public bool TryToggle() => Try(Inner.Patterns.Toggle.PatternOrDefault, p => p.Toggle());

    public bool TrySelectItem() => Try(Inner.Patterns.SelectionItem.PatternOrDefault, p => p.Select());

    public bool TryExpand() => Try(Inner.Patterns.ExpandCollapse.PatternOrDefault, p => p.Expand());

    public bool TryCollapse() => Try(Inner.Patterns.ExpandCollapse.PatternOrDefault, p => p.Collapse());

    public bool TryScrollIntoView() => Try(Inner.Patterns.ScrollItem.PatternOrDefault, p => p.ScrollIntoView());

    public bool TryScroll(ScrollDirection direction)
    {
        var p = Inner.Patterns.Scroll.PatternOrDefault;
        if (p is null) return false;
        var vertical = direction is ScrollDirection.Up or ScrollDirection.Down;
        if (vertical && !p.VerticallyScrollable.ValueOrDefault) return false;
        if (!vertical && !p.HorizontallyScrollable.ValueOrDefault) return false;
        var amount = direction is ScrollDirection.Down or ScrollDirection.Right ? ScrollAmount.SmallIncrement : ScrollAmount.SmallDecrement;
        if (vertical) p.Scroll(ScrollAmount.NoAmount, amount);
        else p.Scroll(amount, ScrollAmount.NoAmount);
        return true;
    }

    public bool TrySelectComboBoxItem(string? text, int? index, out string? selected)
    {
        selected = null;
        if (Kind != ControlKind.ComboBox) return false;
        try
        {
            var cb = Inner.AsComboBox();
            var item = index is int i ? cb.Select(i) : cb.Select(text ?? "");
            if (item is null) return false;
            selected = item.Text;
            if (string.IsNullOrEmpty(selected)) selected = item.Name;
            try { if (cb.IsEditable == false) cb.Collapse(); } catch { /* 部分 ComboBox 不支援收合 */ }
            return true;
        }
        catch
        {
            return false;
        }
    }

    public string? TryGetDocumentText()
    {
        try { return Inner.Patterns.Text.PatternOrDefault?.DocumentRange.GetText(-1); }
        catch { return null; }
    }

    public bool TrySetWindowState(WindowStateKind state) =>
        Try(Inner.Patterns.Window.PatternOrDefault, p => p.SetWindowVisualState(Mapping.ToVisualState(state)));

    public bool TryResize(int width, int height)
    {
        var p = Inner.Patterns.Transform.PatternOrDefault;
        if (p is null || !p.CanResize.ValueOrDefault) return false;
        p.Resize(width, height);
        return true;
    }

    public bool TryMove(int x, int y)
    {
        var p = Inner.Patterns.Transform.PatternOrDefault;
        if (p is null || !p.CanMove.ValueOrDefault) return false;
        p.Move(x, y);
        return true;
    }

    public bool TryClose()
    {
        if (Kind == ControlKind.Window)
        {
            Inner.AsWindow().Close();
            return true;
        }
        return Try(Inner.Patterns.Window.PatternOrDefault, p => p.Close());
    }

    private static bool Try<T>(T? pattern, Action<T> action) where T : class
    {
        if (pattern is null) return false;
        action(pattern);
        return true;
    }

    public bool Equals(IUiElement? other)
    {
        if (other is not FlaUIElement f) return false;
        if (ReferenceEquals(Inner, f.Inner)) return true;
        try { return Inner.Equals(f.Inner); }
        catch { return false; }
    }

    public override bool Equals(object? obj) => obj is IUiElement e && Equals(e);

    public override int GetHashCode() => RuntimeId?.GetHashCode(StringComparison.Ordinal) ?? 0;

    public override string ToString() => $"{Kind} \"{Name}\" id={AutomationId}";
}
