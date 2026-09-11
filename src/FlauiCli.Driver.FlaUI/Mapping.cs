using FlaUI.Core.Definitions;
using FlauiCli.Core.Abstractions;

namespace FlauiCli.Drivers;

/// <summary>
/// FlaUI 列舉與 Core 自有列舉的對應。以名稱對應，
/// 完整性由契約測試（DriverContractTests.列舉對應完整）檢查：FlaUI 新增或更名成員時測試會失敗。
/// </summary>
internal static class Mapping
{
    public static ControlKind ToKind(ControlType type) =>
        Enum.TryParse<ControlKind>(type.ToString(), out var k) ? k : ControlKind.Unknown;

    public static ControlType ToControlType(ControlKind kind) =>
        Enum.TryParse<ControlType>(kind.ToString(), out var t)
            ? t
            : throw new NotSupportedException($"FlaUI 不支援的控制項類型：{kind}");

    public static ToggleValue ToToggle(ToggleState state) => state switch
    {
        ToggleState.On => ToggleValue.On,
        ToggleState.Indeterminate => ToggleValue.Indeterminate,
        _ => ToggleValue.Off,
    };

    public static ExpandValue ToExpand(ExpandCollapseState state) => state switch
    {
        ExpandCollapseState.Expanded => ExpandValue.Expanded,
        ExpandCollapseState.PartiallyExpanded => ExpandValue.PartiallyExpanded,
        ExpandCollapseState.LeafNode => ExpandValue.LeafNode,
        _ => ExpandValue.Collapsed,
    };

    public static WindowVisualState ToVisualState(WindowStateKind state) => state switch
    {
        WindowStateKind.Maximized => WindowVisualState.Maximized,
        WindowStateKind.Minimized => WindowVisualState.Minimized,
        _ => WindowVisualState.Normal,
    };

    public static FlaUI.Core.Input.MouseButton ToMouseButton(MouseButtonKind button) => button switch
    {
        MouseButtonKind.Right => FlaUI.Core.Input.MouseButton.Right,
        MouseButtonKind.Middle => FlaUI.Core.Input.MouseButton.Middle,
        _ => FlaUI.Core.Input.MouseButton.Left,
    };
}
