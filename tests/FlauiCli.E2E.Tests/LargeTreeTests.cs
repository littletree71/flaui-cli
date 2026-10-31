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

/// <summary>The rows of a large grid through the CLI: every step of a lookup has to stay within --timeout.</summary>
[Trait("Category", "E2E")]
public sealed class LargeGridCliTests : IDisposable
{
    private readonly string _work = TestEnvironment.NewTempDir();
    private readonly CliSession _cli;

    public LargeGridCliTests() => _cli = new CliSession(_work);

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
    public void RowsFarDownTheGridAreReadWithinTheTimeout()
    {
        Ok("open", TestEnvironment.WinFormsLargeTreeExe, "grid", "7000");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        Assert.Contains("C5000", Ok("get", "value", "id=dataGridView1 >> type=dataitem", "--skip-rows", "5000", "--timeout", "5000").StdOut);
        // The first match is off-screen once the grid is scrolled: sorting and comparing all matches must not outlast the timeout
        Assert.Contains("C200", Ok("get", "value", "id=dataGridView1 >> type=dataitem", "--skip-rows", "200", "--timeout", "5000").StdOut);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10), $"took {sw.Elapsed}");

        var snapshot = Ok("snapshot", "--root", "id=dataGridView1", "--skip-rows", "5000", "--max-rows", "2").StdOut;
        Assert.Contains("5000 rows skipped", snapshot);
        Assert.Contains("dataitem", snapshot);

        sw.Restart();
        var noRow = _cli.Run("get", "value", "id=dataGridView1 >> id=noSuchElement", "--max-rows", "3", "--timeout", "2000");
        Assert.NotEqual(0, noRow.ExitCode);
        Assert.Contains("--max-rows", noRow.All);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(4), $"took {sw.Elapsed}");

        Ok("close");
    }
}

/// <summary>
/// WinFormsLargeTree in grid mode: a DataGridView with thousands of rows comes before a filter text box and a tool strip.
/// A UIA descendant search walks every row first, so these tests fail (or time out) when a lookup or a snapshot enters it.
/// </summary>
public sealed class LargeGridTests : IDisposable
{
    private const int Rows = 7000;

    private readonly FlaUIDriver _driver = new();
    private readonly IAppProcess _app;
    private readonly IUiElement _window;

    public LargeGridTests()
    {
        _app = _driver.Launch(TestEnvironment.WinFormsLargeTreeExe, $"grid {Rows}", null);
        _window = _app.GetMainWindow(TimeSpan.FromSeconds(15)) ?? throw new InvalidOperationException("WinFormsLargeTree did not open a window");
    }

    public void Dispose()
    {
        try { _app.Kill(); } catch { /* ignore */ }
        _app.Dispose();
        _driver.Dispose();
    }

