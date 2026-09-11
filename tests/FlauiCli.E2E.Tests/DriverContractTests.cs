using System.Diagnostics;
using FlaUI.Core.Definitions;
using FlauiCli.Core.Abstractions;
using FlauiCli.Drivers;

namespace FlauiCli.E2E.Tests;

/// <summary>
/// FlaUI 轉接層的契約測試：直接對真實的 WPF 程式驗證 <see cref="IUiDriver"/> / <see cref="IUiElement"/> 的每個行為。
/// 升級 FlaUI 版本時必須全部通過，這是 Core 單元測試（使用 FakeDriver）無法涵蓋的部分。
/// </summary>
[Trait("Category", "E2E")]
public sealed class DriverContractTests : IDisposable
{
    private readonly FlaUIDriver _driver = new();
    private readonly IAppProcess _app;
    private readonly IUiElement _window;

    public DriverContractTests()
    {
        _app = _driver.Launch(TestEnvironment.WpfSampleExe, null, null);
        _window = _app.GetMainWindow(TimeSpan.FromSeconds(15)) ?? throw new InvalidOperationException("WpfSample 沒有開啟視窗");
        _window.SetForeground();
    }

    public void Dispose()
    {
        try { _app.Kill(); } catch { /* 忽略 */ }
        _app.Dispose();
        _driver.Dispose();
    }

    private IUiElement ById(string id) =>
        _window.FindAll(new ElementQuery(AutomationId: id)).FirstOrDefault() ?? throw new InvalidOperationException($"找不到 {id}");

    private static void WaitUntil(Func<bool> condition, int timeoutMs = 3000)
    {
        var sw = Stopwatch.StartNew();
        while (!condition())
        {
            if (sw.ElapsedMilliseconds > timeoutMs) throw new TimeoutException("條件未在時限內成立");
            Thread.Sleep(50);
        }
    }

    // ───────────── 不需要 UI 的對應檢查 ─────────────

    [Fact]
    public void 列舉對應完整()
    {
        foreach (var t in Enum.GetValues<ControlType>())
            Assert.True(Enum.IsDefined(Mapping.ToKind(t)) && (t == ControlType.Unknown || Mapping.ToKind(t) != ControlKind.Unknown),
                $"FlaUI ControlType.{t} 沒有對應的 ControlKind");
        foreach (var k in Enum.GetValues<ControlKind>())
            Assert.Equal(k, Mapping.ToKind(Mapping.ToControlType(k)));
        foreach (var s in Enum.GetValues<ToggleState>())
            Assert.Equal(s.ToString(), Mapping.ToToggle(s).ToString());
        foreach (var s in Enum.GetValues<ExpandCollapseState>())
            Assert.Equal(s.ToString(), Mapping.ToExpand(s).ToString());
    }

    // ───────────── 屬性 ─────────────

    [Fact]
    public void 基本屬性()
    {
        var name = ById("nameInput");
        Assert.Equal(ControlKind.Edit, name.Kind);
        Assert.Equal("Name", name.Name);
        Assert.Equal("WPF", name.FrameworkId);
        Assert.True(name.IsEnabled);
        Assert.False(name.IsOffscreen);
        Assert.False(name.Bounds.IsEmpty);
        Assert.Equal(_app.ProcessId, name.ProcessId);
        Assert.NotNull(name.RuntimeId);
        Assert.Equal("", name.Value);
        Assert.False(name.IsReadOnly);
        Assert.True(name.IsAlive);
        Assert.NotNull(name.Parent);
        Assert.Null(_driver.GetTopLevelWindows().First().Parent?.Parent); // 頂層視窗的父元素是桌面

        Assert.Equal(ControlKind.Window, _window.Kind);
        Assert.Equal("WpfSample", _window.Name);
        Assert.NotEqual(0, _window.WindowHandle);
    }

