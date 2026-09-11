namespace FlauiCli.E2E.Tests;

/// <summary>
/// End-to-end tests that drive applications through the real flaui-cli.exe (daemon + named pipe).
/// </summary>
[Trait("Category", "E2E")]
public sealed class CliEndToEndTests : IDisposable
{
    private readonly string _work = TestEnvironment.NewTempDir();
    private readonly CliSession _cli;

    public CliEndToEndTests() => _cli = new CliSession(_work);

    public void Dispose()
    {
        _cli.Dispose();
        try { Directory.Delete(_work, recursive: true); } catch { /* ignore */ }
    }

    private CliRun Ok(params string[] args)
    {
        var r = _cli.Run(args);
        Assert.True(r.ExitCode == 0, $"flaui-cli {string.Join(' ', args)} failed ({r.ExitCode}): {r.All}");
        return r;
    }

    private void OpenWpfSample() => Ok("open", TestEnvironment.WpfSampleExe);

    [Fact]
    public void ExitCode3WithoutASession()
    {
        var r = _cli.Run("snapshot");
        Assert.Equal(3, r.ExitCode);
        Assert.Contains("is not running", r.StdErr);
    }

    [Fact]
    public void CalculatorAddition()
    {
        // Calculator is a UWP app: calc.exe exits immediately, so attach by window title (English or Chinese Windows)
        var open = _cli.Run("open", "calc.exe", "--window", "Calculator");
        if (open.ExitCode != 0) Ok("open", "calc.exe", "--window", "小算盤");

        var snapshot = Ok("snapshot").StdOut;
        Assert.Contains("id=num1Button", snapshot);

        Ok("click", "id=clearButton");
        Ok("click", "id=num1Button");
        Ok("click", "id=plusButton");
        Ok("click", "id=num2Button");
        var click = Ok("click", "id=equalButton").StdOut;
        Assert.Contains("[Snapshot](", click);

        // Match only the trailing number so the test does not depend on the OS language ("Display is 3")
        Ok("assert", "matches", "id=CalculatorResults", "\\b3$");
        var failed = _cli.Run("assert", "matches", "id=CalculatorResults", "\\b4$", "--timeout", "500");
        Assert.Equal(1, failed.ExitCode);
        Assert.Contains("Assertion failed", failed.StdErr);

        Ok("screenshot", "--highlight", "id=CalculatorResults", "--filename", "calc.png");
        Assert.True(File.Exists(Path.Combine(_work, "calc.png")));
        Ok("close");
    }

    [Fact]
    public void WpfFormOperations()
    {
        OpenWpfSample();
        Ok("fill", "id=nameInput", "Alice");
        Ok("fill", "id=passwordInput", "secret", "--keyboard");
        Ok("select", "id=colorCombo", "Blue");
        Ok("select", "id=fruitList", "Cherry");
        Ok("check", "id=agreeCheck");
        Ok("click", "id=submitButton");
        Ok("assert", "text", "id=resultText", "Hello, Alice! color=Blue, fruit=Cherry, agree=True, password=6");
        Ok("assert", "disabled", "id=disabledButton");

        Ok("click", "id=delayButton");
        Ok("wait", "name=\"Delayed done\"", "--timeout", "5000");

        var json = Ok("get", "value", "id=nameInput", "--json").StdOut;
        Assert.Contains("\"value\": \"Alice\"", json);
    }

    [Fact]
    public void RefsSurviveAcrossCommands()
    {
        OpenWpfSample();
        var snapshot = Ok("snapshot").StdOut;
        var line = snapshot.Split('\n').First(l => l.Contains("id=submitButton"));
        var r = line.Split("[ref=")[1].Split(']')[0];
        Ok("fill", "id=nameInput", "Ref");
        Ok("click", r);
        Ok("assert", "contains", "id=resultText", "Hello, Ref!");
    }

    [Fact]
    public void DialogsAndWindowSwitching()
    {
        OpenWpfSample();
        // A modal dialog blocks a physical click from returning, so trigger it with invoke
        Ok("click", "id=dialogButton", "--invoke");
        Ok("wait-window", "Confirm");
        Assert.Contains("Confirm", Ok("windows").StdOut);
        Ok("click", "id=dialogOkButton");
        Ok("window", "WpfSample");
        Ok("assert", "text", "id=statusText", "Dialog accepted");
    }

    [Fact]
    public void RecordedCommandsCanBeReplayed()
    {
        OpenWpfSample();
        Ok("record", "start", "--name", "Form");
        Ok("fill", "id=nameInput", "Rec");
        var r = Ok("snapshot").StdOut.Split('\n').First(l => l.Contains("id=agreeCheck")).Split("[ref=")[1].Split(']')[0];
        Ok("check", r);
        Ok("click", "id=submitButton");
        Ok("assert", "contains", "id=resultText", "agree=True");
        Ok("record", "stop", "--out", "rec.yaml");
        Ok("close");

        var yaml = File.ReadAllText(Path.Combine(_work, "rec.yaml"));
        Assert.Contains("id=agreeCheck", yaml);      // the ref was turned into a stable selector
        Assert.DoesNotContain("[ref=", yaml);

        var run = Ok("run", "rec.yaml", "--reporter", "junit", "--output", "report.xml");
        Assert.Contains("1 passed", run.StdOut);
        Assert.Contains("failures=\"0\"", File.ReadAllText(Path.Combine(_work, "report.xml")));
    }

    [Fact]
    public void ScriptsProduceDocuments()
    {
        var script = Path.Combine(_work, "form.flow.yaml");
        File.WriteAllText(script, $$"""
            name: Fill in the form
            app:
              launch: '{{TestEnvironment.WpfSampleExe}}'
            steps:
              - fill: { target: id=nameInput, text: Doc }
                doc: Enter a name in the Name field
              - check: id=agreeCheck
              - click: id=submitButton
                doc: Click Submit
              - assert: { target: id=statusText, text: Submitted }
            """);

        var run = Ok("run", script, "--doc", "manual");
        Assert.Contains("✔", run.StdOut);
        var md = File.ReadAllText(Path.Combine(_work, "manual", "index.md"));
        Assert.Contains("## Step 1: Enter a name in the Name field", md);
        Assert.Contains("## Step 2: Check the \"I agree\" check box", md);
        Assert.Contains("## Step 3: Click Submit", md);
        Assert.Equal(3, Directory.GetFiles(Path.Combine(_work, "manual", "images")).Length);
        Assert.True(File.Exists(Path.Combine(_work, "manual", "index.html")));
    }

    [Fact]
    public void FailingScriptsExitWithCode1()
    {
        var script = Path.Combine(_work, "fail.flow.yaml");
        File.WriteAllText(script, $$"""
            app:
              launch: '{{TestEnvironment.WpfSampleExe}}'
            timeout: 500
            steps:
              - assert: { target: id=statusText, text: Nope }
            """);
        var r = _cli.Run("run", script);
        Assert.Equal(1, r.ExitCode);
        Assert.Contains("✘", r.StdOut);
    }
}
