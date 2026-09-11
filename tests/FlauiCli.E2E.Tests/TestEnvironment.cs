using System.Diagnostics;
using System.Text;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace FlauiCli.E2E.Tests;

/// <summary>E2E 測試共用的路徑與工具。UI 測試不可平行執行（共用滑鼠鍵盤與前景視窗）。</summary>
internal static class TestEnvironment
{
    public static string RepoRoot { get; } = FindRepoRoot();

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FlauiCli.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("找不到儲存庫根目錄（FlauiCli.slnx）");
    }

    /// <summary>與測試相同組態（Debug/Release）建置出的檔案。</summary>
    private static string BuildOutput(string project, string file)
    {
        // AppContext.BaseDirectory = …\bin\<Configuration>\<tfm>\
        var configuration = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)).Parent!.Name;
        var path = Path.Combine(RepoRoot, project, "bin", configuration, "net10.0-windows", file);
        return File.Exists(path) ? path : throw new FileNotFoundException($"找不到 {file}，請先建置方案", path);
    }

    public static string WpfSampleExe => BuildOutput(Path.Combine("tests", "TestApps", "WpfSample"), "WpfSample.exe");

    public static string CliExe => BuildOutput(Path.Combine("src", "FlauiCli"), "flaui-cli.exe");

    public static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "flaui-cli-e2e", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}

/// <summary>執行 flaui-cli.exe 的結果。</summary>
internal sealed record CliRun(int ExitCode, string StdOut, string StdErr)
{
    public string All => StdOut + StdErr;
}

/// <summary>以獨立 session 呼叫真正的 flaui-cli.exe（涵蓋 daemon 與 Named Pipe）。</summary>
internal sealed class CliSession(string workDir) : IDisposable
{
    public string Name { get; } = "e2e-" + Guid.NewGuid().ToString("N")[..8];

    public string WorkDir { get; } = workDir;

    public CliRun Run(params string[] args)
    {
        var psi = new ProcessStartInfo(TestEnvironment.CliExe)
        {
            WorkingDirectory = WorkDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        psi.ArgumentList.Add("--session");
        psi.ArgumentList.Add(Name);

        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(TimeSpan.FromSeconds(90)))
        {
            p.Kill();
            throw new TimeoutException("flaui-cli 執行逾時：" + string.Join(' ', args));
        }
        return new CliRun(p.ExitCode, stdout.Result, stderr.Result);
    }

    public void Dispose()
    {
        try { Run("close"); } catch { /* 忽略 */ }
    }
}
