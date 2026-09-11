using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace FlauiCli;

/// <summary>
/// Starts the daemon fully detached: no inherited handles (so the caller's output pipes are never held
/// open), no visible console window.
/// <para>
/// The daemon only breaks away from the caller's job object when that job would kill it on close
/// (JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE), for example inside some terminals or agent sandboxes.
/// Breaking away unconditionally is a behaviour antivirus heuristics dislike, so it is avoided otherwise.
/// </para>
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "SYSLIB1054", Justification = "CreateProcessW needs a writable command line buffer")]
internal static class ProcessLauncher
{
    private const uint CREATE_NO_WINDOW = 0x08000000;
    private const uint CREATE_NEW_PROCESS_GROUP = 0x00000200;
    private const uint CREATE_BREAKAWAY_FROM_JOB = 0x01000000;
    private const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;

    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000;
    private const uint JOB_OBJECT_LIMIT_SILENT_BREAKAWAY_OK = 0x00001000;
    private const int JobObjectExtendedLimitInformation = 9;

    public static void StartDaemon(string session)
    {
        var processPath = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot determine the path of the current executable");
        var isDotnetHost = Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase);
        var cmd = new StringBuilder();
        cmd.Append('"').Append(processPath).Append('"');
        // Running as "dotnet flaui-cli.dll" needs the dll path (never the case for published builds)
        if (isDotnetHost) cmd.Append(" \"").Append(Path.Combine(AppContext.BaseDirectory, "flaui-cli.dll")).Append('"');
        cmd.Append(" daemon --session \"").Append(session.Replace("\"", "")).Append('"');

        var flags = CREATE_NO_WINDOW | CREATE_NEW_PROCESS_GROUP | CREATE_UNICODE_ENVIRONMENT;
        if (JobKillsChildrenOnClose() && TryCreate(cmd.ToString(), flags | CREATE_BREAKAWAY_FROM_JOB)) return;
        if (!TryCreate(cmd.ToString(), flags))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot start the daemon");
    }

    /// <summary>
    /// Whether the current process runs inside a job that would terminate the daemon when the job closes
    /// and does not already let children break away silently.
    /// </summary>
    private static bool JobKillsChildrenOnClose()
    {
        try
        {
            if (!IsProcessInJob(GetCurrentProcess(), 0, out var inJob) || !inJob) return false;
            var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
            if (!QueryInformationJobObject(0, JobObjectExtendedLimitInformation, ref info, Marshal.SizeOf(info), out _)) return false;
            var limits = info.BasicLimitInformation.LimitFlags;
            return (limits & JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE) != 0 && (limits & JOB_OBJECT_LIMIT_SILENT_BREAKAWAY_OK) == 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryCreate(string commandLine, uint flags)
    {
        var si = new STARTUPINFO { cb = Marshal.SizeOf<STARTUPINFO>() };
        var buffer = new StringBuilder(commandLine, commandLine.Length + 1);
        // Use the program directory as working directory so the daemon never locks the user's project folder
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

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessW(string? lpApplicationName, StringBuilder lpCommandLine, nint lpProcessAttributes,
        nint lpThreadAttributes, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandles, uint dwCreationFlags, nint lpEnvironment,
        string? lpCurrentDirectory, ref STARTUPINFO lpStartupInfo, out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint hObject);

    [DllImport("kernel32.dll")]
    private static extern nint GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsProcessInJob(nint processHandle, nint jobHandle, [MarshalAs(UnmanagedType.Bool)] out bool result);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryInformationJobObject(nint hJob, int jobObjectInfoClass,
        ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION lpJobObjectInfo, int cbJobObjectInfoLength, out int lpReturnLength);
}
