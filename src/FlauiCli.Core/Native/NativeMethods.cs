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
}
