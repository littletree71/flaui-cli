using FlauiCli.Core.Protocol;
using FlauiCli.Core.Recording;

namespace FlauiCli.Recorder;

/// <summary>Reports captured steps to flaui-cli, one <see cref="CaptureProtocol"/> line per message. Thread-safe.</summary>
internal sealed class StepWriter(TextWriter output)
{
    private readonly object _lock = new();

    public void Ready() => Write(CaptureProtocol.Ready);

    public void Add(CommandCall call) => Write(CaptureProtocol.Add + ProtocolJson.Serialize(call));

    public void ReplaceLast(CommandCall call) => Write(CaptureProtocol.Replace + ProtocolJson.Serialize(call));

    public void Error(string message) => Write(CaptureProtocol.Error + message.ReplaceLineEndings(" "));

    private void Write(string line)
    {
        lock (_lock)
        {
            try
            {
                output.WriteLine(line);
                output.Flush();
            }
            catch (IOException)
            {
                // flaui-cli went away; the closed standard input stops the capture
            }
        }
    }
}
