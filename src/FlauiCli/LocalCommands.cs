using System.Diagnostics;
using System.Text;
using System.Text.Json;
using FlauiCli.Core;
using FlauiCli.Core.Daemon;
using FlauiCli.Core.Protocol;
using FlauiCli.Core.Scripting;
using FlauiCli.Drivers;

namespace FlauiCli;

/// <summary>在 CLI 程序內直接處理的指令。</summary>
internal static class LocalCommands
{
    public static int Execute(CommandCall call, string session, bool json) => call.Command switch
    {
        "list" => List(json),
        "close-all" => CloseAll(json),
        "kill-all" => KillAll(json),
        "run" => Run(call, json),
        "install-skill" => InstallSkill(call, json),
        "daemon" => DaemonHost.Run(session, () => new FlaUIDriver()),
        _ => throw new CliException($"未知的本機指令：{call.Command}"),
    };

    private static IEnumerable<SessionInfo> Sessions()
    {
        if (!Directory.Exists(DaemonPaths.SessionsDir)) yield break;
        foreach (var file in Directory.GetFiles(DaemonPaths.SessionsDir, "*.json"))
        {
            SessionInfo? info;
            try { info = ProtocolJson.DeserializeSession(File.ReadAllText(file)); }
            catch (IOException) { continue; }
            if (info is not null) yield return info;
        }
    }

