using System.Drawing;
using System.Text;
using FlauiCli.Core.Docs;
using FlauiCli.Core.Imaging;
using FlauiCli.Core.Recording;
using FlauiCli.Core.Scripting;

namespace FlauiCli.Core.Engine;

// Screenshots, recording and documentation
public sealed partial class CommandDispatcher
{
    private string Screenshot(CommandContext ctx)
    {
        var window = _s.RequireWindow();
        var highlights = ctx.Call.GetList("highlight").Select(h => Resolve(ctx, h)).ToList();

        Rectangle region;
        if (ctx.Call.GetBool("screen"))
        {
            region = _s.Driver.Screen.VirtualScreen;
        }
        else if (ctx.Call.Get("target") is { } t)
        {
            TryForeground(window);
            region = Resolve(ctx, t).Bounds;
        }
        else
        {
            TryForeground(window);
            region = WindowBounds(window);
            foreach (var h in highlights) region = Rectangle.Union(region, h.Bounds);
        }

        using var bmp = Screenshotter.CaptureRegion(_s.Driver.Screen, region, out var captured);
        // Without outlines a single highlight still needs its badge
        var style = Style(ctx);
        var warnings = Screenshotter.Annotate(bmp, captured.Location, [.. highlights.Select(h => h.Bounds)],
            highlights.Count > 1 || !style.Outline ? 1 : null, style, ctx.Call.GetList("highlight"));

        var path = ctx.Call.Get("filename") is { } f
            ? ctx.Call.ResolvePath(f)
            : Path.Combine(ctx.OutputDir, $"screenshot-{Stamp()}.png");
        Screenshotter.SavePng(bmp, path);
        ctx.Data["path"] = path;
        return $"### Result\nScreenshot saved: {ctx.Relative(path)}" + WarningsSection(ctx, warnings);
    }

    /// <summary>
    /// Highlight style from the <c>--highlight-padding</c>, <c>--no-outline</c> and <c>--badge-position</c> options,
    /// else from <c>highlight</c> in the config file (a script's own settings are already applied to it).
    /// </summary>
    private static HighlightStyle Style(CommandContext ctx)
    {
        var config = ctx.Config.Highlight;
        return new HighlightStyle(
            ctx.Call.GetInt("highlight-padding") ?? config.Padding,
            !ctx.Call.GetBool("no-outline") && config.Outline,
            HighlightStyle.ParseBadge(ctx.Call.Get("badge-position") ?? config.Badge));
    }

    /// <summary>
    /// Warnings about the annotations, after the result (the screenshot is saved anyway); at most
    /// <see cref="MaxWarnings"/> lines, so a crowded screenshot does not flood the output. Also in the result data as
    /// <c>warnings</c>, one per line.
    /// </summary>
    private static string WarningsSection(CommandContext ctx, IReadOnlyList<string> warnings)
    {
        if (warnings.Count == 0) return "";
        var shown = warnings.Take(MaxWarnings).ToList();
        if (warnings.Count > MaxWarnings) shown.Add($"...and {warnings.Count - MaxWarnings} more");
        ctx.Data["warnings"] = string.Join("\n", shown);
        return "\n### Warnings\n" + string.Join("\n", shown.Select(w => "- " + w));
    }

    private const int MaxWarnings = 5;

