using System.Diagnostics;
using System.Text;
using FlauiCli.Core.Protocol;

namespace FlauiCli.Core.Recording;

/// <summary>
/// Captures real mouse and keyboard input by running the separate recorder executable
/// (flaui-cli-record.exe) and feeding the steps it reports into a <see cref="ScriptRecorder"/>.
/// <para>
/// The keyboard/mouse hooks live only in that executable; this process never installs a hook and does
/// not import the hook APIs. The recorder is started as an ordinary child process and stops when it is
/// told to, when the user presses the stop hotkey, or when this process goes away.
/// </para>
/// </summary>
public sealed class InputCapture : IDisposable
{
    private static readonly UTF8Encoding Utf8 = new(false);
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(8);

    private readonly ScriptRecorder _recorder;
    private readonly nint _rootHwnd;
    private readonly int[] _pids;
    private readonly ManualResetEventSlim _started = new();
    private Process? _process;
    private Thread? _reader;
    private volatile bool _ready;

    public InputCapture(ScriptRecorder recorder, nint rootHwnd, IEnumerable<int> pids)
    {
        _recorder = recorder;
        _rootHwnd = rootHwnd;
        _pids = [.. pids.Where(p => p != 0).Distinct()];
    }

    /// <summary>Full path of the recorder executable (next to the running program).</summary>
    public static string ExecutablePath => Path.Combine(AppContext.BaseDirectory, CaptureProtocol.ExecutableName);

    public bool IsRunning => _ready && _process is { HasExited: false };

    public string? LastError { get; private set; }

    public void Start()
    {
        if (_rootHwnd == 0) throw new CliException("The current window has no window handle (HWND), so input cannot be captured");
        var exe = ExecutablePath;
        if (!File.Exists(exe))
            throw new CliException(
                $"Input capture needs {CaptureProtocol.ExecutableName} next to flaui-cli.exe, but it was not found: {exe}. " +
                "It ships in the release zip; if it is missing, security software may have removed it");

        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            StandardOutputEncoding = Utf8,
            WorkingDirectory = AppContext.BaseDirectory,
        };
        psi.ArgumentList.Add("--hwnd");
        psi.ArgumentList.Add(_rootHwnd.ToString());
        foreach (var pid in _pids)
        {
            psi.ArgumentList.Add("--pid");
            psi.ArgumentList.Add(pid.ToString());
        }

        try
        {
            _process = Process.Start(psi) ?? throw new CliException($"Cannot start {CaptureProtocol.ExecutableName}");
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new CliException($"Cannot start {CaptureProtocol.ExecutableName}: {ex.Message}", ex);
        }

        _reader = new Thread(ReadLoop) { IsBackground = true, Name = "flaui-cli-capture-reader" };
        _reader.Start();

        if (!_started.Wait(StartTimeout) || !_ready)
        {
            var error = LastError ?? (_started.IsSet ? "the recorder exited unexpectedly" : "timed out");
            Stop();
            throw new CliException("Cannot start input capture: " + error);
        }
    }

    public void Stop()
    {
        var process = _process;
        if (process is null) return;
        try
        {
            if (!process.HasExited)
            {
                try
                {
                    process.StandardInput.WriteLine(CaptureProtocol.Stop);
                    process.StandardInput.Close();
                }
                catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
                {
                    // The recorder is already shutting down
                }

                if (!process.WaitForExit(StopTimeout))
                {
                    LastError ??= "the recorder did not stop in time and was terminated";
                    try { process.Kill(); } catch (InvalidOperationException) { /* exited meanwhile */ }
                }
            }

            // The last steps are flushed right before the recorder exits; wait until they are all read
            _reader?.Join(StopTimeout);
        }
        finally
        {
            _ready = false;
        }
    }

    public void Dispose()
    {
        Stop();
        _process?.Dispose();
        _process = null;
    }

    private void ReadLoop()
    {
        try
        {
            var output = _process!.StandardOutput;
            while (output.ReadLine() is { } line) HandleLine(line);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // The recorder went away
        }
        finally
        {
            _started.Set();
        }
    }

    /// <summary>Handles one protocol line sent by the recorder.</summary>
    internal void HandleLine(string line)
    {
        try
        {
            if (line == CaptureProtocol.Ready)
            {
                _ready = true;
                _started.Set();
            }
            else if (line.StartsWith(CaptureProtocol.Add, StringComparison.Ordinal))
            {
                _recorder.Add(ProtocolJson.DeserializeCall(line[CaptureProtocol.Add.Length..]));
            }
            else if (line.StartsWith(CaptureProtocol.Replace, StringComparison.Ordinal))
            {
                _recorder.ReplaceLast(ProtocolJson.DeserializeCall(line[CaptureProtocol.Replace.Length..]));
            }
            else if (line.StartsWith(CaptureProtocol.Error, StringComparison.Ordinal))
            {
                LastError = line[CaptureProtocol.Error.Length..];
            }
        }
        catch (Exception ex) when (ex is CliException or System.Text.Json.JsonException)
        {
            LastError = "Invalid message from the recorder: " + ex.Message;
        }
    }
}
