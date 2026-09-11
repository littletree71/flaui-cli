using System.IO.Pipes;
using System.Text;
using FlauiCli.Core.Daemon;
using FlauiCli.Core.Protocol;

namespace FlauiCli;

/// <summary>透過 Named Pipe 與 daemon 通訊；需要時自動啟動 daemon。</summary>
internal static class DaemonClient
{
    private static readonly UTF8Encoding Utf8 = new(false);

    /// <summary>送出指令；連不上時回傳 null。</summary>
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
            ? CommandResult.Failure("daemon 意外中斷連線，請查看記錄檔：" + DaemonPaths.LogFile(session))
            : ProtocolJson.DeserializeResult(line);
    }

    public static CommandResult Send(string session, CommandCall call, bool autoStart)
    {
        var result = TrySend(session, call);
        if (result is not null) return result;

        if (!autoStart)
            return CommandResult.Failure(
                $"session「{session}」沒有在執行。請先執行 flaui-cli open <app> 或 flaui-cli attach <process>",
                ExitCodes.DaemonUnavailable);

        ProcessLauncher.StartDaemon(session);
        return TrySend(session, call, connectTimeoutMs: 15_000)
               ?? CommandResult.Failure("無法啟動 daemon，請查看記錄檔：" + DaemonPaths.LogFile(session), ExitCodes.DaemonUnavailable);
    }
}
