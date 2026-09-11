using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace FlauiCli;

/// <summary>
/// 以「完全分離」的方式啟動 daemon：不繼承任何 handle（避免呼叫端的輸出管線被佔住而卡住）、
/// 不建立可見視窗，並盡量脫離呼叫端的 Job（避免終端機關閉時被一起結束）。
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "SYSLIB1054", Justification = "CreateProcessW 需要可寫入的命令列緩衝區")]
internal static class ProcessLauncher
{
    private const uint CREATE_NO_WINDOW = 0x08000000;
    private const uint CREATE_NEW_PROCESS_GROUP = 0x00000200;
    private const uint CREATE_BREAKAWAY_FROM_JOB = 0x01000000;
    private const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;

    public static void StartDaemon(string session)
    {
        var processPath = Environment.ProcessPath ?? throw new InvalidOperationException("無法取得目前執行檔路徑");
        var isDotnetHost = Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase);
        var cmd = new StringBuilder();
        cmd.Append('"').Append(processPath).Append('"');
        // 以 dotnet flaui-cli.dll 執行時需要帶上 dll 路徑（單檔發佈時不會走到這裡）
        if (isDotnetHost) cmd.Append(" \"").Append(Path.Combine(AppContext.BaseDirectory, "flaui-cli.dll")).Append('"');
        cmd.Append(" daemon --session \"").Append(session.Replace("\"", "")).Append('"');

        var flags = CREATE_NO_WINDOW | CREATE_NEW_PROCESS_GROUP | CREATE_UNICODE_ENVIRONMENT;
        if (!TryCreate(cmd.ToString(), flags | CREATE_BREAKAWAY_FROM_JOB) && !TryCreate(cmd.ToString(), flags))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "無法啟動 daemon");
    }

    private static bool TryCreate(string commandLine, uint flags)
    {
        var si = new STARTUPINFO { cb = Marshal.SizeOf<STARTUPINFO>() };
        var buffer = new StringBuilder(commandLine, commandLine.Length + 1);
        // 工作目錄設為程式所在目錄，避免 daemon 鎖住使用者的專案資料夾
        if (!CreateProcessW(null, buffer, 0, 0, false, flags, 0, AppContext.BaseDirectory, ref si, out var pi)) return false;
        CloseHandle(pi.hProcess);
        CloseHandle(pi.hThread);
        return true;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public nint lpReserved2;
        public nint hStdInput;
        public nint hStdOutput;
        public nint hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public nint hProcess;
        public nint hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessW(string? lpApplicationName, StringBuilder lpCommandLine, nint lpProcessAttributes,
        nint lpThreadAttributes, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandles, uint dwCreationFlags, nint lpEnvironment,
        string? lpCurrentDirectory, ref STARTUPINFO lpStartupInfo, out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint hObject);
}