    [Fact]
    public void 狀態屬性()
    {
        Assert.False(ById("disabledButton").IsEnabled);
        Assert.True(ById("passwordInput").IsPassword);
        Assert.Equal(ToggleValue.Off, ById("agreeCheck").Toggle);
        Assert.Equal(ExpandValue.Collapsed, ById("colorCombo").Expand);
        Assert.Contains("Invoke", ById("submitButton").SupportedPatterns);
        // 在地化類型名稱依系統介面語系而異（例如「按鈕」），只檢查讀得到
        Assert.False(string.IsNullOrEmpty(ById("submitButton").GetProperty("LocalizedControlType")));
        Assert.Null(ById("submitButton").GetProperty("NoSuchProperty"));
    }

    [Fact]
    public void 同一元素相等()
    {
        var a = ById("submitButton");
        var b = _window.FindAll(new ElementQuery(Name: "Submit", Kind: ControlKind.Button)).Single();
        Assert.True(a.Equals(b));
        Assert.False(a.Equals(ById("dialogButton")));
        Assert.Equal(a.RuntimeId, b.RuntimeId);
    }

    // ───────────── 搜尋 ─────────────

    [Fact]
    public void 搜尋條件與範圍()
    {
        Assert.Single(_window.FindAll(new ElementQuery(AutomationId: "submitButton", Kind: ControlKind.Button)));
        Assert.Empty(_window.FindAll(new ElementQuery(AutomationId: "submitButton", Kind: ControlKind.Edit)));
        Assert.Empty(_window.FindAll(new ElementQuery(AutomationId: "submitButton"), SearchScope.Children));
        Assert.True(_window.FindAll(ElementQuery.Any).Count > 10);
        Assert.Contains(_driver.GetTopLevelWindows(_app.ProcessId), w => w.Equals(_window));
    }

