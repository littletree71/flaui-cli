using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Definitions;
using FlauiCli.Core.Abstractions;

namespace FlauiCli.Drivers;

/// <summary>以 UIA CacheRequest 一次取回整棵子樹（Control View），大幅減少跨程序呼叫。</summary>
internal static class TreeCapture
{
    /// <summary>節點數上限，避免超大型 DataGrid 拖垮輸出。</summary>
    public const int MaxNodes = 5000;

    public static ElementNode Capture(AutomationElement root)
    {
        var tree = CaptureRaw(root);
        // 快取中沒有 SelectionPattern：補上不可編輯 ComboBox 的選取值（通常只有少數幾個）
        foreach (var n in tree.DescendantsAndSelf().Where(n => n.Kind == ControlKind.ComboBox && n.Value is null))
            n.Value = FlaUIElement.SelectedItemName(((FlaUIElement)n.Element).Inner);
        return tree;
    }

    private static ElementNode CaptureRaw(AutomationElement root)
    {
        var lib = root.Automation.PropertyLibrary;
        var cr = new CacheRequest
        {
            TreeScope = TreeScope.Subtree,
            TreeFilter = lib.Element.IsControlElement.GetCondition(true),
        };
        cr.Add(lib.Element.Name);
        cr.Add(lib.Element.AutomationId);
        cr.Add(lib.Element.ControlType);
        cr.Add(lib.Element.ClassName);
        cr.Add(lib.Element.IsEnabled);
        cr.Add(lib.Element.IsOffscreen);
        cr.Add(lib.Element.BoundingRectangle);
        cr.Add(lib.Element.RuntimeId);
        cr.Add(lib.Element.HasKeyboardFocus);
        cr.Add(lib.Toggle.ToggleState);
        cr.Add(lib.Value.Value);
        cr.Add(lib.ExpandCollapse.ExpandCollapseState);
        cr.Add(lib.SelectionItem.IsSelected);

        try
        {
            using (cr.Activate())
            {
                var cached = root.FindFirst(TreeScope.Element, TrueCondition.Default);
                if (cached is not null)
                {
                    var count = 0;
                    return Build(cached, cachedMode: true, ref count);
                }
            }
        }
        catch
        {
            // 部分程式的 provider 不支援快取，改用逐一讀取
        }

        var liveCount = 0;
        return Build(root, cachedMode: false, ref liveCount);
    }

    private static ElementNode Build(AutomationElement el, bool cachedMode, ref int count)
    {
        count++;
        var lib = el.Automation.PropertyLibrary;
        var node = new ElementNode
        {
            Element = new FlaUIElement(el),
            Name = Props.Ref<string>(el, lib.Element.Name) ?? "",
            AutomationId = Props.Ref<string>(el, lib.Element.AutomationId) ?? "",
            Kind = Props.Val<ControlType>(el, lib.Element.ControlType) is { } ct ? Mapping.ToKind(ct) : ControlKind.Unknown,
            ClassName = Props.Ref<string>(el, lib.Element.ClassName) ?? "",
            IsEnabled = Props.Val<bool>(el, lib.Element.IsEnabled) ?? true,
            IsOffscreen = Props.Val<bool>(el, lib.Element.IsOffscreen) ?? false,
            Bounds = Props.Bounds(el),
            RuntimeId = Props.RuntimeId(el),
            Toggle = Props.Val<ToggleState>(el, lib.Toggle.ToggleState) is { } ts ? Mapping.ToToggle(ts) : null,
            Value = Props.Ref<string>(el, lib.Value.Value),
            Expand = Props.Val<ExpandCollapseState>(el, lib.ExpandCollapse.ExpandCollapseState) is { } es ? Mapping.ToExpand(es) : null,
            IsSelected = Props.Val<bool>(el, lib.SelectionItem.IsSelected),
            HasFocus = Props.Val<bool>(el, lib.Element.HasKeyboardFocus) ?? false,
        };

        if (count >= MaxNodes) return node;

        AutomationElement[] children;
        try { children = cachedMode ? el.CachedChildren : el.FindAllChildren(); }
        catch { children = []; }

        foreach (var child in children)
        {
            if (count >= MaxNodes) break;
            node.Children.Add(Build(child, cachedMode, ref count));
        }

        return node;
    }
}
