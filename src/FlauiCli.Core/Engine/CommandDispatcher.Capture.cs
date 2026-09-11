using System.Drawing;
using System.Text;
using FlauiCli.Core.Abstractions;
using FlauiCli.Core.Docs;
using FlauiCli.Core.Imaging;
using FlauiCli.Core.Recording;
using FlauiCli.Core.Scripting;

namespace FlauiCli.Core.Engine;

// 截圖、錄製、操作文件
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
            region = window.Bounds;
            foreach (var h in highlights) region = Rectangle.Union(region, h.Bounds);
        }

        using var bmp = Screenshotter.CaptureRegion(_s.Driver.Screen, region, out var captured);
        Screenshotter.Annotate(bmp, captured.Location, [.. highlights.Select(h => h.Bounds)], highlights.Count > 1 ? 1 : null);

        var path = ctx.Call.Get("filename") is { } f
            ? ctx.Call.ResolvePath(f)
            : Path.Combine(ctx.OutputDir, $"screenshot-{Stamp()}.png");
        Screenshotter.SavePng(bmp, path);
        ctx.Data["path"] = path;
        return $"### Result\n截圖已儲存：{ctx.Relative(path)}";
    }

    private string Record(CommandContext ctx)
    {
        var action = ctx.Call.Require("action").ToLowerInvariant();
        switch (action)
        {
            case "start":
                if (_s.Recorder is not null) throw new CliException("已經在錄製中。先執行 record stop");
                _s.Recorder = new ScriptRecorder(ctx.Call.Get("name"), _s.AppInfo);
                return "### Result\n開始記錄指令。之後執行的操作指令都會被記錄；完成後執行 record stop --out <檔名.yaml>";

            case "capture":
            {
                if (_s.InputCapture is { IsRunning: true }) throw new CliException("已經在捕捉真人操作中");
                var window = _s.RequireWindow();
                _s.Recorder ??= new ScriptRecorder(ctx.Call.Get("name"), _s.AppInfo);
                var pids = new List<int> { window.ProcessId };
                if (_s.App is { HasExited: false } app) pids.Add(app.ProcessId);
                var capture = new InputRecorder(_s.DriverFactory, _s.Recorder, window.WindowHandle, pids);
                capture.Start();
                _s.InputCapture = capture;
                TryForeground(window);
                ctx.Data["capturing"] = "true";
                return $"### Result\n開始捕捉真人操作（視窗「{window.Name}」）。直接操作應用程式，按 Ctrl+Shift+Q 或執行 record stop 結束";
            }

            case "status":
            {
                var recording = _s.Recorder is not null;
                var capturing = _s.InputCapture?.IsRunning == true;
                ctx.Data["recording"] = recording ? "true" : "false";
                ctx.Data["capturing"] = capturing ? "true" : "false";
                ctx.Data["steps"] = (_s.Recorder?.Count ?? 0).ToString();
                var error = _s.InputCapture?.LastError is { } e ? $"\n錯誤：{e}" : "";
                return recording
                    ? $"### Result\n錄製中（{_s.Recorder!.Count} 步）{(capturing ? "，正在捕捉真人操作" : "")}{error}"
                    : "### Result\n目前沒有在錄製";
            }

            case "stop":
            {
                if (_s.Recorder is null) throw new CliException("目前沒有在錄製");
                _s.InputCapture?.Dispose();
                var captureError = _s.InputCapture?.LastError;
                _s.InputCapture = null;

                var doc = _s.Recorder.ToDocument();
                _s.Recorder = null;
                var yaml = ScriptYaml.Save(doc);

                var sb = new StringBuilder($"### Result\n已停止錄製（{doc.Steps.Count} 個步驟）\n");
                if (captureError is not null) sb.AppendLine($"注意：錄製過程發生錯誤：{captureError}");
                if (ctx.Call.Get("out") is { } outFile)
                {
                    var path = ctx.Call.ResolvePath(outFile);
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    File.WriteAllText(path, yaml);
                    ctx.Data["path"] = path;
                    sb.AppendLine($"已儲存：{ctx.Relative(path)}");
                }
                sb.AppendLine("```yaml").Append(yaml.TrimEnd()).AppendLine().Append("```");
                return sb.ToString();
            }

            default:
                throw new CliException($"未知的 record 動作：{action}（可用：start, capture, status, stop）");
        }
    }

    private string Doc(CommandContext ctx)
    {
        var action = ctx.Call.Require("action").ToLowerInvariant();
        switch (action)
        {
            case "start":
            {
                if (_s.Doc is not null) throw new CliException("已經在記錄操作文件。先執行 doc stop");
                var title = ctx.Call.Get("title") ?? ctx.Call.Get("text") ?? "操作說明";
                _s.Doc = new DocBuilder(title, Path.Combine(ctx.OutputDir, $"doc-work-{Stamp()}"));
                return $"### Result\n開始記錄操作文件「{title}」。之後的操作都會自動截圖並標註目標元素；" +
                       "可用 doc step \"說明\" 手動加入步驟，完成後執行 doc stop --out <資料夾>";
            }

            case "step":
            {
                var doc = _s.Doc ?? throw new CliException("尚未開始記錄操作文件。先執行 doc start --title <標題>");
                var text = ctx.Call.Require("text");
                var window = _s.RequireWindow();
                var highlights = ctx.Call.GetList("highlight").Select(h => Resolve(ctx, h)).ToList();
                TryForeground(window);
                var region = window.Bounds;
                foreach (var h in highlights) region = Rectangle.Union(region, h.Bounds);
                using var bmp = Screenshotter.CaptureRegion(_s.Driver.Screen, region, out var captured);
                Screenshotter.Annotate(bmp, captured.Location, [.. highlights.Select(h => h.Bounds)],
                    highlights.Count == 1 ? doc.NextNumber : highlights.Count > 1 ? 1 : null);
                var step = doc.AddStep(text, bmp, null);
                return $"### Result\n已加入步驟 {step.Number}：{text}";
            }

            case "status":
                return _s.Doc is { } d
                    ? $"### Result\n操作文件「{d.Title}」記錄中（{d.Steps.Count} 步）"
                    : "### Result\n目前沒有在記錄操作文件";

            case "stop":
            {
                var doc = _s.Doc ?? throw new CliException("目前沒有在記錄操作文件");
                var outDir = ctx.Call.Get("out") is { } o ? ctx.Call.ResolvePath(o) : Path.Combine(ctx.OutputDir, $"doc-{Stamp()}");
                var formats = (ctx.Call.Get("format") ?? "md,html").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var files = doc.Export(outDir, formats);
                _s.Doc = null;
                ctx.Data["path"] = outDir;
                var sb = new StringBuilder($"### Result\n操作文件「{doc.Title}」已產生（{doc.Steps.Count} 個步驟）\n");
                foreach (var f in files) sb.AppendLine($"- {ctx.Relative(f)}");
                return sb.ToString();
            }

            default:
                throw new CliException($"未知的 doc 動作：{action}（可用：start, step, status, stop）");
        }
    }
}
