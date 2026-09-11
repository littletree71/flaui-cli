using System.Text;

// 讓中文輸出在各種終端機都正確
Console.OutputEncoding = new UTF8Encoding(false);

// 診斷用：設定環境變數 FLAUI_CLI_TRACE=<檔案路徑> 時，記錄每次啟動收到的命令列
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
        // 追蹤失敗不影響執行
    }
}

return FlauiCli.CliApp.Run(args);
