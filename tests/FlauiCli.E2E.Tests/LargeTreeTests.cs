using FlauiCli.Core.Abstractions;
using FlauiCli.Drivers;

namespace FlauiCli.E2E.Tests;

/// <summary>
/// WinFormsLargeTree has more than 512 UIA elements in front of its tool strip. UIA3 descendant searches and Subtree
/// cache requests stop after about 512 elements on it without an error, so these tests fail when a search or a
/// snapshot relies on a single UIA request.
/// </summary>
public sealed class LargeTreeTests : IDisposable
{
    private const int Months = 12;

    private readonly FlaUIDriver _driver = new();
    private readonly IAppProcess _app;
    private readonly IUiElement _window;

    public LargeTreeTests()
    {
        _app = _driver.Launch(TestEnvironment.WinFormsLargeTreeExe, Months.ToString(), null);
        _window = _app.GetMainWindow(TimeSpan.FromSeconds(15)) ?? throw new InvalidOperationException("WinFormsLargeTree did not open a window");
    }

    public void Dispose()
    {
        try { _app.Kill(); } catch { /* ignore */ }
        _app.Dispose();
        _driver.Dispose();
    }

    [Fact]
    public void FindAllReachesElementsAfterTheFirst512()
    {
        // Not in the part UIA returns: found by the level-by-level fallback
        Assert.Single(_window.FindAll(new ElementQuery(Kind: ControlKind.ToolBar)));
        Assert.Single(_window.FindAll(new ElementQuery(AutomationId: $"day{Months}_49")));
        Assert.Single(_window.FindAll(new ElementQuery(AutomationId: "toolStrip1", Kind: ControlKind.ToolBar)));
    }

    [Fact]
    public void FindFirstReachesElementsAfterTheFirst512()
    {
        Assert.NotNull(_window.FindFirst(new ElementQuery(AutomationId: "toolStrip1")));
        Assert.NotNull(_window.FindFirst(new ElementQuery(AutomationId: $"day{Months}_49")));
        Assert.Null(_window.FindFirst(new ElementQuery(AutomationId: "noSuchElement")));
    }

    [Fact]
    public void FullCaptureIsComplete()
    {
        var nodes = _window.CaptureTree().DescendantsAndSelf().ToList();
        Assert.Contains(nodes, n => n.AutomationId == "toolStrip1");
        Assert.Contains(nodes, n => n.AutomationId == $"day{Months}_49");
        Assert.Equal(Months * 49, nodes.Count(n => n.AutomationId.StartsWith("day")));
    }
}

/// <summary>The same window through the CLI: snapshot and a type selector must see the tool strip.</summary>
[Trait("Category", "E2E")]
public sealed class LargeTreeCliTests : IDisposable
{
    private readonly string _work = TestEnvironment.NewTempDir();
    private readonly CliSession _cli;

    public LargeTreeCliTests() => _cli = new CliSession(_work);

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

    [Fact]
    public void SnapshotAndSelectorsSeeTheToolStrip()
    {
        Ok("open", TestEnvironment.WinFormsLargeTreeExe);
        var snapshot = Ok("snapshot").StdOut;
        Assert.Contains("id=toolStrip1", snapshot);
        Assert.Contains("id=day12_49", snapshot);
        Assert.Contains("2026", Ok("get", "value", "type=toolbar >> type=combobox").StdOut);

        // A depth-limited snapshot expands below the first level (cached children must stay usable)
        var shallow = Ok("snapshot", "--depth", "3").StdOut;
        Assert.Contains("id=toolStrip1", shallow);
        Assert.Contains("id=month1", shallow);
        Ok("close");
    }
}
