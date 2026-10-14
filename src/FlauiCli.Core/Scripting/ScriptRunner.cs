using System.Diagnostics;
using FlauiCli.Core.Abstractions;
using FlauiCli.Core.Commands;
using FlauiCli.Core.Docs;
using FlauiCli.Core.Engine;
using FlauiCli.Core.Imaging;
using FlauiCli.Core.Protocol;

namespace FlauiCli.Core.Scripting;

public sealed record RunOptions
{
    /// <summary>Folder for the generated document (null = no document).</summary>
    public string? DocDir { get; init; }

    public IReadOnlyList<string> DocFormats { get; init; } = ["md", "html"];

    public string Cwd { get; init; } = Environment.CurrentDirectory;

    /// <summary>Called after each step (used by the console reporter).</summary>
    public Action<StepResult>? OnStep { get; init; }
}

public sealed record StepResult(int Index, string Command, bool Passed, string? Error, TimeSpan Duration, bool IsSetup = false);

public sealed class ScriptResult
{
    public required string Name { get; init; }
    public string? File { get; init; }
    public bool Passed { get; set; }
    public TimeSpan Duration { get; set; }
    public List<StepResult> Steps { get; } = [];
    public string? Error { get; set; }
    public string? FailureScreenshot { get; set; }
    public IReadOnlyList<string> DocFiles { get; set; } = [];
}

/// <summary>Runs YAML scripts in-process (without a daemon).</summary>
public sealed class ScriptRunner(Func<IUiDriver> driverFactory)
{
    public ScriptResult Run(ScriptDocument doc, RunOptions options)
    {
        var total = Stopwatch.StartNew();
        var result = new ScriptResult { Name = doc.DisplayName, File = doc.SourcePath };
        var config = CliConfig.Load(options.Cwd);

        using var session = new AutomationSession("run", driverFactory);
        var dispatcher = new CommandDispatcher(session) { AutoSnapshot = false };
        if (options.DocDir is not null)
            session.Doc = new DocBuilder(doc.DisplayName, Path.Combine(config.ResolveOutputDir(options.Cwd), $"doc-work-{DateTime.Now:yyyyMMdd-HHmmss-fff}"));

        var setup = BuildSetup(doc.App);
        var launched = doc.App?.Launch is not null;

        bool RunStep(CommandCall call, int index, bool isSetup)
        {
            call.Cwd = options.Cwd;
            if (doc.Timeout is int t && !call.Has("timeout") && CommandCatalog.Find(call.Command)?.Options.Any(o => o.Name == "timeout") == true)
            {
                // The step timeout is usually far shorter than an application needs to show its first window:
                // launching waits at least the launch timeout
                call.Set("timeout", (isSetup ? Math.Max(t, config.Timeouts.Launch) : t).ToString());
            }

            var sw = Stopwatch.StartNew();
            var r = dispatcher.Execute(call);
            var step = new StepResult(index, CommandFormatter.ToCli(call.Clone().Remove("note").Remove("timeout")), r.Ok, r.Error, sw.Elapsed, isSetup);
            result.Steps.Add(step);
            options.OnStep?.Invoke(step);
            if (!r.Ok) result.Error = $"{(isSetup ? "Launching the application" : $"Step {index}")} failed: {r.Error}";
            return r.Ok;
        }

        try
        {
            var ok = setup is null || RunStep(setup, 0, isSetup: true);
            for (var i = 0; ok && i < doc.Steps.Count; i++) ok = RunStep(doc.Steps[i].Clone(), i + 1, isSetup: false);
            result.Passed = ok;

            if (!ok) result.FailureScreenshot = TryCaptureFailure(session, config, options.Cwd, doc.DisplayName);
        }
        finally
        {
            if (session.Doc is { Steps.Count: > 0 } d && options.DocDir is not null)
            {
                try { result.DocFiles = d.Export(options.DocDir, options.DocFormats); }
                catch (Exception ex) { result.Error ??= $"Failed to create the document: {ex.Message}"; }
            }

            if (doc.App?.Close ?? launched) CloseApp(session, dispatcher, options.Cwd);
        }

        result.Duration = total.Elapsed;
        return result;
    }

    /// <summary>
    /// Closes the application at the end of a script: its window through the close command (which also terminates the
    /// launched process when it does not exit), or, when no window was found (the launch timed out), the process the
    /// script launched. Processes the script only attached to are never terminated.
    /// </summary>
    private static void CloseApp(AutomationSession session, CommandDispatcher dispatcher, string cwd)
    {
        if (session.CurrentWindow is not null)
        {
            dispatcher.Execute(new CommandCall("close") { Cwd = cwd });
            return;
        }
        if (session.LaunchedPid is int pid && session.App is { HasExited: false } app && app.ProcessId == pid)
            app.Kill();
    }

    private static CommandCall? BuildSetup(AppSpec? app)
    {
        if (app is null) return null;
        if (app.Launch is not null)
        {
            var call = new CommandCall("open").Set("app", app.Launch).Set("window", app.Window);
            if (!string.IsNullOrEmpty(app.Args)) call.Set("args", app.Args);
            return call;
        }
        if (app.Attach is not null) return new CommandCall("attach").Set("process", app.Attach).Set("title", app.Window);
        return new CommandCall("attach").Set("title", app.Window);
    }

    private static string? TryCaptureFailure(AutomationSession session, CliConfig config, string cwd, string name)
    {
        try
        {
            if (session.CurrentWindow is not { IsAlive: true } window) return null;
            using var bmp = Screenshotter.CaptureRegion(session.Driver.Screen, window.Bounds, out _);
            var safe = string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            var path = Path.Combine(config.ResolveOutputDir(cwd), "failures", $"{safe}-{DateTime.Now:yyyyMMdd-HHmmss}.png");
            return Screenshotter.SavePng(bmp, path);
        }
        catch
        {
            return null;
        }
    }
}
