using System.Runtime.InteropServices;

namespace FlauiCli.Core.Native;

/// <summary>
/// Win32 API declarations used by the engine.
/// <para>
/// Hook and keyboard-state APIs (SetWindowsHookEx, GetAsyncKeyState, ...) must never be declared here:
/// they live only in the separate recorder executable (FlauiCli.Recorder), so that flaui-cli.exe does
/// not import them at all. NativeImportTests enforces this.
/// </para>
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "SYSLIB1054", Justification = "Plain DllImport keeps the declarations simple; none of them needs marshalling code")]
internal static class NativeMethods
{
    public const int SM_XVIRTUALSCREEN = 76;
    public const int SM_YVIRTUALSCREEN = 77;
    public const int SM_CXVIRTUALSCREEN = 78;
    public const int SM_CYVIRTUALSCREEN = 79;

    private static readonly nint DpiAwarenessContextPerMonitorAwareV2 = -4;
    private static int _dpiInitialized;

    /// <summary>Makes the current process Per-Monitor V2 DPI aware (so coordinates match screenshots). Runs only once.</summary>
    public static void EnsureDpiAware()
    {
        if (Interlocked.Exchange(ref _dpiInitialized, 1) == 1) return;
        try { SetProcessDpiAwarenessContext(DpiAwarenessContextPerMonitorAwareV2); }
        catch { /* Already set by the manifest, or not supported by the OS */ }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetProcessDpiAwarenessContext(nint value);

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    public static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern short VkKeyScanW(char ch);

    /// <summary>A visible top-level window as Win32 sees it.</summary>
    public readonly record struct TopLevelWindow(nint Handle, int ProcessId, string Title, string ClassName);

    private const int DWMWA_CLOAKED = 14;

    /// <summary>Visible, non-cloaked top-level windows (in z-order), optionally limited to one process.</summary>
    public static List<TopLevelWindow> VisibleTopLevelWindows(int? processId = null)
    {
        var result = new List<TopLevelWindow>();
        EnumWindowsProc callback = (hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd)) return true;
            GetWindowThreadProcessId(hwnd, out var pid);
            if (processId is int wanted && pid != wanted) return true;
            if (DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return true;
            result.Add(new TopLevelWindow(hwnd, pid, GetText(hwnd), GetClass(hwnd)));
            return true;
        };
        EnumWindows(callback, 0);
        GC.KeepAlive(callback);
        return result;
    }

    private static string GetText(nint hwnd)
    {
        var length = GetWindowTextLengthW(hwnd);
        if (length <= 0) return "";
        var buffer = new char[length + 1];
        var copied = GetWindowTextW(hwnd, buffer, buffer.Length);
        return new string(buffer, 0, Math.Max(0, copied));
    }

    private static string GetClass(nint hwnd)
    {
        var buffer = new char[256];
        var copied = GetClassNameW(hwnd, buffer, buffer.Length);
        return new string(buffer, 0, Math.Max(0, copied));
    }

    private delegate bool EnumWindowsProc(nint hwnd, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint hwnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hwnd, out int processId);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLengthW(nint hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(nint hwnd, [Out] char[] text, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(nint hwnd, [Out] char[] className, int maxCount);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out int value, int size);

    private const uint TTM_POP = 0x041C;

    /// <summary>
    /// Hides the Win32 tooltips (tooltips_class32) currently shown by a process, so a screenshot does not
    /// show a tooltip left over from an earlier mouse move. Returns whether any tooltip was hidden.
    /// </summary>
    public static bool HideTooltips(int processId)
    {
        var any = false;
        foreach (var w in VisibleTopLevelWindows(processId))
        {
            if (!w.ClassName.Contains("tooltips_class32", StringComparison.OrdinalIgnoreCase)) continue;
            SendMessageTimeoutW(w.Handle, TTM_POP, 0, 0, SMTO_ABORTIFHUNG, 200, out _);
            // Tracking tooltips (WinForms ToolStrip) ignore TTM_POP, so hide the window as well
            ShowWindowAsync(w.Handle, SW_HIDE);
            any = true;
        }
        return any;
    }

    private const uint SMTO_ABORTIFHUNG = 0x0002;
    private const int SW_HIDE = 0;
    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindowAsync(nint hwnd, int command);

    /// <summary>DwmGetWindowAttribute for RECT values, read as four ints: left, top, right, bottom.</summary>
    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
    private static extern int DwmGetWindowRect(nint hwnd, int attribute, [Out] int[] rect, int size);

    /// <summary>
    /// The visible frame of a top-level window, without the invisible resize borders that Windows 10/11 add around it
    /// (UIA's BoundingRectangle includes them). Returns false for child windows or when DWM cannot tell.
    /// </summary>
    public static bool TryGetVisibleFrame(nint hwnd, out System.Drawing.Rectangle frame)
    {
        frame = System.Drawing.Rectangle.Empty;
        if (hwnd == 0) return false;
        var r = new int[4];
        if (DwmGetWindowRect(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, r, r.Length * sizeof(int)) != 0) return false;
        frame = System.Drawing.Rectangle.FromLTRB(r[0], r[1], r[2], r[3]);
        return frame.Width > 0 && frame.Height > 0;
    }

    [DllImport("user32.dll")]
    private static extern nint SendMessageTimeoutW(nint hwnd, uint msg, nint wParam, nint lParam, uint flags, uint timeoutMs, out nint result);

    /// <summary>Whether a window is minimized; false when the handle is unknown or the call fails.</summary>
    public static bool IsMinimized(nint hwnd)
    {
        if (hwnd == 0) return false;
        try { return IsIconic(hwnd); }
        catch { return false; }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint hwnd);

    private const uint WM_CLOSE = 0x0010;

    /// <summary>
    /// Posts WM_CLOSE to a window, the same message its title bar close button sends. Works for dialogs that have
    /// no WindowPattern and no Cancel button. Returns false when the message could not be posted.
    /// </summary>
    public static bool PostClose(nint hwnd) => hwnd != 0 && PostMessageW(hwnd, WM_CLOSE, 0, 0);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessageW(nint hwnd, uint msg, nint wParam, nint lParam);
}
