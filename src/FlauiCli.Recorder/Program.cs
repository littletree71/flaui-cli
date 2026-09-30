using System.Text;
using FlauiCli.Core;
using FlauiCli.Drivers;
using FlauiCli.Recorder;

// flaui-cli-record: captures real mouse/keyboard input for "flaui-cli record capture".
// Started by the flaui-cli daemon; steps are written to standard output, one line each, and the
// capture ends on Ctrl+Shift+Q, on a line on standard input, or when standard input is closed.

Console.OutputEncoding = new UTF8Encoding(false);

nint hwnd = 0;
var pids = new List<int>();
for (var i = 0; i + 1 < args.Length; i += 2)
{
    if (args[i] == "--hwnd" && long.TryParse(args[i + 1], out var h)) hwnd = (nint)h;
    else if (args[i] == "--pid" && int.TryParse(args[i + 1], out var p)) pids.Add(p);
}

if (hwnd == 0)
{
    Console.Error.WriteLine("flaui-cli-record is the input recorder of flaui-cli and is not meant to be run directly.");
    Console.Error.WriteLine("Use: flaui-cli record capture --out <file.yaml>");
    Console.Error.WriteLine();
    Console.Error.WriteLine("Usage: flaui-cli-record --hwnd <window handle> [--pid <process id>]...");
    return 2;
}

var writer = new StepWriter(Console.Out);
try
{
    using var recorder = new InputRecorder(() => new FlaUIDriver(), writer, hwnd, pids);
    recorder.Start();
    writer.Ready();

    // Any line, or the end of standard input (flaui-cli exited), stops the capture
    new Thread(() =>
    {
        try { Console.In.ReadLine(); } catch (IOException) { /* treated as closed */ }
        recorder.Stop();
    }) { IsBackground = true, Name = "flaui-cli-record-stdin" }.Start();

    recorder.WaitForExit();
    return 0;
}
catch (CliException ex)
{
    writer.Error(ex.Message);
    return 1;
}
catch (Exception ex)
{
    writer.Error($"{ex.GetType().Name}: {ex.Message}");
    return 1;
}