    private static bool IsAlive(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return !p.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static int List(bool json)
    {
        var sb = new StringBuilder("### Sessions\n");
        var count = 0;
        foreach (var info in Sessions())
        {
            var status = IsAlive(info.Pid) ? DaemonClient.TrySend(info.Session, new CommandCall("status"), 1000) : null;
            if (status is null)
            {
                // 殘留的 session 檔
                try { File.Delete(DaemonPaths.SessionFile(info.Session)); } catch (IOException) { }
                continue;
            }
            count++;
            var window = status.Data?.GetValueOrDefault("window") ?? "（無視窗）";
            sb.AppendLine($"- {info.Session}（daemon PID {info.Pid}，啟動於 {info.StartedAt:HH:mm:ss}）— {window}");
        }
        if (count == 0) sb.AppendLine("（沒有執行中的 session）");
        Output.Print(CommandResult.Success(sb.ToString().TrimEnd()), json);
        return ExitCodes.Success;
    }

    private static int CloseAll(bool json)
    {
        var sb = new StringBuilder("### Result\n");
        foreach (var info in Sessions())
        {
            var r = DaemonClient.TrySend(info.Session, new CommandCall("close") { Cwd = Environment.CurrentDirectory }, 1000);
            sb.AppendLine(r is { Ok: true } ? $"- 已關閉 {info.Session}" : $"- {info.Session} 無回應");
        }
        Output.Print(CommandResult.Success(sb.ToString().TrimEnd()), json);
        return ExitCodes.Success;
    }

    private static int KillAll(bool json)
    {
        var sb = new StringBuilder("### Result\n");
        foreach (var info in Sessions())
        {
            try
            {
                using var p = Process.GetProcessById(info.Pid);
                p.Kill();
                sb.AppendLine($"- 已強制結束 {info.Session}（PID {info.Pid}）");
            }
            catch (ArgumentException)
            {
                sb.AppendLine($"- {info.Session} 已不在執行");
            }
            try { File.Delete(DaemonPaths.SessionFile(info.Session)); } catch (IOException) { }
        }
        Output.Print(CommandResult.Success(sb.ToString().TrimEnd()), json);
        return ExitCodes.Success;
    }

    private static int Run(CommandCall call, bool json)
    {
        var files = call.GetList("files");
        if (files.Count == 0) throw new CliException("請指定要執行的腳本檔");
        var reporter = (call.Get("reporter") ?? "console").ToLowerInvariant();
        if (reporter is not ("console" or "junit")) throw new CliException($"未知的 reporter：{reporter}（可用：console, junit）");
        var docRoot = call.Get("doc") is { } d ? call.ResolvePath(d) : null;
        var docFormats = (call.Get("doc-format") ?? "md,html").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var bail = call.GetBool("bail");

        var runner = new ScriptRunner(() => new FlaUIDriver());
        var results = new List<ScriptResult>();
        foreach (var file in files)
        {
            var path = call.ResolvePath(file);
            ScriptDocument doc;
            try
            {
                doc = ScriptYaml.Load(path);
            }
            catch (CliException ex)
            {
                var failed = new ScriptResult { Name = Path.GetFileNameWithoutExtension(path), File = path, Passed = false, Error = ex.Message };
                results.Add(failed);
                if (!json) Console.Error.WriteLine($"✘ {failed.Name}：{ex.Message}");
                if (bail) break;
                continue;
            }

            if (!json) Console.WriteLine($"▶ {doc.DisplayName}（{DisplayPath(path)}）");
            var docDir = docRoot is null ? null : files.Count == 1 ? docRoot : Path.Combine(docRoot, SafeName(doc.DisplayName));
            var result = runner.Run(doc, new RunOptions
            {
                DocDir = docDir,
                DocFormats = docFormats,
                Cwd = Environment.CurrentDirectory,
                OnStep = json ? null : PrintStep,
            });
            results.Add(result);

            if (!json)
            {
                Console.WriteLine(result.Passed
                    ? $"✔ 通過（{result.Duration.TotalSeconds:0.0}s）"
                    : $"✘ 失敗（{result.Duration.TotalSeconds:0.0}s）：{result.Error}");
                if (result.FailureScreenshot is not null) Console.WriteLine($"  失敗截圖：{result.FailureScreenshot}");
                foreach (var f in result.DocFiles) Console.WriteLine($"  操作文件：{f}");
                Console.WriteLine();
            }
            if (!result.Passed && bail) break;
        }

        if (reporter == "junit")
        {
            var output = call.ResolvePath(call.Get("output") ?? "flaui-cli-results.xml");
            JUnitReporter.Write(results, output);
            if (!json) Console.WriteLine($"JUnit 報告：{output}");
        }

        var passed = results.Count(r => r.Passed);
        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(results.Select(r => new
            {
                r.Name, r.File, r.Passed, DurationMs = (long)r.Duration.TotalMilliseconds, r.Error, r.FailureScreenshot, r.DocFiles,
                Steps = r.Steps.Select(s => new { s.Index, s.Command, s.Passed, s.Error, DurationMs = (long)s.Duration.TotalMilliseconds, s.IsSetup }),
            }), new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        }
        else
        {
            Console.WriteLine($"總計 {results.Count} 個腳本：通過 {passed}，失敗 {results.Count - passed}");
        }
        return passed == results.Count ? ExitCodes.Success : ExitCodes.AssertionFailed;
    }

    private static void PrintStep(StepResult s)
    {
        var mark = s.Passed ? "✓" : "✗";
        var label = s.IsSetup ? "setup" : $"#{s.Index}";
        Console.WriteLine($"  {mark} {label} {s.Command}（{s.Duration.TotalMilliseconds:0}ms）");
        if (s.Error is not null) Console.WriteLine($"      {s.Error}");
    }

    /// <summary>在工作目錄之下顯示相對路徑，否則顯示完整路徑。</summary>
    private static string DisplayPath(string path)
    {
        var rel = Path.GetRelativePath(Environment.CurrentDirectory, path);
        return rel.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(rel) ? path : rel;
    }

    private static string SafeName(string name) =>
        string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    private static int InstallSkill(CommandCall call, bool json)
    {
        var source = Path.Combine(AppContext.BaseDirectory, "skills", "SKILL.md");
        if (!File.Exists(source)) throw new CliException($"找不到內建的 SKILL.md：{source}");
        var dir = call.Get("dir") is { } d ? call.ResolvePath(d) : Path.Combine(call.Cwd ?? Environment.CurrentDirectory, ".claude", "skills", "flaui-cli");
        Directory.CreateDirectory(dir);
        var dest = Path.Combine(dir, "SKILL.md");
        File.Copy(source, dest, overwrite: true);
        Output.Print(CommandResult.Success($"### Result\n已安裝 Agent 技能說明：{dest}"), json);
        return ExitCodes.Success;
    }
}
