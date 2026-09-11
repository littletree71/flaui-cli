using System.Xml.Linq;
using FlauiCli.Core.Commands;
using FlauiCli.Core.Protocol;
using FlauiCli.Core.Scripting;
using FlauiCli.Core.Tests.Fakes;

namespace FlauiCli.Core.Tests;

public class ScriptTests
{
    private const string Sample = """
        name: Sample
        app:
          launch: calc.exe
          window: Calculator
        timeout: 3000
        steps:
          - click: id=num1Button
            doc: Press 1
          - fill: { target: id=nameInput, text: "Alice Chen" }
          - press: Ctrl+A
          - screenshot:
              filename: a.png
              highlight: [id=a, id=b]
          - assert: { target: id=CalculatorResults, text: Display is 1 }
          - close
        """;

    [Fact]
    public void ParsesEveryStepFormat()
    {
        var doc = ScriptYaml.Parse(Sample);
        Assert.Equal("Sample", doc.Name);
        Assert.Equal("calc.exe", doc.App!.Launch);
        Assert.Equal(3000, doc.Timeout);
        Assert.Equal(6, doc.Steps.Count);

        Assert.Equal("click", doc.Steps[0].Command);
        Assert.Equal("id=num1Button", doc.Steps[0].Get("target"));
        Assert.Equal("Press 1", doc.Steps[0].Get("note"));
        Assert.Equal("Alice Chen", doc.Steps[1].Get("text"));
        Assert.Equal("Ctrl+A", doc.Steps[2].Get("keys"));
        Assert.Equal(["id=a", "id=b"], doc.Steps[3].GetList("highlight"));
        Assert.Equal("Display is 1", doc.Steps[4].Get("text"));
        Assert.Equal("close", doc.Steps[5].Command);
    }

    [Theory]
    [InlineData("steps:\n  - nope: x", "unknown command")]
    [InlineData("steps:\n  - click: a\n    fill: b", "only one command per step")]
    [InlineData("steps:\n  - run: a.yaml", "cannot be used in scripts")]
    [InlineData("foo: 1", "unknown field")]
    [InlineData("- a\n- b", "the top level must be a mapping")]
    [InlineData("steps: [", "invalid YAML")]
    [InlineData("timeout: abc", "timeout must be an integer")]
    public void ErrorMessagesAreClear(string yaml, string message)
    {
        var ex = Assert.Throws<CliException>(() => ScriptYaml.Parse(yaml));
        Assert.Contains(message, ex.Message);
    }

    [Fact]
    public void SavedScriptsCanBeLoadedAgain()
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
    public void RunnerExecutesTheScriptAndReportsEachStep()
    {
        using var app = new TestApp();
        var doc = ScriptYaml.Parse("""
            name: Addition
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
    public void RunnerStopsAtTheFirstFailure()
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
        Assert.Contains("Step 2", result.Error);
        Assert.Equal(3, result.Steps.Count); // setup + 2 steps; step 3 never runs
        Assert.DoesNotContain("click:Left", app.Two.Actions);
        Assert.NotNull(result.FailureScreenshot);
    }

    [Fact]
    public void JUnitReport()
    {
        var pass = new ScriptResult { Name = "a", File = "a.yaml", Passed = true, Duration = TimeSpan.FromSeconds(1) };
        var fail = new ScriptResult { Name = "b", File = "b.yaml", Passed = false, Error = "Step 1 failed: x" };
        fail.Steps.Add(new StepResult(1, "click id=x", false, "x", TimeSpan.FromMilliseconds(5)));

        var xml = XDocument.Parse(JUnitReporter.Render([pass, fail], new DateTime(2026, 9, 10)));
        var suite = xml.Root!.Element("testsuite")!;
        Assert.Equal("2", suite.Attribute("tests")!.Value);
        Assert.Equal("1", suite.Attribute("failures")!.Value);
        var failure = suite.Elements("testcase").Last().Element("failure")!;
        Assert.Equal("Step 1 failed: x", failure.Attribute("message")!.Value);
        Assert.Contains("click id=x", failure.Value);
    }

    [Fact]
    public void CommandsAreFormattedAsCliLines()
    {
        var call = new CommandCall("fill").Set("target", "id=a").Set("text", "hello world").Set("keyboard", "true");
        Assert.Equal("fill id=a \"hello world\" --keyboard", CommandFormatter.ToCli(call));

        var shot = new CommandCall("screenshot").Set("highlight", "id=a\nid=b");
        Assert.Equal("screenshot --highlight id=a --highlight id=b", CommandFormatter.ToCli(shot));
    }

    [Fact]
    public void CatalogNamesAreUniqueAndArgumentsAreOrdered()
    {
        var names = CommandCatalog.All.Select(s => s.Name).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
        foreach (var spec in CommandCatalog.All)
        {
            var all = spec.Args.Select(a => a.Name).Concat(spec.Options.Select(o => o.Name)).ToList();
            Assert.Equal(all.Count, all.Distinct().Count());
            // A required positional argument must not follow an optional one
            var seenOptional = false;
            foreach (var a in spec.Args)
            {
                Assert.False(seenOptional && a.Required, $"{spec.Name}: required argument {a.Name} follows an optional one");
                seenOptional |= !a.Required;
            }
        }
    }
}
