namespace FlauiCli.E2E.Tests;

/// <summary>
/// 以真正的 flaui-cli.exe（daemon + Named Pipe）操作應用程式的端對端測試。
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
        try { Directory.Delete(_work, recursive: true); } catch { /* 忽略 */ }
    }

    private CliRun Ok(params string[] args)
    {
        var r = _cli.Run(args);
        Assert.True(r.ExitCode == 0, $"flaui-cli {string.Join(' ', args)} 失敗（{r.ExitCode}）：{r.All}");
        return r;
    }

    private void OpenWpfSample() => Ok("open", TestEnvironment.WpfSampleExe);

    [Fact]
    public void 沒有session時回傳結束碼3()
    {
        var r = _cli.Run("snapshot");
        Assert.Equal(3, r.ExitCode);
        Assert.Contains("沒有在執行", r.StdErr);
    }

    [Fact]
    public void 小算盤加法()
    {
        // 小算盤是 UWP：calc.exe 啟動後立即結束，需要用視窗標題附加（中英文系統都支援）
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

        // 以結尾數字比對，避免依賴系統語系（Display is 3 / 顯示為 3）
        Ok("assert", "matches", "id=CalculatorResults", "\\b3$");
        var failed = _cli.Run("assert", "matches", "id=CalculatorResults", "\\b4$", "--timeout", "500");
        Assert.Equal(1, failed.ExitCode);
        Assert.Contains("斷言失敗", failed.StdErr);

        Ok("screenshot", "--highlight", "id=CalculatorResults", "--filename", "calc.png");
        Assert.True(File.Exists(Path.Combine(_work, "calc.png")));
        Ok("close");
    }

    [Fact]
    public void Wpf表單操作()
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
    public void Ref在指令之間保留()
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
    public void 對話框與視窗切換()
    {
        OpenWpfSample();
        // 開啟強制回應對話框後 click 會一直等待，因此用 invoke 觸發
        Ok("click", "id=dialogButton", "--invoke");
        Ok("wait-window", "Confirm");
        Assert.Contains("Confirm", Ok("windows").StdOut);
        Ok("click", "id=dialogOkButton");
        Ok("window", "WpfSample");
        Ok("assert", "text", "id=statusText", "Dialog accepted");
    }

    [Fact]
    public void 錄製指令後可重播()
    {
        OpenWpfSample();
        Ok("record", "start", "--name", "表單");
        Ok("fill", "id=nameInput", "Rec");
        var r = Ok("snapshot").StdOut.Split('\n').First(l => l.Contains("id=agreeCheck")).Split("[ref=")[1].Split(']')[0];
        Ok("check", r);
        Ok("click", "id=submitButton");
        Ok("assert", "contains", "id=resultText", "agree=True");
        Ok("record", "stop", "--out", "rec.yaml");
        Ok("close");

        var yaml = File.ReadAllText(Path.Combine(_work, "rec.yaml"));
        Assert.Contains("id=agreeCheck", yaml);      // ref 已轉成穩定 selector
        Assert.DoesNotContain("[ref=", yaml);

        var run = Ok("run", "rec.yaml", "--reporter", "junit", "--output", "report.xml");
        Assert.Contains("通過 1", run.StdOut);
        Assert.Contains("failures=\"0\"", File.ReadAllText(Path.Combine(_work, "report.xml")));
    }

    [Fact]
    public void 腳本產生操作文件()
    {
        var script = Path.Combine(_work, "form.flow.yaml");
        File.WriteAllText(script, $$"""
            name: 填寫表單
            app:
              launch: '{{TestEnvironment.WpfSampleExe}}'
            steps:
              - fill: { target: id=nameInput, text: Doc }
                doc: 在 Name 欄位輸入名字
              - check: id=agreeCheck
              - click: id=submitButton
                doc: 按下 Submit 送出
              - assert: { target: id=statusText, text: Submitted }
            """);

        var run = Ok("run", script, "--doc", "manual");
        Assert.Contains("✔", run.StdOut);
        var md = File.ReadAllText(Path.Combine(_work, "manual", "index.md"));
        Assert.Contains("## 步驟 1：在 Name 欄位輸入名字", md);
        Assert.Contains("## 步驟 2：勾選「I agree」核取方塊", md);
        Assert.Contains("## 步驟 3：按下 Submit 送出", md);
        Assert.Equal(3, Directory.GetFiles(Path.Combine(_work, "manual", "images")).Length);
        Assert.True(File.Exists(Path.Combine(_work, "manual", "index.html")));
    }

    [Fact]
    public void 失敗的腳本回傳結束碼1()
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