    [Fact]
    public void ControlsAfterTheGridAreFoundWithoutReadingItsRows()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using (SearchBudget.Begin(5000))
        {
            Assert.NotNull(_window.FindFirst(new ElementQuery(AutomationId: "filterTextBox")));
            Assert.Single(_window.FindAll(new ElementQuery(Kind: ControlKind.ToolBar)));
            // ToolStrip items expose no AutomationId: the button is found by its name
            Assert.Single(_window.FindAll(new ElementQuery(Name: "Query")));
        }
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10), $"took {sw.Elapsed}");
    }

    [Fact]
    public void TableContentIsStillSearchedInsideTheGrid()
    {
        using (SearchBudget.Begin(10_000))
        {
            // Cells are data items: a query for table content searches the table
            Assert.NotNull(_window.FindFirst(new ElementQuery(Kind: ControlKind.DataItem)));
            Assert.Null(_window.FindFirst(new ElementQuery(AutomationId: "noSuchElement")));
        }
    }

    [Fact]
    public void ASelectorThatFindsNothingDoesNotEnterTheGrid()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using (SearchBudget.Begin(5000))
        {
            Assert.Null(_window.FindFirst(new ElementQuery(AutomationId: "noSuchElement")));
            Assert.Empty(_window.FindAll(new ElementQuery(AutomationId: "noSuchElement")));
            // The name of a column header: only found when the query is scoped to the table
            Assert.Null(_window.FindFirst(new ElementQuery(Name: "Code")));
            // A search inside the grid would keep the application busy, and this would wait behind it
            Assert.NotNull(_window.FindFirst(new ElementQuery(AutomationId: "filterTextBox")));
        }
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2), $"took {sw.Elapsed}");

        using (SearchBudget.Begin(5000))
        {
            var grid = _window.FindFirst(new ElementQuery(AutomationId: "dataGridView1"));
            Assert.NotNull(grid);
            Assert.NotNull(grid.FindFirst(new ElementQuery(Name: "Code")));
        }
    }

    [Fact]
    public void ASearchInsideTheGridStopsAtTheDeadlineAndLeavesTheApplicationFree()
    {
        var grid = _window.FindFirst(new ElementQuery(AutomationId: "dataGridView1"));
        Assert.NotNull(grid);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        using (SearchBudget.Begin(2000))
        {
            Assert.Null(grid.FindFirst(new ElementQuery(AutomationId: "noSuchElement")));
            Assert.Contains("time limit", SearchBudget.Incomplete);
        }
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"the search took {sw.Elapsed}");

        // One descendant search would keep the application busy for minutes, and this would wait behind it
        sw.Restart();
        using (SearchBudget.Begin(2000))
        {
            Assert.NotNull(_window.FindFirst(new ElementQuery(AutomationId: "filterTextBox")));
            Assert.Null(SearchBudget.Incomplete);
        }
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(1), $"the next search took {sw.Elapsed}");
    }

    [Fact]
    public void SkipRowsJumpsToARowFarDownTheGrid()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        ElementNode tree;
        using (SearchBudget.Begin(5000)) tree = _window.CaptureTree(maxRows: 5, skipRows: 5000);
        var grid = tree.DescendantsAndSelf().Single(n => n.AutomationId == "dataGridView1");
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(3), $"took {sw.Elapsed}");

        Assert.Equal(5000, grid.SkippedChildren);
        Assert.Equal(5, grid.Children.Count);
        Assert.Equal(1995, grid.OmittedChildren);
        var values = grid.DescendantsAndSelf().Select(n => n.Value).ToList();
        Assert.Contains("C5000", values);
        Assert.Contains("C5004", values);
        Assert.DoesNotContain("C4999", values);
        Assert.DoesNotContain("C5005", values);

        // Past the last row there is nothing to read
        var past = _window.CaptureTree(maxRows: 5, skipRows: 8000).DescendantsAndSelf().Single(n => n.AutomationId == "dataGridView1");
        Assert.Empty(past.Children);
    }

    [Fact]
    public void RowOptionsLimitASearchInsideTheGrid()
    {
        var grid = _window.FindFirst(new ElementQuery(AutomationId: "dataGridView1"));
        Assert.NotNull(grid);

        // --skip-rows: the first cell found is in the row skipped to, far beyond what can be walked in the time given
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using (SearchBudget.Begin(3000))
        {
            SearchBudget.LimitRows(5000, 3);
            var cell = grid.FindFirst(new ElementQuery(Kind: ControlKind.DataItem));
            Assert.NotNull(cell);
            Assert.Equal("C5000", cell.Value);
        }
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(3), $"took {sw.Elapsed}");

        // --max-rows: the search ends after those rows, not at the deadline, and says why it found nothing
        sw.Restart();
        using (SearchBudget.Begin(5000))
        {
            SearchBudget.LimitRows(0, 3);
            Assert.Null(grid.FindFirst(new ElementQuery(AutomationId: "noSuchElement")));
            Assert.Contains("--max-rows", SearchBudget.Incomplete);
        }
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2), $"took {sw.Elapsed}");
    }

    [Fact]
    public void CaptureReadsTheFirstRowsAndEverythingAfterTheGrid()
    {
        var tree = _window.CaptureTree(maxRows: 20);
        var nodes = tree.DescendantsAndSelf().ToList();
        Assert.Contains(nodes, n => n.AutomationId == "filterTextBox");
        Assert.Contains(nodes, n => n.AutomationId == "toolStrip1");
        var grid = nodes.Single(n => n.AutomationId == "dataGridView1");
        Assert.Equal(20, grid.Children.Count);
        Assert.NotEqual(0, grid.OmittedChildren);
    }
}
