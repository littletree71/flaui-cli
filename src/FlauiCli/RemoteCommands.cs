using FlauiCli.Core.Protocol;

namespace FlauiCli;

/// <summary>Commands executed by the daemon.</summary>
internal static class RemoteCommands
{
    public static int Execute(CommandCall call, string session, bool json)
    {
        // record capture --out: block until the user finishes, then write the YAML directly
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

        if (!json) Console.WriteLine("Capturing... use the application directly and press Ctrl+Shift+Q (or Ctrl+C here) to finish");

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