    private string Record(CommandContext ctx)
    {
        var action = ctx.Call.Require("action").ToLowerInvariant();
        switch (action)
        {
            case "start":
                if (_s.Recorder is not null) throw new CliException("Already recording. Run record stop first");
                _s.Recorder = new ScriptRecorder(ctx.Call.Get("name"), _s.AppInfo);
                return "### Result\nRecording commands. Every action command from now on is recorded; run record stop --out <file.yaml> when done";

            case "capture":
            {
                if (_s.InputCapture is { IsRunning: true }) throw new CliException("Already capturing input");
                var window = _s.RequireWindow();
                _s.Recorder ??= new ScriptRecorder(ctx.Call.Get("name"), _s.AppInfo);
                var pids = new List<int> { window.ProcessId };
                if (_s.App is { HasExited: false } app) pids.Add(app.ProcessId);
                var capture = new InputCapture(_s.Recorder, window.WindowHandle, pids);
                capture.Start();
                _s.InputCapture = capture;
                TryForeground(window);
                ctx.Data["capturing"] = "true";
                return $"### Result\nCapturing input from window \"{window.Name}\". Use the application directly; press Ctrl+Shift+Q or run record stop to finish";
            }

            case "status":
            {
                var recording = _s.Recorder is not null;
                var capturing = _s.InputCapture?.IsRunning == true;
                ctx.Data["recording"] = recording ? "true" : "false";
                ctx.Data["capturing"] = capturing ? "true" : "false";
                ctx.Data["steps"] = (_s.Recorder?.Count ?? 0).ToString();
                var error = _s.InputCapture?.LastError is { } e ? $"\nError: {e}" : "";
                return recording
                    ? $"### Result\nRecording ({_s.Recorder!.Count} steps){(capturing ? ", capturing input" : "")}{error}"
                    : "### Result\nNot recording";
            }

            case "stop":
            {
                if (_s.Recorder is null) throw new CliException("Not recording");
                _s.InputCapture?.Dispose();
                var captureError = _s.InputCapture?.LastError;
                _s.InputCapture = null;

                var doc = _s.Recorder.ToDocument();
                _s.Recorder = null;
                var yaml = ScriptYaml.Save(doc);

                var sb = new StringBuilder($"### Result\nRecording stopped ({doc.Steps.Count} steps)\n");
                if (captureError is not null) sb.AppendLine($"Warning: an error occurred while capturing input: {captureError}");
                if (ctx.Call.Get("out") is { } outFile)
                {
                    var path = ctx.Call.ResolvePath(outFile);
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    File.WriteAllText(path, yaml);
                    ctx.Data["path"] = path;
                    sb.AppendLine($"Saved: {ctx.Relative(path)}");
                }
                sb.AppendLine("```yaml").Append(yaml.TrimEnd()).AppendLine().Append("```");
                return sb.ToString();
            }

            default:
                throw new CliException($"Unknown record action: {action} (use start, capture, status or stop)");
        }
    }

    private string Doc(CommandContext ctx)
    {
        var action = ctx.Call.Require("action").ToLowerInvariant();
        switch (action)
        {
            case "start":
            {
                if (_s.Doc is not null) throw new CliException("Already recording a document. Run doc stop first");
                var title = ctx.Call.Get("title") ?? ctx.Call.Get("text") ?? DocBuilder.DefaultTitle;
                _s.Doc = new DocBuilder(title, Path.Combine(ctx.OutputDir, $"doc-work-{Stamp()}"));
                return $"### Result\nRecording document \"{title}\". Every action is captured with an annotated screenshot; " +
                       "add manual steps with doc step \"text\" and finish with doc stop --out <folder>";
            }

            case "step":
            {
                var doc = _s.Doc ?? throw new CliException("No document is being recorded. Run doc start --title <title> first");
                var text = ctx.Call.Require("text");
                var window = _s.RequireWindow();
                var highlights = ctx.Call.GetList("highlight").Select(h => Resolve(ctx, h)).ToList();
                TryForeground(window);
                var region = WindowBounds(window);
                foreach (var h in highlights) region = Rectangle.Union(region, h.Bounds);
                using var bmp = Screenshotter.CaptureRegion(_s.Driver.Screen, region, out var captured);
                var warnings = Screenshotter.Annotate(bmp, captured.Location, [.. highlights.Select(h => h.Bounds)],
                    highlights.Count == 1 ? doc.NextNumber : highlights.Count > 1 ? 1 : null, Style(ctx), ctx.Call.GetList("highlight"));
                var step = doc.AddStep(text, bmp, null);
                return $"### Result\nAdded step {step.Number}: {text}" + WarningsSection(ctx, warnings);
            }

            case "status":
                return _s.Doc is { } d
                    ? $"### Result\nRecording document \"{d.Title}\" ({d.Steps.Count} steps)"
                    : "### Result\nNo document is being recorded";

            case "stop":
            {
                var doc = _s.Doc ?? throw new CliException("No document is being recorded");
                var outDir = ctx.Call.Get("out") is { } o ? ctx.Call.ResolvePath(o) : Path.Combine(ctx.OutputDir, $"doc-{Stamp()}");
                var formats = (ctx.Call.Get("format") ?? "md,html").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var files = doc.Export(outDir, formats);
                _s.Doc = null;
                ctx.Data["path"] = outDir;
                var sb = new StringBuilder($"### Result\nDocument \"{doc.Title}\" created ({doc.Steps.Count} steps)\n");
                foreach (var f in files) sb.AppendLine($"- {ctx.Relative(f)}");
                return sb.ToString();
            }

            default:
                throw new CliException($"Unknown doc action: {action} (use start, step, status or stop)");
        }
    }
}
