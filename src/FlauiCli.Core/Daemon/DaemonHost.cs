using System.IO.Pipes;
using System.Text;
using FlauiCli.Core.Abstractions;
using FlauiCli.Core.Engine;
using FlauiCli.Core.Protocol;

namespace FlauiCli.Core.Daemon;

/// <summary>
/// 常駐程序：透過 Named Pipe 接收指令（每個連線一行 JSON 請求、一行 JSON 回應），
/// 所有 UIA 操作都在單一專用執行緒上依序執行。閒置超過設定時間自動結束。
/// </summary>
public static class DaemonHost
{
    private static readonly UTF8Encoding Utf8 = new(false);

    public static int Run(string sessionName, Func<IUiDriver> driverFactory)
    {
        using var mutex = new Mutex(true, DaemonPaths.MutexName(sessionName), out var created);
        if (!created) return 0; // 同名 session 的 daemon 已在執行

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
            Log($"無法建立 session：{ex}");
            return ExitCodes.Error;
        }

        var dispatcher = new CommandDispatcher(session);
        var sessionFile = DaemonPaths.SessionFile(sessionName);
        File.WriteAllText(sessionFile, ProtocolJson.Serialize(
            new SessionInfo(sessionName, Environment.ProcessId, DaemonPaths.PipeName(sessionName), DateTime.Now)));
        Log($"daemon 啟動：session={sessionName} pid={Environment.ProcessId} driver={session.Driver.Description}");

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
                    Log("閒置逾時，結束 daemon");
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
            Log($"daemon 發生錯誤：{ex}");
        }
        finally
        {
            try { worker.Invoke(session.Dispose); } catch (Exception ex) { Log($"釋放 session 失敗：{ex.Message}"); }
            TryDeleteSessionFile(sessionFile);
            Log("daemon 結束");
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
                log($"> {call}");
                result = call.Command == "ping"
                    ? CommandResult.Success("pong")
                    : await worker.InvokeAsync(() => dispatcher.Execute(call));
                log($"< {(result.Ok ? "ok" : $"失敗({result.ExitCode})：{result.Error}")}");
            }
            catch (Exception ex)
            {
                result = CommandResult.Failure(ex.Message);
                log($"< 例外：{ex}");
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
            // 用戶端提早斷線
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
            // 忽略
        }
    }

    /// <summary>追蹤執行中的請求數與最後活動時間。</summary>
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
