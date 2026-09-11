using System.Drawing;

namespace FlauiCli.Core.Abstractions;

/// <summary>
/// UI automation driver (current implementation: FlaUI UIA3). Implementations do not need to be
/// thread-safe; callers guarantee single-threaded use.
/// </summary>
public interface IUiDriver : IDisposable
{
    /// <summary>Driver name and version, for example "FlaUI.UIA3 5.0.0".</summary>
    string Description { get; }

    /// <summary>Top-level elements on the desktop; limited to one process when processId is given.</summary>
    IReadOnlyList<IUiElement> GetTopLevelWindows(int? processId = null);

    IUiElement? FromPoint(Point point);

    IUiElement? FromHandle(nint hwnd);

    IUiElement? GetFocusedElement();

    IAppProcess Launch(string executable, string? arguments, string? workingDirectory);

    IAppProcess LaunchStoreApp(string appUserModelId, string? arguments);

    IAppProcess Attach(int processId);

    IAppProcess Attach(string processName);

    IInputDevice Input { get; }

    IScreenCapture Screen { get; }
}

/// <summary>An automated application process.</summary>
public interface IAppProcess : IDisposable
{
    int ProcessId { get; }

    bool HasExited { get; }

    IUiElement? GetMainWindow(TimeSpan timeout);

    bool WaitForExit(TimeSpan timeout);

    void Kill();
}

/// <summary>Mouse and keyboard input. Coordinates are physical screen pixels; keys are Win32 virtual-key codes.</summary>
public interface IInputDevice
{
    void MoveTo(Point point);

    void Click(Point point, MouseButtonKind button);

    void DoubleClick(Point point, MouseButtonKind button);

    /// <summary>Wheel scroll; positive values scroll up / right.</summary>
    void Scroll(int amount, bool horizontal);

    void Drag(Point from, Point to);

    void Type(string text);

    /// <summary>Presses a set of keys together (a chord).</summary>
    void PressChord(IReadOnlyList<ushort> virtualKeys);

    /// <summary>Waits until the target application has processed the input.</summary>
    void WaitUntilIdle();
}

/// <summary>Screen capture.</summary>
public interface IScreenCapture
{
    Rectangle VirtualScreen { get; }

    Bitmap Capture(Rectangle region);
}
