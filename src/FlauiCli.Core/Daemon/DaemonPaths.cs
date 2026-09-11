using System.Text.RegularExpressions;

namespace FlauiCli.Core.Daemon;

/// <summary>Daemon information stored in the session file.</summary>
public sealed record SessionInfo(string Session, int Pid, string Pipe, DateTime StartedAt);

/// <summary>Paths and names used by the daemon.</summary>
public static partial class DaemonPaths
{
    public static string StateDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "flaui-cli");

    public static string SessionsDir => Path.Combine(StateDir, "sessions");

    public static string LogsDir => Path.Combine(StateDir, "logs");

    public static string PipeName(string session) => $"flaui-cli-{Sanitize(Environment.UserName)}-{Sanitize(session)}";

    public static string MutexName(string session) => $@"Local\flaui-cli-daemon-{Sanitize(Environment.UserName)}-{Sanitize(session)}";

    public static string SessionFile(string session) => Path.Combine(SessionsDir, Sanitize(session) + ".json");

    public static string LogFile(string session) => Path.Combine(LogsDir, Sanitize(session) + ".log");

    [GeneratedRegex(@"[^A-Za-z0-9_.\-]")]
    private static partial Regex UnsafeChars();

    public static string Sanitize(string s) => UnsafeChars().Replace(s, "_");
}
