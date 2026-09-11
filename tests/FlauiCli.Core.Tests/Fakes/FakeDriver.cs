using System.Drawing;
using FlauiCli.Core.Abstractions;

namespace FlauiCli.Core.Tests.Fakes;

/// <summary>記憶體內的驅動程式。滑鼠點擊會以座標命中測試找到最深的元素並觸發其 OnClick。</summary>
public sealed class FakeDriver : IUiDriver
{
    public FakeDriver()
    {
        Input = new FakeInput(this);
    }

    public string Description => "FakeDriver";

    public List<FakeElement> TopLevel { get; } = [];

    public FakeElement? Focused { get; set; }

    /// <summary>Launch 時要回傳的主視窗（由測試設定）。</summary>
    public Func<string, FakeElement>? OnLaunch { get; set; }

    public List<string> Launched { get; } = [];

    public FakeInput Input { get; }

    IInputDevice IUiDriver.Input => Input;

    public IScreenCapture Screen { get; } = new FakeScreen();

    public IReadOnlyList<IUiElement> GetTopLevelWindows(int? processId = null) =>
        [.. TopLevel.Where(w => w.Alive && (processId is null || w.ProcessId == processId))];

    public IUiElement? FromPoint(Point point) => HitTest(point);

    public IUiElement? FromHandle(nint hwnd) => TopLevel.FirstOrDefault(w => w.WindowHandle == hwnd);

    public IUiElement? GetFocusedElement() => Focused;

    public IAppProcess Launch(string executable, string? arguments, string? workingDirectory)
    {
        Launched.Add($"{executable} {arguments}".Trim());
        var window = OnLaunch?.Invoke(executable) ?? throw new InvalidOperationException("測試未設定 OnLaunch");
        if (!TopLevel.Contains(window)) TopLevel.Add(window);
        return new FakeProcess(window.ProcessId, window);
    }

    public IAppProcess LaunchStoreApp(string appUserModelId, string? arguments) => Launch(appUserModelId, arguments, null);

    public IAppProcess Attach(int processId) =>
        new FakeProcess(processId, TopLevel.FirstOrDefault(w => w.ProcessId == processId));

    public IAppProcess Attach(string processName) =>
        new FakeProcess(TopLevel.FirstOrDefault()?.ProcessId ?? 0, TopLevel.FirstOrDefault());

    internal FakeElement? HitTest(Point p)
    {
        FakeElement? best = null;
        foreach (var root in TopLevel.Where(w => w.Alive))
        {
            if (!root.Bounds.Contains(p)) continue;
            best = root;
            foreach (var d in root.Descendants().Where(d => d.Alive && d.Bounds.Contains(p))) best = d;
        }
        return best;
    }

    public bool Disposed { get; private set; }

    public void Dispose() => Disposed = true;
}

public sealed class FakeProcess(int pid, FakeElement? mainWindow) : IAppProcess
{
    public int ProcessId => pid;

    public bool HasExited { get; set; }

    public IUiElement? GetMainWindow(TimeSpan timeout) => mainWindow;

    public bool WaitForExit(TimeSpan timeout) => true;

    public void Kill() => HasExited = true;

    public void Dispose() { }
}

public sealed class FakeInput(FakeDriver driver) : IInputDevice
{
    public List<string> Log { get; } = [];

    public void MoveTo(Point point) => Log.Add($"move {point.X},{point.Y}");

    public void Click(Point point, MouseButtonKind button)
    {
        Log.Add($"click {point.X},{point.Y} {button}");
        var el = driver.HitTest(point);
        el?.Actions.Add("click:" + button);
        if (button == MouseButtonKind.Left) el?.OnClick?.Invoke();
    }

    public void DoubleClick(Point point, MouseButtonKind button)
    {
        Log.Add($"dblclick {point.X},{point.Y} {button}");
        driver.HitTest(point)?.Actions.Add("dblclick");
    }

    public void Scroll(int amount, bool horizontal) => Log.Add($"scroll {amount} {(horizontal ? "h" : "v")}");

    public void Drag(Point from, Point to) => Log.Add($"drag {from.X},{from.Y} -> {to.X},{to.Y}");

    public void Type(string text)
    {
        Log.Add("type " + text);
        if (driver.Focused is { Value: not null } f) f.Value += text;
    }

    public void PressChord(IReadOnlyList<ushort> virtualKeys)
    {
        Log.Add("press " + string.Join("+", virtualKeys.Select(v => $"0x{v:X2}")));
        // 模擬 Ctrl+A、Delete 清空焦點元素
        if (virtualKeys is [0x2E] && driver.Focused is { Value: not null } f) f.Value = "";
    }

    public void WaitUntilIdle() { }
}

public sealed class FakeScreen : IScreenCapture
{
    public Rectangle VirtualScreen { get; } = new(0, 0, 1920, 1080);

    public Bitmap Capture(Rectangle region) => new(Math.Max(1, region.Width), Math.Max(1, region.Height));
}