    [Fact]
    public void XPath往返()
    {
        var submit = ById("submitButton");
        var xpath = submit.GetXPathFrom(_window);
        Assert.False(string.IsNullOrEmpty(xpath));
        var found = _window.FindByXPath(xpath!);
        Assert.True(found.Any(e => e.Equals(submit)),
            $"xpath={xpath}，找到 {found.Count} 個：{string.Join(" | ", found)}；//Button 共 {_window.FindByXPath("//Button").Count} 個");
        Assert.Single(_window.FindByXPath("//Button[@AutomationId='submitButton']"));
    }

    [Fact]
    public void 快照樹包含控制項與狀態()
    {
        ById("nameInput").TrySetValue("snap");
        var nodes = _window.CaptureTree().DescendantsAndSelf().ToList();
        var name = nodes.Single(n => n.AutomationId == "nameInput");
        Assert.Equal(ControlKind.Edit, name.Kind);
        Assert.Equal("snap", name.Value);
        Assert.Contains(nodes, n => n.AutomationId == "agreeCheck" && n.Toggle == ToggleValue.Off);
        Assert.Contains(nodes, n => n.AutomationId == "disabledButton" && !n.IsEnabled);
        Assert.All(nodes, n => Assert.NotNull(n.RuntimeId));
        Assert.True(name.Element.Equals(ById("nameInput")));
    }

    [Fact]
    public void FromPoint與焦點()
    {
        var submit = ById("submitButton");
        // 命中測試回傳最深的元素（WPF 按鈕中央是其內容文字），因此接受按鈕本身或其子元素
        var hit = _driver.FromPoint(submit.Bounds.Center());
        Assert.NotNull(hit);
        Assert.True(hit!.Equals(submit) || hit.Parent?.Equals(submit) == true, $"FromPoint 回傳 {hit}");
        var name = ById("nameInput");
        name.Focus();
        WaitUntil(() => _driver.GetFocusedElement()?.Equals(name) == true);
        Assert.True(_driver.FromHandle(_window.WindowHandle)?.Equals(_window));
    }

    // ───────────── Pattern 操作 ─────────────

    [Fact]
    public void Invoke與SetValue()
    {
        Assert.True(ById("nameInput").TrySetValue("Alice"));
        Assert.True(ById("submitButton").TryInvoke());
        WaitUntil(() => ById("resultText").Name.StartsWith("Hello, Alice!"));
        Assert.False(ById("resultText").TrySetValue("x"));
    }

    [Fact]
    public void Toggle()
    {
        var check = ById("agreeCheck");
        Assert.True(check.TryToggle());
        Assert.Equal(ToggleValue.On, check.Toggle);
        Assert.False(ById("submitButton").TryToggle());
    }

    [Fact]
    public void ComboBox選取()
    {
        var combo = ById("colorCombo");
        Assert.True(combo.TrySelectComboBoxItem("Green", null, out var selected));
        Assert.Equal("Green", selected);
        Assert.Equal("Green", combo.Value);
        Assert.True(combo.TrySelectComboBoxItem(null, 2, out selected));
        Assert.Equal("Blue", selected);
        Assert.False(combo.TrySelectComboBoxItem("Purple", null, out _));
        Assert.False(ById("nameInput").TrySelectComboBoxItem("x", null, out _));
    }

    [Fact]
    public void 清單項目選取()
    {
        var item = ById("fruitList").FindAll(new ElementQuery(Name: "Banana")).First(e => e.Kind == ControlKind.ListItem);
        Assert.False(item.IsSelected);
        Assert.True(item.TrySelectItem());
        Assert.True(item.IsSelected);
    }

    [Fact]
    public void 索引標籤與樹狀展開()
    {
        var tab = ById("treeTab");
        Assert.True(tab.TrySelectItem());
        WaitUntil(() => _window.FindAll(new ElementQuery(AutomationId: "nodeFruits")).Count > 0);
        var node = ById("nodeFruits");
        Assert.True(node.TryExpand());
        Assert.Equal(ExpandValue.Expanded, node.Expand);
        Assert.True(node.TryCollapse());
        Assert.Equal(ExpandValue.Collapsed, node.Expand);
    }

    [Fact]
    public void 視窗操作()
    {
        Assert.True(_window.TrySetWindowState(WindowStateKind.Normal));
        Assert.True(_window.TryResize(650, 520));
        WaitUntil(() => _window.Bounds.Width is > 600 and < 700);
        Assert.True(_window.TryMove(40, 40));
        Assert.False(ById("submitButton").TrySetWindowState(WindowStateKind.Maximized));
    }

    [Fact]
    public void 對話框是新的頂層視窗且可關閉()
    {
        ById("dialogButton").TryInvoke();
        IUiElement? dialog = null;
        WaitUntil(() => (dialog = _driver.GetTopLevelWindows(_app.ProcessId).FirstOrDefault(w => w.Name == "Confirm")) is not null
                        || (dialog = _window.FindAll(new ElementQuery(Name: "Confirm", Kind: ControlKind.Window)).FirstOrDefault()) is not null, 5000);
        Assert.True(dialog!.TryClose());
        WaitUntil(() => ById("statusText").Name == "Dialog cancelled", 5000);
    }

    // ───────────── 輸入與截圖 ─────────────

    [Fact]
    public void 滑鼠點擊與鍵盤輸入()
    {
        var name = ById("nameInput");
        _driver.Input.Click(name.ClickPoint(), MouseButtonKind.Left);
        _driver.Input.Type("Bob");
        _driver.Input.WaitUntilIdle();
        WaitUntil(() => name.Value == "Bob");

        _driver.Input.PressChord([0x11, 0x41]); // Ctrl+A
        _driver.Input.PressChord([0x2E]);       // Delete
        WaitUntil(() => name.Value == "");

        _driver.Input.Click(ById("submitButton").ClickPoint(), MouseButtonKind.Left);
        WaitUntil(() => ById("statusText").Name == "Submitted");
    }

    [Fact]
    public void 截圖()
    {
        var bounds = _window.Bounds;
        using var bmp = _driver.Screen.Capture(bounds);
        Assert.Equal(bounds.Width, bmp.Width);
        Assert.Equal(bounds.Height, bmp.Height);
        Assert.True(_driver.Screen.VirtualScreen.Contains(bounds.Center()));
    }

    [Fact]
    public void 程序生命週期()
    {
        Assert.False(_app.HasExited);
        Assert.True(_window.TryClose());
        Assert.True(_app.WaitForExit(TimeSpan.FromSeconds(5)));
        Assert.True(_app.HasExited);
        Assert.False(_window.IsAlive);
    }
}
