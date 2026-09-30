namespace FlauiCli.Core.Recording;

/// <summary>
/// Line protocol between the daemon and the recorder executable (flaui-cli-record.exe), spoken over
/// the recorder's redirected standard input and output. One message per line.
/// </summary>
internal static class CaptureProtocol
{
    /// <summary>File name of the recorder executable, expected next to flaui-cli.exe.</summary>
    public const string ExecutableName = "flaui-cli-record.exe";

    /// <summary>Recorder to daemon: the hooks are installed and input is being captured.</summary>
    public const string Ready = "ready";

    /// <summary>Recorder to daemon: append a step (followed by the step as JSON).</summary>
    public const string Add = "add ";

    /// <summary>Recorder to daemon: replace the last step, for example click becomes dblclick (followed by the step as JSON).</summary>
    public const string Replace = "replace ";

    /// <summary>Recorder to daemon: something went wrong (followed by the message).</summary>
    public const string Error = "error ";

    /// <summary>Daemon to recorder: stop capturing. Closing standard input has the same effect.</summary>
    public const string Stop = "stop";
}
