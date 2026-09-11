using System.IO.Pipes;
using System.Text;
using FlauiCli.Core.Daemon;
using FlauiCli.Core.Protocol;

namespace FlauiCli;

/// <summary>Talks to the daemon over a named pipe and starts the daemon when needed.</summary>
internal static class DaemonClient
{
    private static readonly UTF8Encoding Utf8 = new(false);

    /// <summary>Sends a command; returns null when the daemon cannot be reached.</summary>
    public static CommandResult? TrySend(string session, CommandCall call, int connectTimeoutMs = 300)
    {
        using var client = new NamedPipeClientStream(".", DaemonPaths.PipeName(session), PipeDirection.InOut, PipeOptions.CurrentUserOnly);
        try
        {
            client.Connect(connectTimeoutMs);
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            return null;
        }

        using var writer = new StreamWriter(client, Utf8, 4096, leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(client, Utf8, false, 4096, leaveOpen: true);
        writer.WriteLine(ProtocolJson.Serialize(call));
        var line = reader.ReadLine();
        return line is null
            ? CommandResult.Failure("The daemon disconnected unexpectedly; see the log: " + DaemonPaths.LogFile(session))
            : ProtocolJson.DeserializeResult(line);
    }

    public static CommandResult Send(string session, CommandCall call, bool autoStart)
    {
        var result = TrySend(session, call);
        if (result is not null) return result;

        if (!autoStart)
            return CommandResult.Failure(
                $"Session '{session}' is not running. Run flaui-cli open <app> or flaui-cli attach <process> first",
                ExitCodes.DaemonUnavailable);

        ProcessLauncher.StartDaemon(session);
        return TrySend(session, call, connectTimeoutMs: 15_000)
               ?? CommandResult.Failure("Cannot start the daemon; see the log: " + DaemonPaths.LogFile(session), ExitCodes.DaemonUnavailable);
    }
}
