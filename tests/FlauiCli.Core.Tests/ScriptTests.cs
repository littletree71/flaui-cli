using System.Xml.Linq;
using FlauiCli.Core.Commands;
using FlauiCli.Core.Protocol;
using FlauiCli.Core.Scripting;
using FlauiCli.Core.Tests.Fakes;

namespace FlauiCli.Core.Tests;

public class ScriptTests
{
    private const string Sample = """
        name: 範例
        app:
          launch: calc.exe
          window: Calculator
        timeout: 3000
        steps:
          - click: id=num1Button
            doc: 按下 1
          - fill: { target: id=nameInput, text: "Alice Chen" }
          - press: Ctrl+A
          - screenshot:
              filename: a.png
              highlight: [id=a, id=b]
          - assert: { target: id=CalculatorResults, text: Display is 1 }
          - close
        """;

    [Fact]
    public void 解析各種步驟格式()
    {
        var doc = ScriptYaml.Parse(Sample);
        Assert.Equal("範例", doc.Name);
        Assert.Equal("calc.exe", doc.App!.Launch);
        Assert.Equal(3000, doc.Timeout);
        Assert.Equal(6, doc.Steps.Count);

        Assert.Equal("click", doc.Steps[0].Command);
        Assert.Equal("id=num1Button", doc.Steps[0].Get("target"));
        Assert.Equal("按下 1", doc.Steps[0].Get("note"));
        Assert.Equal("Alice Chen", doc.Steps[1].Get("text"));
        Assert.Equal("Ctrl+A", doc.Steps[2].Get("keys"));
        Assert.Equal(["id=a", "id=b"], doc.Steps[3].GetList("highlight"));
        Assert.Equal("Display is 1", doc.Steps[4].Get("text"));
        Assert.Equal("close", doc.Steps[5].Command);
    }

    [Theory]
    [InlineData("steps:\n  - nope: x", "未知的指令")]
    [InlineData("steps:\n  - click: a\n    fill: b", "只能有一個指令")]
    [InlineData("steps:\n  - run: a.yaml", "不能用在腳本中")]
    [InlineData("foo: 1", "未知的欄位")]
    [InlineData("- a\n- b", "最上層必須是 mapping")]
    [InlineData("steps: [", "YAML 格式錯誤")]
    [InlineData("timeout: abc", "timeout 必須是整數")]
    public void 錯誤訊息清楚(string yaml, string message)
    {
        var ex = Assert.Throws<CliException>(() => ScriptYaml.Parse(yaml));
        Assert.Contains(message, ex.Message);
    }

    [Fact]
    public void 儲存後可以重新載入()
    {
        var doc = ScriptYaml.Parse(Sample);
        var yaml = ScriptYaml.Save(doc);
        var again = ScriptYaml.Parse(yaml);

        Assert.Equal(doc.Name, again.Name);
        Assert.Equal(doc.App!.Window, again.App!.Window);
        Assert.Equal(doc.Steps.Count, again.Steps.Count);
        for (var i = 0; i < doc.Steps.Count; i++)
        {
            Assert.Equal(doc.Steps[i].Command, again.Steps[i].Command);
            Assert.Equal(doc.Steps[i].Args, again.Steps[i].Args);
        }
        Assert.Contains("- click: id=num1Button", yaml);
    }

    [Fact]
    public void Runner執行腳本並回報每個步驟()
    {
        using var app = new TestApp();
        var doc = ScriptYaml.Parse("""
            name: 加法
            app: { launch: calc.exe, window: Calculator }
            steps:
              - click: id=num1Button
              - click: id=plusButton
              - click: id=num2Button
              - click: id=equalButton
              - assert: { target: id=CalculatorResults, text: Display is 3 }
            """);
        var steps = new List<StepResult>();
        var result = new ScriptRunner(() => app.Driver).Run(doc, new RunOptions { Cwd = app.Cwd, OnStep = steps.Add });

        Assert.True(result.Passed, result.Error);
        Assert.Equal(6, steps.Count);
        Assert.True(steps[0].IsSetup);
        Assert.Contains("close", app.Window.Actions);
    }

    [Fact]
    public void Runner在失敗時停止並記錄錯誤()
    {
        using var app = new TestApp();
        var doc = ScriptYaml.Parse("""
            app: { launch: calc.exe, window: Calculator }
            timeout: 200
            steps:
              - click: id=num1Button
              - assert: { target: id=CalculatorResults, text: Display is 9 }
              - click: id=num2Button
            """);
        var result = new ScriptRunner(() => app.Driver).Run(doc, new RunOptions { Cwd = app.Cwd });

        Assert.False(result.Passed);
        Assert.Contains("第 2 步", result.Error);
        Assert.Equal(3, result.Steps.Count); // setup + 2 步，第 3 步不執行
        Assert.DoesNotContain("click:Left", app.Two.Actions);
        Assert.NotNull(result.FailureScreenshot);
    }

    [Fact]
    public void JUnit報告()
    {
        var pass = new ScriptResult { Name = "a", File = "a.yaml", Passed = true, Duration = TimeSpan.FromSeconds(1) };
        var fail = new ScriptResult { Name = "b", File = "b.yaml", Passed = false, Error = "第 1 步失敗：x" };
        fail.Steps.Add(new StepResult(1, "click id=x", false, "x", TimeSpan.FromMilliseconds(5)));

        var xml = XDocument.Parse(JUnitReporter.Render([pass, fail], new DateTime(2026, 9, 10)));
        var suite = xml.Root!.Element("testsuite")!;
        Assert.Equal("2", suite.Attribute("tests")!.Value);
        Assert.Equal("1", suite.Attribute("failures")!.Value);
        var failure = suite.Elements("testcase").Last().Element("failure")!;
        Assert.Equal("第 1 步失敗：x", failure.Attribute("message")!.Value);
        Assert.Contains("click id=x", failure.Value);
    }

    [Fact]
    public void 指令格式化成CLI字串()
    {
        var call = new CommandCall("fill").Set("target", "id=a").Set("text", "hello world").Set("keyboard", "true");
        Assert.Equal("fill id=a \"hello world\" --keyboard", CommandFormatter.ToCli(call));

        var shot = new CommandCall("screenshot").Set("highlight", "id=a\nid=b");
        Assert.Equal("screenshot --highlight id=a --highlight id=b", CommandFormatter.ToCli(shot));
    }

    [Fact]
    public void 規格表的名稱不重複且主要參數存在()
    {
        var names = CommandCatalog.All.Select(s => s.Name).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
        foreach (var spec in CommandCatalog.All)
        {
            var all = spec.Args.Select(a => a.Name).Concat(spec.Options.Select(o => o.Name)).ToList();
            Assert.Equal(all.Count, all.Distinct().Count());
            // 選填位置參數之後不可再有必填參數
            var seenOptional = false;
            foreach (var a in spec.Args)
            {
                Assert.False(seenOptional && a.Required, $"{spec.Name}：必填參數 {a.Name} 位於選填參數之後");
                seenOptional |= !a.Required;
            }
        }
    }
}
