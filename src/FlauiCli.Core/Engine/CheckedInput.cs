using System.Drawing;
using FlauiCli.Core.Abstractions;

namespace FlauiCli.Core.Engine;

/// <summary>
/// Checks that the desktop can receive input before every simulated mouse / keyboard action. On a locked workstation
/// SendInput fails with an unhelpful "Access is denied", or its input is silently lost. Commands that work through UI
/// Automation only (click --invoke, fill through ValuePattern) never get here and keep working.
/// <para>
/// With <c>timeouts.unlock</c> set, a locked workstation is waited for (up to that many milliseconds) instead of failing
/// at once, so an unattended run continues after the user unlocks.
/// </para>
/// </summary>
internal sealed class CheckedInput(IUiDriver driver, Func<int> unlockWaitMs) : IInputDevice
{
    private IInputDevice Inner => driver.Input;

    private IInputDevice Checked()
    {
        if (WaitForDesktop(driver, unlockWaitMs()) is { } reason) throw new DesktopUnavailableException(reason);
        return Inner;
    }

    /// <summary>
    /// Waits up to <paramref name="timeoutMs"/> until input can reach the desktop; returns null when it can, else why not.
    /// </summary>
    public static string? WaitForDesktop(IUiDriver driver, int timeoutMs, int intervalMs = 1000)
    {
        var reason = driver.DesktopUnavailableReason();
        if (reason is null || timeoutMs <= 0) return reason;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            Thread.Sleep((int)Math.Min(intervalMs, Math.Max(1, timeoutMs - sw.ElapsedMilliseconds)));
            reason = driver.DesktopUnavailableReason();
            if (reason is null) return null;
        }
        return reason;
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
