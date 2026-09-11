using System.Diagnostics;
using System.Text;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace FlauiCli.E2E.Tests;

/// <summary>Paths and helpers shared by the E2E tests. UI tests must not run in parallel (they share mouse, keyboard and foreground window).</summary>
internal static class TestEnvironment
{
    public static string RepoRoot { get; } = FindRepoRoot();

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FlauiCli.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root (FlauiCli.slnx) not found");
    }

    /// <summary>A file built with the same configuration (Debug/Release) as the tests.</summary>
    private static string BuildOutput(string project, string file)
    {
        // AppContext.BaseDirectory = ...\bin\<Configuration>\<tfm>\
        var configuration = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)).Parent!.Name;
        var path = Path.Combine(RepoRoot, project, "bin", configuration, "net10.0-windows", file);
        return File.Exists(path) ? path : throw new FileNotFoundException($"{file} not found; build the solution first", path);
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

/// <summary>Result of running flaui-cli.exe.</summary>
internal sealed record CliRun(int ExitCode, string StdOut, string StdErr)
{
    public string All => StdOut + StdErr;
}

/// <summary>Runs the real flaui-cli.exe in its own session (covers the daemon and the named pipe).</summary>
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
            throw new TimeoutException("flaui-cli timed out: " + string.Join(' ', args));
        }
        return new CliRun(p.ExitCode, stdout.Result, stderr.Result);
    }

    public void Dispose()
    {
        try { Run("close"); } catch { /* ignore */ }
    }
}
