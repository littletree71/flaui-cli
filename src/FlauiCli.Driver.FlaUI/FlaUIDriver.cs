using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;
using FlauiCli.Core.Abstractions;

namespace FlauiCli.Drivers;

/// <summary>
/// Driver implemented with FlaUI UIA3.
/// Note: FlaUI.UIA3 relies on built-in COM interop, which Native AOT does not support
/// (CUIAutomation8Class fails to construct), so the application is published self-contained instead.
/// </summary>
public sealed class FlaUIDriver : IUiDriver
{
    private readonly UIA3Automation _automation = new();

    public string Description => $"FlaUI.UIA3 {typeof(UIA3Automation).Assembly.GetName().Version?.ToString(3)}";

    public IInputDevice Input { get; } = new FlaUIInput();

    public IScreenCapture Screen { get; } = new FlaUIScreenCapture();

    public IReadOnlyList<IUiElement> GetTopLevelWindows(int? processId = null)
    {
        var desktop = _automation.GetDesktop();
        var found = processId is int pid
            ? desktop.FindAllChildren(_automation.ConditionFactory.ByProcessId(pid))
            : desktop.FindAllChildren();
        return [.. found.Select(e => new FlaUIElement(e))];
    }

    public IUiElement? FromPoint(Point point) => Wrap(() => _automation.FromPoint(point));

    public IUiElement? FromHandle(nint hwnd) => Wrap(() => _automation.FromHandle(hwnd));

    public IUiElement? GetFocusedElement() => Wrap(_automation.FocusedElement);

    public IAppProcess Launch(string executable, string? arguments, string? workingDirectory)
    {
        var psi = new ProcessStartInfo(executable, arguments ?? "")
        {
            UseShellExecute = false,
            WorkingDirectory = workingDirectory ?? "",
        };
        return new FlaUIAppProcess(Application.Launch(psi), _automation);
    }

    public IAppProcess LaunchStoreApp(string appUserModelId, string? arguments) =>
        new FlaUIAppProcess(Application.LaunchStoreApp(appUserModelId, arguments ?? ""), _automation);

    public IAppProcess Attach(int processId) => new FlaUIAppProcess(Application.Attach(processId), _automation);

    public IAppProcess Attach(string processName) => new FlaUIAppProcess(Application.Attach(processName), _automation);

    private static FlaUIElement? Wrap(Func<AutomationElement?> get)
    {
        try { return get() is { } e ? new FlaUIElement(e) : null; }
        catch { return null; }
    }

    public void Dispose() => _automation.Dispose();
}

internal sealed class FlaUIAppProcess(Application app, AutomationBase automation) : IAppProcess
{
    public int ProcessId => app.ProcessId;

    public bool HasExited
    {
        get
        {
            try { return app.HasExited; }
            catch { return true; }
        }
    }

    public IUiElement? GetMainWindow(TimeSpan timeout)
    {
        try { return app.GetMainWindow(automation, timeout) is { } w ? new FlaUIElement(w) : null; }
        catch { return null; }
    }

    public bool WaitForExit(TimeSpan timeout)
    {
        try
        {
            using var p = Process.GetProcessById(ProcessId);
            return p.WaitForExit(timeout);
        }
        catch (ArgumentException)
        {
            return true; // The process no longer exists
        }
    }

    public void Kill()
    {
        try { app.Kill(); }
        catch { /* already exited */ }
    }

    public void Dispose() => app.Dispose();
}

internal sealed class FlaUIInput : IInputDevice
{
    public void MoveTo(Point point) => Mouse.MoveTo(point);

    public void Click(Point point, MouseButtonKind button) => Mouse.Click(point, Mapping.ToMouseButton(button));

    public void DoubleClick(Point point, MouseButtonKind button) => Mouse.DoubleClick(point, Mapping.ToMouseButton(button));

    public void Scroll(int amount, bool horizontal)
    {
        if (horizontal) Mouse.HorizontalScroll(amount);
        else Mouse.Scroll(amount);
    }

    public void Drag(Point from, Point to) => Mouse.Drag(from, to, MouseButton.Left);

    public void Type(string text) => Keyboard.Type(text);

    public void PressChord(IReadOnlyList<ushort> virtualKeys) =>
        Keyboard.TypeSimultaneously([.. virtualKeys.Select(v => (VirtualKeyShort)v)]);

    public void WaitUntilIdle() => Wait.UntilInputIsProcessed();
}

internal sealed partial class FlaUIScreenCapture : IScreenCapture
{
    public Rectangle VirtualScreen => new(GetSystemMetrics(76), GetSystemMetrics(77), GetSystemMetrics(78), GetSystemMetrics(79));

    public Bitmap Capture(Rectangle region)
    {
        using var img = FlaUI.Core.Capturing.Capture.Rectangle(region);
        return new Bitmap(img.Bitmap);
    }

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetrics(int index);
}
