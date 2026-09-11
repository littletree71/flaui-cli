using System.IO.Pipes;
using System.Text;
using FlauiCli.Core.Abstractions;
using FlauiCli.Core.Engine;
using FlauiCli.Core.Protocol;

namespace FlauiCli.Core.Daemon;

/// <summary>
/// Long-running process that receives commands over a named pipe (one JSON request line and one JSON
/// response line per connection). Every UIA operation runs sequentially on a single dedicated thread.
/// Exits automatically after the configured idle time.
/// </summary>
public static class DaemonHost
{
    private static readonly UTF8Encoding Utf8 = new(false);

    public static int Run(string sessionName, Func<IUiDriver> driverFactory)
    {
        using var mutex = new Mutex(true, DaemonPaths.MutexName(sessionName), out var created);
        if (!created) return 0; // A daemon for this session is already running

        Directory.CreateDirectory(DaemonPaths.SessionsDir);
        Directory.CreateDirectory(DaemonPaths.LogsDir);
        using var log = new StreamWriter(DaemonPaths.LogFile(sessionName), append: false, Utf8) { AutoFlush = true };
        void Log(string message)
        {
            lock (log) log.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {message}");
        }

        using var worker = new SingleThreadWorker("flaui-cli-uia");
        AutomationSession session;
        try
        {
            session = worker.Invoke(() => new AutomationSession(sessionName, driverFactory));
        }
        catch (Exception ex)
        {
            Log($"cannot create session: {ex}");
            return ExitCodes.Error;
        }

        var dispatcher = new CommandDispatcher(session);
        var sessionFile = DaemonPaths.SessionFile(sessionName);
        File.WriteAllText(sessionFile, ProtocolJson.Serialize(
            new SessionInfo(sessionName, Environment.ProcessId, DaemonPaths.PipeName(sessionName), DateTime.Now)));
        Log($"daemon started: session={sessionName} pid={Environment.ProcessId} driver={session.Driver.Description}");

        using var shutdown = new CancellationTokenSource();
        var state = new ActivityState();
        var idleLimit = TimeSpan.FromMinutes(Math.Max(1, CliConfig.Load(null).DaemonIdleMinutes));
        var idleWatcher = Task.Run(async () =>
        {
            while (!shutdown.IsCancellationRequested)
            {
                try { await Task.Delay(TimeSpan.FromSeconds(15), shutdown.Token); }
                catch (OperationCanceledException) { break; }
                if (state.IsIdleLongerThan(idleLimit) && session.InputCapture?.IsRunning != true)
                {
                    Log("idle timeout, stopping daemon");
                    shutdown.Cancel();
                }
            }
        });

        try
        {
            AcceptLoopAsync(sessionName, worker, dispatcher, state, shutdown, Log).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Log($"daemon error: {ex}");
        }
        finally
        {
            try { worker.Invoke(session.Dispose); } catch (Exception ex) { Log($"failed to dispose session: {ex.Message}"); }
            TryDeleteSessionFile(sessionFile);
            Log("daemon stopped");
        }

        idleWatcher.Wait(TimeSpan.FromSeconds(1));
        return ExitCodes.Success;
    }

    private static async Task AcceptLoopAsync(string sessionName, SingleThreadWorker worker, CommandDispatcher dispatcher,
        ActivityState state, CancellationTokenSource shutdown, Action<string> log)
    {
        var pipeName = DaemonPaths.PipeName(sessionName);
        while (!shutdown.IsCancellationRequested)
        {
            var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            try
            {
                await server.WaitForConnectionAsync(shutdown.Token);
            }
            catch (OperationCanceledException)
            {
                await server.DisposeAsync();
                break;
            }

            _ = Task.Run(() => HandleConnectionAsync(server, worker, dispatcher, state, shutdown, log));
        }
    }

    private static async Task HandleConnectionAsync(NamedPipeServerStream server, SingleThreadWorker worker,
        CommandDispatcher dispatcher, ActivityState state, CancellationTokenSource shutdown, Action<string> log)
    {
        await using var _ = server;
        try
        {
            using var reader = new StreamReader(server, Utf8, false, 4096, leaveOpen: true);
            await using var writer = new StreamWriter(server, Utf8, 4096, leaveOpen: true) { AutoFlush = true };
            var line = await reader.ReadLineAsync();
            if (line is null) return;

            CommandResult result;
            state.Begin();
            try
            {
                var call = ProtocolJson.DeserializeCall(line);
                log($"> {call.Redacted()}"); // never persist typed text: it may be a password
                result = call.Command == "ping"
                    ? CommandResult.Success("pong")
                    : await worker.InvokeAsync(() => dispatcher.Execute(call));
                log($"< {(result.Ok ? "ok" : $"failed({result.ExitCode}): {result.Error}")}");
            }
            catch (Exception ex)
            {
                result = CommandResult.Failure(ex.Message);
                log($"< exception: {ex}");
            }
            finally
            {
                state.End();
            }

            await writer.WriteLineAsync(ProtocolJson.Serialize(result));
            if (result.Data?.GetValueOrDefault("shutdown") == "true") shutdown.Cancel();
        }
        catch (IOException)
        {
            // The client disconnected early
        }
    }

    private static void TryDeleteSessionFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            var info = ProtocolJson.DeserializeSession(File.ReadAllText(path));
            if (info is null || info.Pid == Environment.ProcessId) File.Delete(path);
        }
        catch
        {
            // ignore
        }
    }

    /// <summary>Tracks the number of in-flight requests and the time of the last activity.</summary>
    private sealed class ActivityState
    {
        private int _active;
        private long _lastTicks = DateTime.UtcNow.Ticks;

        public void Begin()
        {
            Interlocked.Increment(ref _active);
            Interlocked.Exchange(ref _lastTicks, DateTime.UtcNow.Ticks);
        }

        public void End()
        {
            Interlocked.Decrement(ref _active);
            Interlocked.Exchange(ref _lastTicks, DateTime.UtcNow.Ticks);
        }

        public bool IsIdleLongerThan(TimeSpan limit) =>
            Volatile.Read(ref _active) == 0 && DateTime.UtcNow - new DateTime(Interlocked.Read(ref _lastTicks), DateTimeKind.Utc) > limit;
    }
}
