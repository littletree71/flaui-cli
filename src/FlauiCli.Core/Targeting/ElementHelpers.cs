using FlauiCli.Core.Abstractions;

namespace FlauiCli.Core.Targeting;

internal static class ElementHelpers
{
    /// <summary>Control types that users actually operate.</summary>
    private static readonly HashSet<ControlKind> InteractiveKinds =
    [
        ControlKind.Button, ControlKind.SplitButton, ControlKind.CheckBox, ControlKind.RadioButton, ControlKind.ComboBox,
        ControlKind.Edit, ControlKind.Hyperlink, ControlKind.ListItem, ControlKind.MenuItem, ControlKind.TabItem,
        ControlKind.TreeItem, ControlKind.DataItem, ControlKind.HeaderItem, ControlKind.Slider, ControlKind.Spinner,
    ];

    /// <summary>
    /// Hit testing (FromPoint) usually returns the deepest element, for example the text inside a button.
    /// Walks up at most three levels and switches to the first interactive control, so recorded selectors
    /// point at the element the user really operated.
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
