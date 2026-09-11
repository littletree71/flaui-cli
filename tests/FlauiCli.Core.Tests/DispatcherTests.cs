using FlauiCli.Core.Abstractions;
using FlauiCli.Core.Commands;
using FlauiCli.Core.Protocol;
using FlauiCli.Core.Scripting;
using FlauiCli.Core.Tests.Fakes;

namespace FlauiCli.Core.Tests;

public sealed class DispatcherTests : IDisposable
{
    private readonly TestApp _app = new();

    public void Dispose() => _app.Dispose();

    private static void Ok(CommandResult r) => Assert.True(r.Ok, r.Error);

    [Fact]
    public void 規格表中每個daemon指令都有對應的handler()
    {
        foreach (var spec in CommandCatalog.All.Where(s => s.Location == CommandLocation.Daemon))
        {
            var r = _app.Dispatcher.Execute(new CommandCall(spec.Name) { Cwd = _app.Cwd });
            Assert.DoesNotContain("未知的指令", r.Error ?? "");
        }
    }

    [Fact]
    public void 未開啟應用程式時給出提示()
    {
        var r = _app.Run("snapshot");
        Assert.False(r.Ok);
        Assert.Equal(ExitCodes.Error, r.ExitCode);
        Assert.Contains("open", r.Error);
    }

    [Fact]
    public void Open後可以snapshot()
    {
        Ok(_app.Open());
        Assert.Equal(["calc.exe"], _app.Driver.Launched);

        var r = _app.Run("snapshot");
        Ok(r);
        Assert.Contains("- window \"Calculator\"", r.Text);
        Assert.Contains("button \"One\"", r.Text);
        Assert.Contains("id=num1Button", r.Text);
    }

    [Fact]
    public void 以ref與selector點擊並斷言()
    {
        Ok(_app.Open());
        var snapshot = _app.Run("snapshot").Text;
        var oneRef = snapshot.Split('\n').First(l => l.Contains("\"One\"")).Split("[ref=")[1].Split(']')[0];

        Ok(_app.Run("click", ("target", oneRef)));
        Ok(_app.Run("click", ("target", "id=plusButton")));
        Ok(_app.Run("click", ("target", "name=Two")));
        Ok(_app.Run("click", ("target", "type=Button&&name=Equals")));

        Ok(_app.Run("assert", ("kind", "text"), ("target", "id=CalculatorResults"), ("expected", "Display is 3")));

        var failed = _app.Run("assert", ("kind", "text"), ("target", "id=CalculatorResults"), ("expected", "Display is 4"), ("timeout", "200"));
        Assert.Equal(ExitCodes.AssertionFailed, failed.ExitCode);
        Assert.Contains("Display is 3", failed.Error);
    }

    [Fact]
    public void 找不到元素時逾時失敗()
    {
        Ok(_app.Open());
        var r = _app.Run("click", ("target", "id=nope"), ("timeout", "200"));
        Assert.Equal(ExitCodes.Error, r.ExitCode);
        Assert.Contains("找不到元素", r.Error);
    }

    [Fact]
    public void 停用的元素不能點擊()
    {
        Ok(_app.Open());
        var r = _app.Run("click", ("target", "id=disabledButton"), ("timeout", "200"));
        Assert.False(r.Ok);
        Assert.Contains("停用", r.Error);
    }

    [Fact]
    public void Fill優先使用ValuePattern()
    {
        Ok(_app.Open());
        Ok(_app.Run("fill", ("target", "id=nameInput"), ("text", "Alice")));
        Assert.Equal("Alice", _app.NameBox.Value);
        Assert.Contains("setvalue:Alice", _app.NameBox.Actions);
        Ok(_app.Run("assert", ("kind", "value"), ("target", "id=nameInput"), ("expected", "Alice")));
        Assert.Equal("Alice", _app.Run("get", ("kind", "text"), ("target", "id=nameInput")).Text);
    }

    [Fact]
    public void Fill可強制使用鍵盤()
    {
        Ok(_app.Open());
        _app.Driver.Focused = _app.NameBox;
        _app.NameBox.Value = "old";
        Ok(_app.Run("fill", ("target", "id=nameInput"), ("text", "new"), ("keyboard", "true")));
        Assert.Contains("type new", _app.Driver.Input.Log);
        Assert.Equal("new", _app.NameBox.Value);
    }

    [Fact]
    public void Check與Uncheck()
    {
        Ok(_app.Open());
        Ok(_app.Run("check", ("target", "id=agreeCheck")));
        Assert.Equal(ToggleValue.On, _app.Agree.Toggle);
        Ok(_app.Run("check", ("target", "id=agreeCheck")));
        Assert.Single(_app.Agree.Actions, "toggle");
        Ok(_app.Run("assert", ("kind", "checked"), ("target", "id=agreeCheck")));
        Ok(_app.Run("uncheck", ("target", "id=agreeCheck")));
        Assert.Equal(ToggleValue.Off, _app.Agree.Toggle);
    }

