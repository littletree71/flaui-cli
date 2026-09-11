using FlauiCli.Core.Protocol;

namespace FlauiCli;

/// <summary>送往 daemon 執行的指令。</summary>
internal static class RemoteCommands
{
    public static int Execute(CommandCall call, string session, bool json)
    {
        // record capture --out：阻塞等待使用者操作完成後直接輸出 YAML
        if (call.Command == "record" && call.Get("action") == "capture" && call.Get("out") is not null)
            return RecordCapture(call, session, json);

        var autoStart = call.Command is "open" or "attach";
        var result = DaemonClient.Send(session, call, autoStart);
        Output.Print(result, json);
        return result.ExitCode;
    }

    private static int RecordCapture(CommandCall call, string session, bool json)
    {
        var start = new CommandCall("record") { Cwd = call.Cwd }.Set("action", "capture").Set("name", call.Get("name"));
        var started = DaemonClient.Send(session, start, autoStart: false);
        Output.Print(started, json);
        if (!started.Ok) return started.ExitCode;

        if (!json) Console.WriteLine("錄製中… 直接操作應用程式，按 Ctrl+Shift+Q（或在此按 Ctrl+C）結束");

        using var cancel = new CancellationTokenSource();
        ConsoleCancelEventHandler onCancel = (_, e) =>
        {
            e.Cancel = true;
            cancel.Cancel();
        };
        Console.CancelKeyPress += onCancel;
        try
        {
            var status = new CommandCall("record") { Cwd = call.Cwd }.Set("action", "status");
            while (!cancel.IsCancellationRequested)
            {
                Thread.Sleep(500);
                var r = DaemonClient.TrySend(session, status);
                if (r?.Data?.GetValueOrDefault("capturing") != "true") break;
            }
        }
        finally
        {
            Console.CancelKeyPress -= onCancel;
        }

        var stop = new CommandCall("record") { Cwd = call.Cwd }.Set("action", "stop").Set("out", call.Get("out"));
        var result = DaemonClient.Send(session, stop, autoStart: false);
        Output.Print(result, json);
        return result.ExitCode;
    }
}
