using System.Runtime.InteropServices;

namespace FlauiCli.Drivers;

/// <summary>
/// Whether simulated input reaches the user's desktop. While the session is locked it goes to the lock screen (and,
/// once that is dismissed, to the secure Winlogon desktop with the password prompt), as it does while a UAC prompt or the
/// Ctrl+Alt+Del screen is shown: input fails or is lost, and screenshots show the lock screen. Only queries the session
/// and the input desktop; nothing is changed.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "SYSLIB1054", Justification = "Plain DllImport keeps the declarations simple")]
internal static class InputDesktop
{
    private const uint DESKTOP_READOBJECTS = 0x0001;
    private const int UOI_NAME = 2;

    public static string? UnavailableReason()
    {
        // The lock screen of Windows 10 / 11 (LockApp) runs on the normal desktop until it is dismissed, so the desktop
        // alone misses it: simulated clicks then land on the lock screen and seem to succeed
        if (IsSessionLocked()) return "the workstation is locked";

        var desktop = OpenInputDesktop(0, false, DESKTOP_READOBJECTS);
        // A normal process may not open the secure desktop
        if (desktop == 0) return "the workstation is locked, or a UAC prompt or the Ctrl+Alt+Del screen is shown";
        try
        {
            var name = Name(desktop);
            if (name is null || name.Equals("Default", StringComparison.OrdinalIgnoreCase)) return null;
            return name.Equals("Winlogon", StringComparison.OrdinalIgnoreCase)
                ? "the workstation is locked, or a UAC prompt or the Ctrl+Alt+Del screen is shown"
                : $"input goes to another desktop (\"{name}\")";
        }
        finally
        {
            CloseDesktop(desktop);
        }
    }

    /// <summary>The lock state of this process's session (WTSSessionInfoEx); false when it cannot be read.</summary>
    private static bool IsSessionLocked()
    {
        if (!WTSQuerySessionInformation(0, WTS_CURRENT_SESSION, WTSSessionInfoEx, out var buffer, out var bytes)) return false;
        try
        {
            // WTSINFOEXW: Level (DWORD), then the WTSINFOEX_LEVEL1_W union member, aligned to 8 bytes because it holds
            // LARGE_INTEGER times: SessionId at 8, SessionState at 12, SessionFlags at 16
            return bytes >= 20 && Marshal.ReadInt32(buffer, 0) == 1 && Marshal.ReadInt32(buffer, 16) == WTS_SESSIONSTATE_LOCK;
        }
        finally
        {
            WTSFreeMemory(buffer);
        }
    }

    private const int WTS_CURRENT_SESSION = -1;
    private const int WTSSessionInfoEx = 25;
    private const int WTS_SESSIONSTATE_LOCK = 0;

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSQuerySessionInformation(nint server, int sessionId, int infoClass, out nint buffer, out int bytes);

    [DllImport("wtsapi32.dll")]
    private static extern void WTSFreeMemory(nint memory);

    private static string? Name(nint desktop)
    {
        var buffer = new char[256];
        return GetUserObjectInformation(desktop, UOI_NAME, buffer, buffer.Length * 2, out var needed) && needed > 2
            ? new string(buffer, 0, needed / 2 - 1)
            : null;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint OpenInputDesktop(uint flags, bool inherit, uint desiredAccess);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseDesktop(nint desktop);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "GetUserObjectInformationW")]
    private static extern bool GetUserObjectInformation(nint handle, int index, [Out] char[] info, int length, out int needed);
}
