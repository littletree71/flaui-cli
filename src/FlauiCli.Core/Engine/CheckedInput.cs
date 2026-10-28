using System.Drawing;
using FlauiCli.Core.Abstractions;

namespace FlauiCli.Core.Engine;

/// <summary>
/// Checks that the desktop can receive input before every simulated mouse / keyboard action. On a locked workstation
/// SendInput fails with an unhelpful "Access is denied", or its input is silently lost. Commands that work through UI
/// Automation only (click --invoke, fill through ValuePattern) never get here and keep working.
/// </summary>
internal sealed class CheckedInput(IUiDriver driver) : IInputDevice
{
    private IInputDevice Inner => driver.Input;

    private IInputDevice Checked()
    {
        if (driver.DesktopUnavailableReason() is { } reason) throw new DesktopUnavailableException(reason);
        return Inner;
    }

    public void MoveTo(Point point) => Checked().MoveTo(point);

    public void Click(Point point, MouseButtonKind button) => Checked().Click(point, button);

    public void DoubleClick(Point point, MouseButtonKind button) => Checked().DoubleClick(point, button);

    public void Scroll(int amount, bool horizontal) => Checked().Scroll(amount, horizontal);

    public void Drag(Point from, Point to) => Checked().Drag(from, to);

    public void Type(string text) => Checked().Type(text);

    public void PressChord(IReadOnlyList<ushort> virtualKeys) => Checked().PressChord(virtualKeys);

    public void WaitUntilIdle() => Inner.WaitUntilIdle();
}
