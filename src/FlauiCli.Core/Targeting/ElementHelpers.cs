using FlauiCli.Core.Abstractions;

namespace FlauiCli.Core.Targeting;

internal static class ElementHelpers
{
    /// <summary>使用者實際會操作的控制項類型。</summary>
    private static readonly HashSet<ControlKind> InteractiveKinds =
    [
        ControlKind.Button, ControlKind.SplitButton, ControlKind.CheckBox, ControlKind.RadioButton, ControlKind.ComboBox,
        ControlKind.Edit, ControlKind.Hyperlink, ControlKind.ListItem, ControlKind.MenuItem, ControlKind.TabItem,
        ControlKind.TreeItem, ControlKind.DataItem, ControlKind.HeaderItem, ControlKind.Slider, ControlKind.Spinner,
    ];

    /// <summary>
    /// 座標命中測試（FromPoint）通常回傳最深的元素，例如按鈕裡的文字。
    /// 往上最多找 3 層，若遇到可互動的控制項就改用它，讓錄製出的 selector 指向真正被操作的元素。
    /// </summary>
    public static IUiElement PromoteToInteractive(IUiElement element)
    {
        if (InteractiveKinds.Contains(element.Kind)) return element;
        IUiElement? current = element;
        for (var i = 0; i < 3; i++)
        {
            current = current.Parent;
            if (current is null || current.Kind == ControlKind.Window) break;
            if (InteractiveKinds.Contains(current.Kind)) return current;
        }
        return element;
    }
}
