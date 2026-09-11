using System.Diagnostics;
using FlaUI.Core.Definitions;
using FlauiCli.Core.Abstractions;
using FlauiCli.Drivers;

namespace FlauiCli.E2E.Tests;

/// <summary>
/// Contract tests for the FlaUI adapter: verify every behaviour of <see cref="IUiDriver"/> / <see cref="IUiElement"/>
/// against a real WPF application. They must all pass when FlaUI is upgraded; this is exactly what the Core unit
/// tests (which use FakeDriver) cannot cover.
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
        _window = _app.GetMainWindow(TimeSpan.FromSeconds(15)) ?? throw new InvalidOperationException("WpfSample did not open a window");
        _window.SetForeground();
    }

    public void Dispose()
    {
        try { _app.Kill(); } catch { /* ignore */ }
        _app.Dispose();
        _driver.Dispose();
    }

    private IUiElement ById(string id) =>
        _window.FindAll(new ElementQuery(AutomationId: id)).FirstOrDefault() ?? throw new InvalidOperationException($"{id} not found");

    private static void WaitUntil(Func<bool> condition, int timeoutMs = 3000)
    {
        var sw = Stopwatch.StartNew();
        while (!condition())
        {
            if (sw.ElapsedMilliseconds > timeoutMs) throw new TimeoutException("The condition did not become true in time");
            Thread.Sleep(50);
        }
    }

    // ───────────── Mapping checks (no UI needed) ─────────────

    [Fact]
    public void EnumMappingsAreComplete()
    {
        foreach (var t in Enum.GetValues<ControlType>())
            Assert.True(Enum.IsDefined(Mapping.ToKind(t)) && (t == ControlType.Unknown || Mapping.ToKind(t) != ControlKind.Unknown),
                $"FlaUI ControlType.{t} has no matching ControlKind");
        foreach (var k in Enum.GetValues<ControlKind>())
            Assert.Equal(k, Mapping.ToKind(Mapping.ToControlType(k)));
        foreach (var s in Enum.GetValues<ToggleState>())
            Assert.Equal(s.ToString(), Mapping.ToToggle(s).ToString());
        foreach (var s in Enum.GetValues<ExpandCollapseState>())
            Assert.Equal(s.ToString(), Mapping.ToExpand(s).ToString());
    }

    // ───────────── Properties ─────────────

    [Fact]
    public void BasicProperties()
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
        Assert.Null(_driver.GetTopLevelWindows().First().Parent?.Parent); // the parent of a top-level window is the desktop

        Assert.Equal(ControlKind.Window, _window.Kind);
        Assert.Equal("WpfSample", _window.Name);
        Assert.NotEqual(0, _window.WindowHandle);
    }

    [Fact]
    public void StateProperties()
    {
        Assert.False(ById("disabledButton").IsEnabled);
        Assert.True(ById("passwordInput").IsPassword);
        Assert.Equal(ToggleValue.Off, ById("agreeCheck").Toggle);
        Assert.Equal(ExpandValue.Collapsed, ById("colorCombo").Expand);
        Assert.Contains("Invoke", ById("submitButton").SupportedPatterns);
        // The localized control type depends on the OS UI language, so only check that it can be read
        Assert.False(string.IsNullOrEmpty(ById("submitButton").GetProperty("LocalizedControlType")));
        Assert.Null(ById("submitButton").GetProperty("NoSuchProperty"));
    }

    [Fact]
    public void SameElementIsEqual()
    {
        var a = ById("submitButton");
        var b = _window.FindAll(new ElementQuery(Name: "Submit", Kind: ControlKind.Button)).Single();
        Assert.True(a.Equals(b));
        Assert.False(a.Equals(ById("dialogButton")));
        Assert.Equal(a.RuntimeId, b.RuntimeId);
    }

    // ───────────── Searching ─────────────

    [Fact]
    public void QueryConditionsAndScopes()
    {
        Assert.Single(_window.FindAll(new ElementQuery(AutomationId: "submitButton", Kind: ControlKind.Button)));
        Assert.Empty(_window.FindAll(new ElementQuery(AutomationId: "submitButton", Kind: ControlKind.Edit)));
        Assert.Empty(_window.FindAll(new ElementQuery(AutomationId: "submitButton"), SearchScope.Children));
        Assert.True(_window.FindAll(ElementQuery.Any).Count > 10);
        Assert.Contains(_driver.GetTopLevelWindows(_app.ProcessId), w => w.Equals(_window));
    }

    [Fact]
    public void XPathRoundTrip()
    {
        var submit = ById("submitButton");
        var xpath = submit.GetXPathFrom(_window);
        Assert.False(string.IsNullOrEmpty(xpath));
        var found = _window.FindByXPath(xpath!);
        Assert.True(found.Any(e => e.Equals(submit)),
            $"xpath={xpath}, found {found.Count}: {string.Join(" | ", found)}; //Button matches {_window.FindByXPath("//Button").Count}");
        Assert.Single(_window.FindByXPath("//Button[@AutomationId='submitButton']"));
    }

    [Fact]
    public void TreeSnapshotContainsControlsAndStates()
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
    public void FromPointAndFocus()
    {
        var submit = ById("submitButton");
        // Hit testing returns the deepest element (the centre of a WPF button is its content text),
        // so both the button itself and its child are accepted
        var hit = _driver.FromPoint(submit.Bounds.Center());
        Assert.NotNull(hit);
        Assert.True(hit!.Equals(submit) || hit.Parent?.Equals(submit) == true, $"FromPoint returned {hit}");
        var name = ById("nameInput");
        name.Focus();
        WaitUntil(() => _driver.GetFocusedElement()?.Equals(name) == true);
        Assert.True(_driver.FromHandle(_window.WindowHandle)?.Equals(_window));
    }

    // ───────────── Pattern operations ─────────────

    [Fact]
    public void InvokeAndSetValue()
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
    public void ComboBoxSelection()
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
    public void ListItemSelection()
    {
        var item = ById("fruitList").FindAll(new ElementQuery(Name: "Banana")).First(e => e.Kind == ControlKind.ListItem);
        Assert.False(item.IsSelected);
        Assert.True(item.TrySelectItem());
        Assert.True(item.IsSelected);
    }

    [Fact]
    public void TabSelectionAndTreeExpansion()
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
    public void WindowOperations()
    {
        Assert.True(_window.TrySetWindowState(WindowStateKind.Normal));
        Assert.True(_window.TryResize(650, 520));
        WaitUntil(() => _window.Bounds.Width is > 600 and < 700);
        Assert.True(_window.TryMove(40, 40));
        Assert.False(ById("submitButton").TrySetWindowState(WindowStateKind.Maximized));
    }

    [Fact]
    public void DialogIsAWindowThatCanBeClosed()
    {
        ById("dialogButton").TryInvoke();
        IUiElement? dialog = null;
        WaitUntil(() => (dialog = _driver.GetTopLevelWindows(_app.ProcessId).FirstOrDefault(w => w.Name == "Confirm")) is not null
                        || (dialog = _window.FindAll(new ElementQuery(Name: "Confirm", Kind: ControlKind.Window)).FirstOrDefault()) is not null, 5000);
        Assert.True(dialog!.TryClose());
        WaitUntil(() => ById("statusText").Name == "Dialog cancelled", 5000);
    }

    // ───────────── Input and screenshots ─────────────

    [Fact]
    public void MouseClicksAndKeyboardInput()
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
    public void Screenshot()
    {
        var bounds = _window.Bounds;
        using var bmp = _driver.Screen.Capture(bounds);
        Assert.Equal(bounds.Width, bmp.Width);
        Assert.Equal(bounds.Height, bmp.Height);
        Assert.True(_driver.Screen.VirtualScreen.Contains(bounds.Center()));
    }

    [Fact]
    public void ProcessLifetime()
    {
        Assert.False(_app.HasExited);
        Assert.True(_window.TryClose());
        Assert.True(_app.WaitForExit(TimeSpan.FromSeconds(5)));
        Assert.True(_app.HasExited);
        Assert.False(_window.IsAlive);
    }
}