    [Fact]
    public void Select_ComboBox與清單()
    {
        Ok(_app.Open());
        Ok(_app.Run("select", ("target", "id=colorCombo"), ("item", "Green")));
        Assert.Equal("Green", _app.Colors.Value);
        Ok(_app.Run("select", ("target", "id=colorCombo"), ("item", "#0")));
        Assert.Equal("Red", _app.Colors.Value);

        Ok(_app.Run("select", ("target", "id=fruitList"), ("item", "Banana")));
        Assert.True(_app.Fruits.Children[1].IsSelected);

        var r = _app.Run("select", ("target", "id=fruitList"), ("item", "Mango"));
        Assert.Contains("Apple", r.Error);
    }

    [Fact]
    public void Press解析按鍵()
    {
        Ok(_app.Open());
        Ok(_app.Run("press", ("keys", "Ctrl+S")));
        Assert.Contains("press 0x11+0x53", _app.Driver.Input.Log);
        Assert.False(_app.Run("press", ("keys", "Ctrl+Nope")).Ok);
    }

    [Fact]
    public void YAML形式的斷言()
    {
        Ok(_app.Open());
        Ok(_app.Run("assert", ("target", "id=CalculatorResults"), ("contains", "is 0")));
        Ok(_app.Run("assert", ("target", "id=nope"), ("exists", "false")));
        Ok(_app.Run("assert", ("target", "id=disabledButton"), ("enabled", "false")));
        Assert.Equal(ExitCodes.AssertionFailed,
            _app.Run("assert", ("target", "id=CalculatorResults"), ("matches", "^Display is [1-9]$"), ("timeout", "100")).ExitCode);
    }

    [Fact]
    public void Wait狀態()
    {
        Ok(_app.Open());
        Ok(_app.Run("wait", ("target", "id=num1Button")));
        Ok(_app.Run("wait", ("target", "id=nope"), ("state", "gone")));
        var r = _app.Run("wait", ("target", "id=disabledButton"), ("state", "enabled"), ("timeout", "200"));
        Assert.Contains("逾時", r.Error);
    }

    [Fact]
    public void 錄製時ref轉成穩定選擇器()
    {
        Ok(_app.Open());
        Ok(_app.Run("record", ("action", "start"), ("name", "demo")));
        _app.Run("snapshot");
        var oneRef = _app.Session.Refs.GetOrAssign(_app.One);

        Ok(_app.Run("click", ("target", oneRef)));
        Ok(_app.Run("fill", ("target", "id=nameInput"), ("text", "Bob"), ("note", "輸入名字")));
        Ok(_app.Run("click", ("target", "name=Dup&&nth=1")));

        var stop = _app.Run("record", ("action", "stop"), ("out", "rec.yaml"));
        Ok(stop);
        var doc = ScriptYaml.Load(Path.Combine(_app.Cwd, "rec.yaml"));
        Assert.Equal("demo", doc.Name);
        Assert.Equal("calc.exe", doc.App?.Launch);
        Assert.Equal("Calculator", doc.App?.Window);
        Assert.Equal(3, doc.Steps.Count);
        Assert.Equal("id=num1Button", doc.Steps[0].Get("target"));
        Assert.Equal("輸入名字", doc.Steps[1].Get("note"));
        Assert.Equal("name=Dup&&nth=1", doc.Steps[2].Get("target"));
    }

    [Fact]
    public void 唯讀指令不會被錄製()
    {
        Ok(_app.Open());
        Ok(_app.Run("record", ("action", "start")));
        _app.Run("snapshot");
        _app.Run("get", ("kind", "name"), ("target", "id=num1Button"));
        _app.Run("find", ("text", "One"));
        Assert.Equal(0, _app.Session.Recorder!.Count);
    }

    [Fact]
    public void 操作文件自動截圖並產生Markdown與HTML()
    {
        Ok(_app.Open());
        Ok(_app.Run("doc", ("action", "start"), ("title", "計算機教學")));
        Ok(_app.Run("click", ("target", "id=num1Button")));
        Ok(_app.Run("click", ("target", "id=plusButton"), ("note", "按下加號")));
        Ok(_app.Run("doc", ("action", "step"), ("text", "完成")));
        var r = _app.Run("doc", ("action", "stop"), ("out", "manual"));
        Ok(r);

        var dir = Path.Combine(_app.Cwd, "manual");
        var md = File.ReadAllText(Path.Combine(dir, "index.md"));
        Assert.Contains("# 計算機教學", md);
        Assert.Contains("## 步驟 1：點擊「One」按鈕", md);
        Assert.Contains("## 步驟 2：按下加號", md);
        Assert.Contains("## 步驟 3：完成", md);
        Assert.Equal(3, Directory.GetFiles(Path.Combine(dir, "images"), "*.png").Length);
        Assert.Contains("data:image/png;base64,", File.ReadAllText(Path.Combine(dir, "index.html")));
    }

    [Fact]
    public void Close結束session並關閉視窗()
    {
        Ok(_app.Open());
        var r = _app.Run("close");
        Ok(r);
        Assert.Equal("true", r.Data?["shutdown"]);
        Assert.Contains("close", _app.Window.Actions);
        Assert.Null(_app.Session.CurrentWindow);
    }

