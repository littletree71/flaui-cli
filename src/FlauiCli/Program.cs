using System.Text;

// Make non-ASCII output render correctly in every terminal
Console.OutputEncoding = new UTF8Encoding(false);

// Diagnostics: when FLAUI_CLI_TRACE=<file path> is set, record the command line of every start
if (Environment.GetEnvironmentVariable("FLAUI_CLI_TRACE") is { Length: > 0 } tracePath)
{
    try
    {
        File.AppendAllText(tracePath,
            $"{DateTime.Now:HH:mm:ss.fff} pid={Environment.ProcessId} commandLine=[{Environment.CommandLine}] " +
            $"args=[{string.Join(" | ", args)}]{Environment.NewLine}");
    }
    catch (IOException)
    {
        // Tracing failures must not affect execution
    }
}

return FlauiCli.CliApp.Run(args);
