using System.Drawing;

namespace FlauiCli.Core.Abstractions;

/// <summary>UI 自動化驅動程式（目前實作：FlaUI UIA3）。實作不需執行緒安全，由呼叫端保證單執行緒使用。</summary>
public interface IUiDriver : IDisposable
{
    /// <summary>驅動程式名稱與版本，例如「FlaUI.UIA3 5.0.0」。</summary>
    string Description { get; }

    /// <summary>桌面上的頂層元素；指定 processId 時只回傳該程序的。</summary>
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

/// <summary>被自動化的應用程式程序。</summary>
public interface IAppProcess : IDisposable
{
    int ProcessId { get; }

    bool HasExited { get; }

    IUiElement? GetMainWindow(TimeSpan timeout);

    bool WaitForExit(TimeSpan timeout);

    void Kill();
}

/// <summary>滑鼠鍵盤輸入。座標為螢幕實體像素；按鍵為 Win32 虛擬鍵碼。</summary>
public interface IInputDevice
{
    void MoveTo(Point point);

    void Click(Point point, MouseButtonKind button);

    void DoubleClick(Point point, MouseButtonKind button);

    /// <summary>滾輪捲動，正值向上 / 向右。</summary>
    void Scroll(int amount, bool horizontal);

    void Drag(Point from, Point to);

    void Type(string text);

    /// <summary>同時按下一組按鍵（組合鍵）。</summary>
    void PressChord(IReadOnlyList<ushort> virtualKeys);

    /// <summary>等待輸入被目標程式處理完。</summary>
    void WaitUntilIdle();
}

/// <summary>螢幕擷取。</summary>
public interface IScreenCapture
{
    Rectangle VirtualScreen { get; }

    Bitmap Capture(Rectangle region);
}