    [Fact]
    public void Windows與視窗狀態()
    {
        Ok(_app.Open());
        Assert.Contains("[0] window \"Calculator\"", _app.Run("windows").Text);
        Ok(_app.Run("maximize"));
        Assert.Contains("state:Maximized", _app.Window.Actions);
        Ok(_app.Run("resize", ("width", "300"), ("height", "200")));
        Assert.Equal(300, _app.Window.Bounds.Width);
    }

    [Fact]
    public void Find搜尋文字()
    {
        Ok(_app.Open());
        var r = _app.Run("find", ("text", "display"));
        Ok(r);
        Assert.Contains("找到 1 個元素", r.Text);
        Assert.Contains("CalculatorResults", r.Text);
        Assert.False(_app.Run("find", ("text", "("), ("regex", "true")).Ok);
    }

    [Fact]
    public void Screenshot輸出檔案()
    {
        Ok(_app.Open());
        var r = _app.Run("screenshot", ("filename", "shot.png"), ("highlight", "id=num1Button\nid=num2Button"));
        Ok(r);
        Assert.True(File.Exists(Path.Combine(_app.Cwd, "shot.png")));
    }

    [Fact]
    public void UWP共用宿主程序的其他視窗不列入搜尋範圍()
    {
        _app.Window.ClassName = "ApplicationFrameWindow";
        var other = new FakeElement(ControlKind.Window, "Settings") { ProcessId = _app.Window.ProcessId, Bounds = new(500, 0, 300, 300) };
        other.Add(new FakeElement(ControlKind.Button, "Other One", "num1Button"));
        _app.Driver.TopLevel.Add(other);

        Ok(_app.Open());
        Assert.DoesNotContain("Settings", _app.Run("snapshot").Text);
        Assert.Single(_app.Session.SearchRoots());
        Assert.DoesNotContain("Settings", _app.Run("windows").Text);
    }

    [Fact]
    public void 一般程序的其他頂層視窗視為popup()
    {
        var popup = new FakeElement(ControlKind.Menu, "Context") { ProcessId = _app.Window.ProcessId, Bounds = new(500, 0, 100, 100) };
        popup.Add(new FakeElement(ControlKind.MenuItem, "Copy", "copyItem") { Bounds = new(500, 0, 100, 20) });
        _app.Driver.TopLevel.Add(popup);

        Ok(_app.Open());
        Assert.Contains("menuitem \"Copy\"", _app.Run("snapshot").Text);
        Ok(_app.Run("click", ("target", "id=copyItem")));
        Assert.Contains("click:Left", popup.Children[0].Actions);
    }

    [Fact]
    public void 視窗失效時以相同標題找回()
    {
        Ok(_app.Open());
        _app.Window.Alive = false;
        var reborn = new FakeElement(ControlKind.Window, "Calculator") { ProcessId = _app.Window.ProcessId, Bounds = _app.Window.Bounds };
        var one = new FakeElement(ControlKind.Button, "One", "num1Button") { Bounds = new(10, 100, 90, 40) };
        reborn.Add(one);
        _app.Driver.TopLevel.Add(reborn);

        Ok(_app.Run("click", ("target", "id=num1Button")));
        Assert.Same(reborn, _app.Session.CurrentWindow);
        Assert.Contains("click:Left", one.Actions);
    }

    [Fact]
    public void Open優先選擇新出現的同名視窗()
    {
        var old = new FakeElement(ControlKind.Window, "Calculator") { ProcessId = _app.Window.ProcessId, Bounds = new(600, 0, 100, 100), WindowHandle = 7 };
        _app.Driver.TopLevel.Add(old);
        Ok(_app.Open());
        Assert.Same(_app.Window, _app.Session.CurrentWindow);
    }

    [Fact]
    public void 子視窗形式的對話框可被wait_window找到()
    {
        Ok(_app.Open());
        var dialog = new FakeElement(ControlKind.Window, "Confirm") { Bounds = new(50, 50, 200, 100) };
        _app.Window.Add(dialog);

        Assert.Contains("\"Confirm\"", _app.Run("windows").Text);
        Ok(_app.Run("wait-window", ("title", "Confirm")));
        Assert.Same(dialog, _app.Session.CurrentWindow);
    }

    [Fact]
    public void 共用宿主視窗內的window元素不視為對話框()
    {
        _app.Window.ClassName = "ApplicationFrameWindow";
        _app.Window.Add(new FakeElement(ControlKind.Window, "Calculator", "TitleBar"));
        Ok(_app.Open());
        Assert.DoesNotContain("TitleBar", _app.Run("windows").Text);
    }

    [Fact]
    public void 元素消失後ref會依屬性重新尋找()
    {
        Ok(_app.Open());
        var r = _app.Session.Refs.GetOrAssign(_app.One);
        // 模擬元素被重建：舊物件失效，新物件有相同的 AutomationId
        _app.One.Alive = false;
        var replacement = new FakeElement(ControlKind.Button, "One", "num1Button") { Bounds = _app.One.Bounds };
        _app.Window.Add(replacement);
        Ok(_app.Run("click", ("target", r)));
        Assert.Contains("click:Left", replacement.Actions);
    }
}
