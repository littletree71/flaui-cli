using System.Runtime.InteropServices;

namespace FlauiCli.Drivers;

/// <summary>
/// Win32 view of the windows (HWNDs) under a window. Enumerating HWNDs is complete and takes microseconds, so it is
/// used to find child and owned windows that a truncated UIA search missed (see <see cref="UiaSearch"/>).
/// <para>
/// Only window enumeration and style queries: nothing here opens, reads or writes another process, which endpoint
/// protection treats as code injection.
/// </para>
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "SYSLIB1054", Justification = "Plain DllImport keeps the declarations simple")]
internal static class NativeWindows
{
    private const int WS_CAPTION = 0x00C00000;
    private const int GWL_STYLE = -16;
    private const int GW_OWNER = 4;

    /// <summary>All child windows of <paramref name="parent"/> at any depth, in Z-order within each level (pre-order).</summary>
    public static List<nint> Descendants(nint parent)
    {
        var result = new List<nint>();
        EnumChildWindows(parent, (h, _) => { result.Add(h); return true; }, 0);
        return result;
    }

    /// <summary>Visible top-level windows owned by <paramref name="owner"/> (dialogs; UIA lists them under their owner).</summary>
    public static List<nint> OwnedWindows(nint owner)
    {
        var result = new List<nint>();
        EnumWindows((h, _) =>
        {
            if (GetWindow(h, GW_OWNER) == owner && IsWindowVisible(h)) result.Add(h);
            return true;
        }, 0);
        return result;
    }

    /// <summary>True for windows with a title bar (MDI children, dialogs embedded as child windows).</summary>
    public static bool HasCaption(nint hwnd) => (GetWindowLongW(hwnd, GWL_STYLE) & WS_CAPTION) == WS_CAPTION;

    private delegate bool EnumWindowsProc(nint hwnd, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumChildWindows(nint parent, EnumWindowsProc callback, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, nint lParam);

    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint hwnd, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint hwnd);

    /// <summary>Whether the handle still refers to an existing window.</summary>
    public static bool Exists(nint hwnd) => IsWindow(hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint hwnd);

    [DllImport("user32.dll")]
    private static extern int GetWindowLongW(nint hwnd, int index);
}
