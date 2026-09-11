using System.Globalization;
using System.Text;
using System.Xml.Linq;

namespace FlauiCli.Core.Scripting;

/// <summary>輸出 JUnit XML 報告（供 CI 使用）。每個腳本是一個 testcase。</summary>
public static class JUnitReporter
{
    public static string Render(IReadOnlyList<ScriptResult> results, DateTime timestamp)
    {
        static string Sec(TimeSpan t) => t.TotalSeconds.ToString("0.000", CultureInfo.InvariantCulture);

        var failures = results.Count(r => !r.Passed);
        var total = TimeSpan.FromTicks(results.Sum(r => r.Duration.Ticks));

        var suite = new XElement("testsuite",
            new XAttribute("name", "flaui-cli"),
            new XAttribute("tests", results.Count),
            new XAttribute("failures", failures),
            new XAttribute("errors", 0),
            new XAttribute("time", Sec(total)),
            new XAttribute("timestamp", timestamp.ToString("s", CultureInfo.InvariantCulture)));

        foreach (var r in results)
        {
            var log = new StringBuilder();
            foreach (var s in r.Steps)
            {
                log.Append(s.Passed ? "[通過] " : "[失敗] ")
                   .Append(s.IsSetup ? "setup" : $"#{s.Index}")
                   .Append(' ').Append(s.Command)
                   .Append($" ({s.Duration.TotalMilliseconds:0}ms)");
                if (s.Error is not null) log.Append(" — ").Append(s.Error);
                log.AppendLine();
            }

            var testcase = new XElement("testcase",
                new XAttribute("name", r.Name),
                new XAttribute("classname", r.File is null ? "flaui-cli" : Path.GetFileName(r.File)),
                new XAttribute("time", Sec(r.Duration)));
            if (!r.Passed)
            {
                var detail = log.ToString();
                if (r.FailureScreenshot is not null) detail += $"失敗截圖：{r.FailureScreenshot}\n";
                testcase.Add(new XElement("failure", new XAttribute("message", r.Error ?? "失敗"), detail));
            }
            testcase.Add(new XElement("system-out", log.ToString()));
            suite.Add(testcase);
        }

        var doc = new XDocument(new XDeclaration("1.0", "utf-8", null),
            new XElement("testsuites",
                new XAttribute("tests", results.Count),
                new XAttribute("failures", failures),
                new XAttribute("time", Sec(total)),
                suite));
        return doc.Declaration + Environment.NewLine + doc.ToString();
    }

    public static void Write(IReadOnlyList<ScriptResult> results, string path)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, Render(results, DateTime.Now), new UTF8Encoding(false));
    }
}
