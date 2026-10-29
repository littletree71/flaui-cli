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
    public void EveryDaemonCommandInTheCatalogHasAHandler()
    {
        foreach (var spec in CommandCatalog.All.Where(s => s.Location == CommandLocation.Daemon))
        {
            var r = _app.Dispatcher.Execute(new CommandCall(spec.Name) { Cwd = _app.Cwd });
            Assert.DoesNotContain("Unknown command", r.Error ?? "");
        }
    }

    [Fact]
    public void HintsWhenNoApplicationIsOpen()
    {
        var r = _app.Run("snapshot");
        Assert.False(r.Ok);
        Assert.Equal(ExitCodes.Error, r.ExitCode);
        Assert.Contains("open", r.Error);
    }

    [Fact]
    public void SnapshotAfterOpen()
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
    public void ClickByRefAndSelectorThenAssert()
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
    public void MissingElementTimesOut()
    {
        Ok(_app.Open());
        var r = _app.Run("click", ("target", "id=nope"), ("timeout", "200"));
        Assert.Equal(ExitCodes.Error, r.ExitCode);
        Assert.Contains("Element not found", r.Error);
    }

    [Fact]
    public void DisabledElementsCannotBeClicked()
    {
        Ok(_app.Open());
        var r = _app.Run("click", ("target", "id=disabledButton"), ("timeout", "200"));
        Assert.False(r.Ok);
        Assert.Contains("disabled", r.Error);
    }

    [Fact]
    public void FillPrefersValuePattern()
    {
        Ok(_app.Open());
        Ok(_app.Run("fill", ("target", "id=nameInput"), ("text", "Alice")));
        Assert.Equal("Alice", _app.NameBox.Value);
        Assert.Contains("setvalue:Alice", _app.NameBox.Actions);
        Ok(_app.Run("assert", ("kind", "value"), ("target", "id=nameInput"), ("expected", "Alice")));
        Assert.Equal("Alice", _app.Run("get", ("kind", "text"), ("target", "id=nameInput")).Text);
    }

    [Fact]
    public void FillCanForceTheKeyboard()
    {
        Ok(_app.Open());
        _app.Driver.Focused = _app.NameBox;
        _app.NameBox.Value = "old";
        Ok(_app.Run("fill", ("target", "id=nameInput"), ("text", "new"), ("keyboard", "true")));
        Assert.Contains("type new", _app.Driver.Input.Log);
        Assert.Equal("new", _app.NameBox.Value);
    }

    [Fact]
    public void CheckAndUncheck()
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
    public void SelectInComboBoxAndList()
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
    public void SelectSetsTheValueWhenTheComboBoxIgnoresTheSelection()
    {
        // WinForms ToolStripComboBox: SelectionItemPattern succeeds but the value does not change
        Ok(_app.Open());
        _app.Colors.Value = "Red";
        _app.Colors.SelectAppliesValue = false;

        Ok(_app.Run("select", ("target", "id=colorCombo"), ("item", "Green")));
        Assert.Equal("Green", _app.Colors.Value);
        Assert.Contains("setvalue:Green", _app.Colors.Actions);
    }

    [Fact]
    public void SelectFailsWhenTheSelectionDoesNotTakeEffect()
    {
        Ok(_app.Open());
        _app.Colors.Value = "Red";
        _app.Colors.IsReadOnly = true;
        _app.Colors.SelectAppliesValue = false;

        var r = _app.Run("select", ("target", "id=colorCombo"), ("item", "Green"));
        Assert.False(r.Ok);
        Assert.Contains("did not take effect", r.Error);
        Assert.Contains("fill", r.Error);
        Assert.Equal("Red", _app.Colors.Value);

        // Selecting the value it already has is not a failure
        Ok(_app.Run("select", ("target", "id=colorCombo"), ("item", "Red")));
    }

    [Fact]
    public void PressParsesKeys()
    {
        Ok(_app.Open());
        Ok(_app.Run("press", ("keys", "Ctrl+S")));
        Assert.Contains("press 0x11+0x53", _app.Driver.Input.Log);
        Assert.False(_app.Run("press", ("keys", "Ctrl+Nope")).Ok);
    }

    /// <summary>A table with a Name column (x 50..150) and an Amount column (x 150..250), two rows.</summary>
    private FakeElement AddGrid()
    {
        var grid = new FakeElement(ControlKind.DataGrid, "Grid", "grid") { Bounds = new System.Drawing.Rectangle(40, 480, 220, 100) };
        var header = new FakeElement(ControlKind.Group, "Top Row").Add(
            new FakeElement(ControlKind.Header, "Name") { Bounds = new System.Drawing.Rectangle(50, 480, 100, 20) },
            new FakeElement(ControlKind.Header, "Amount") { Bounds = new System.Drawing.Rectangle(150, 480, 100, 20) });
        grid.Add(header);
        for (var row = 0; row < 2; row++)
        {
            var y = 500 + row * 20;
            grid.Add(new FakeElement(ControlKind.Group, $"Row {row}").Add(
                new FakeElement(ControlKind.DataItem, $"Name Row {row}") { Bounds = new System.Drawing.Rectangle(50, y, 100, 20) },
                new FakeElement(ControlKind.DataItem, $"Amount Row {row}") { Bounds = new System.Drawing.Rectangle(150, y, 100, 20) }));
        }
        _app.Window.Add(grid);
        return grid;
    }

    private static bool Masked(System.Drawing.Bitmap bmp, int x, int y) => bmp.GetPixel(x, y).ToArgb() == System.Drawing.Color.FromArgb(96, 96, 96).ToArgb();

    [Fact]
    public void ScreenshotMasksElementsAndColumns()
    {
        Ok(_app.Open());
        AddGrid();
        var path = Path.Combine(_app.Cwd, "masked.png");
        Ok(_app.Run("screenshot", ("filename", path), ("mask", "id=nameInput"), ("mask-column", "id=grid >> name=Name")));

        using var bmp = new System.Drawing.Bitmap(path);
        Assert.True(Masked(bmp, 100, 215), "the name input is not masked");
        Assert.True(Masked(bmp, 100, 505) && Masked(bmp, 100, 525), "the Name cells are not masked");
        Assert.False(Masked(bmp, 100, 485), "the Name header was masked");
        Assert.False(Masked(bmp, 200, 505), "the Amount cells were masked");
    }

    [Fact]
    public void ScreenshotFailsWithoutSavingWhenAMaskIsMissing()
    {
        Ok(_app.Open());
        var path = Path.Combine(_app.Cwd, "leak.png");
        var r = _app.Run("screenshot", ("filename", path), ("mask", "id=nope"), ("timeout", "200"));
        Assert.False(r.Ok);
        Assert.False(File.Exists(path), "a screenshot was saved without its mask");
        Assert.Contains("not inside a table", _app.Run("screenshot", ("mask-column", "id=num1Button")).Error);
    }

    [Fact]
    public void DocumentMasksApplyToEveryStepWhereTheyAreShown()
    {
        Ok(_app.Open());
        AddGrid();
        Ok(_app.Run("doc", ("action", "start"), ("mask-column", "id=grid >> name=Name"), ("mask", "id=gone")));
        Ok(_app.Run("click", ("target", "id=num1Button")));
        Ok(_app.Run("doc", ("action", "step"), ("text", "Done")));
        var outDir = Path.Combine(_app.Cwd, "doc");
        Ok(_app.Run("doc", ("action", "stop"), ("out", outDir), ("format", "md")));

        foreach (var image in Directory.GetFiles(Path.Combine(outDir, "images"), "*.png"))
        {
            using var bmp = new System.Drawing.Bitmap(image);
            Assert.True(Masked(bmp, 100, 505), $"{Path.GetFileName(image)} is not masked");
        }
    }

    [Fact]
    public void SelectWithTheKeyboardPressesArrowsFromTheCurrentItem()
    {
        Ok(_app.Open());
        var colors = _app.Colors;
        colors.Add(new FakeElement(ControlKind.ListItem, "Blue"));
        colors.Value = "Blue";
        string[] names = ["Red", "Green", "Blue"];
        // The combo box moves one item per arrow key, like a real one
        _app.Driver.Input.OnPress = () =>
        {
            var i = Array.IndexOf(names, colors.Value);
            colors.Value = names[Math.Clamp(i + (_app.Driver.Input.Log[^1] == "press 0x28" ? 1 : -1), 0, names.Length - 1)];
        };

        var r = _app.Run("select", ("target", "id=colorCombo"), ("item", "Red"), ("keyboard", "true"));
        Ok(r);
        Assert.Equal("Red", colors.Value);
        Assert.Contains("with the keyboard (Up × 2)", r.Text);
        Assert.Equal(2, _app.Driver.Input.Log.Count(l => l == "press 0x26"));
        Assert.DoesNotContain(colors.Actions, a => a.StartsWith("combo:"));

        Ok(_app.Run("select", ("target", "id=colorCombo"), ("item", "#1"), ("keyboard", "true")));
        Assert.Equal("Green", colors.Value);

        Assert.Contains("already selected", _app.Run("select", ("target", "id=colorCombo"), ("item", "Green"), ("keyboard", "true")).Text);
        Assert.Contains("Available items: Red, Green, Blue",
            _app.Run("select", ("target", "id=colorCombo"), ("item", "Purple"), ("keyboard", "true")).Error);
        Assert.Contains("works on combo boxes and lists",
            _app.Run("select", ("target", "id=num1Button"), ("item", "x"), ("keyboard", "true")).Error);
    }

    [Fact]
    public void SelectWithTheKeyboardFailsWhenTheSelectionDoesNotFollow()
    {
        Ok(_app.Open());
        _app.Colors.Value = "Red";
        var r = _app.Run("select", ("target", "id=colorCombo"), ("item", "Green"), ("keyboard", "true"));
        Assert.False(r.Ok);
        Assert.Contains("but \"Red\" is selected instead of \"Green\"", r.Error);
    }

    [Fact]
    public void PressRepeatsAndFocusesTheTargetBeforeEveryPress()
    {
        Ok(_app.Open());
        // The application moves the focus to the next field after every key, like a WinForms SelectedIndexChanged handler
        _app.Driver.Input.OnPress = () => _app.Driver.Focused = _app.NameBox;
        var r = _app.Run("press", ("keys", "Down"), ("target", "id=colorCombo"), ("repeat", "3"));
        Ok(r);
        Assert.Equal(3, _app.Driver.Input.Log.Count(l => l == "press 0x28"));
        Assert.Equal(3, _app.Colors.Actions.Count(a => a == "focus"));
        Assert.Contains("Pressed Down 3 times", r.Text);
        Assert.DoesNotContain("focus moved", r.Text);

        Assert.Contains("between 1 and 100", _app.Run("press", ("keys", "Down"), ("repeat", "0")).Error);
    }

    [Fact]
    public void PressSaysWhenTheApplicationMovedTheFocus()
    {
        Ok(_app.Open());
        _app.Driver.Focused = _app.Colors;
        _app.Driver.Input.OnPress = () => _app.Driver.Focused = _app.NameBox;
        var r = _app.Run("press", ("keys", "Down"));
        Ok(r);
        Assert.Contains("The focus moved from combobox \"Color\"", r.Text);
        Assert.Contains("--target", r.Text);

        // Without a focus change there is no note
        _app.Driver.Input.OnPress = null;
        Assert.DoesNotContain("focus moved", _app.Run("press", ("keys", "Down")).Text);
    }

    [Fact]
    public void SimulatedInputFailsClearlyWhileTheWorkstationIsLocked()
    {
        Ok(_app.Open());
        _app.Driver.Locked = "the workstation is locked";

        var click = _app.Run("click", ("target", "id=num1Button"));
        Assert.False(click.Ok);
        Assert.Equal(ExitCodes.DesktopUnavailable, click.ExitCode);
        Assert.Contains("input cannot be simulated: the workstation is locked", click.Error);
        Assert.DoesNotContain(_app.Driver.Input.Log, l => l.StartsWith("click"));

        Assert.Equal(ExitCodes.DesktopUnavailable, _app.Run("press", ("keys", "Enter")).ExitCode);
        Assert.Equal(ExitCodes.DesktopUnavailable, _app.Run("screenshot").ExitCode);

        // UI Automation keeps working: reading, invoking and the status (which tells why input is unavailable)
        Ok(_app.Run("get", ("kind", "text"), ("target", "id=CalculatorResults")));
        Ok(_app.Run("click", ("target", "id=num1Button"), ("invoke", "true")));
        var status = _app.Run("status");
        Assert.Contains("- Input: unavailable (the workstation is locked)", status.Text);

        _app.Driver.Locked = null;
        Ok(_app.Run("click", ("target", "id=num1Button")));
        Assert.Contains("- Input: available", _app.Run("status").Text);
    }

    [Fact]
    public async Task CommandsWaitForTheUnlockWithAnUnlockTimeout()
    {
        Ok(_app.Open());
        Directory.CreateDirectory(Path.Combine(_app.Cwd, ".flaui-cli"));
        File.WriteAllText(Path.Combine(_app.Cwd, ".flaui-cli", "config.json"), """{ "timeouts": { "unlock": 5000 } }""");
        _app.Driver.Locked = "the workstation is locked";
        // The user unlocks while the click waits
        var unlock = Task.Run(async () => { await Task.Delay(300); _app.Driver.Locked = null; });

        var sw = System.Diagnostics.Stopwatch.StartNew();
        Ok(_app.Run("click", ("target", "id=num1Button")));
        Assert.True(sw.ElapsedMilliseconds >= 250, $"the click did not wait ({sw.ElapsedMilliseconds}ms)");
        Assert.Contains(_app.Driver.Input.Log, l => l.StartsWith("click"));
        await unlock;

        // Still locked when the wait ends: exit code 4
        File.WriteAllText(Path.Combine(_app.Cwd, ".flaui-cli", "config.json"), """{ "timeouts": { "unlock": 300 } }""");
        _app.Driver.Locked = "the workstation is locked";
        Assert.Equal(ExitCodes.DesktopUnavailable, _app.Run("click", ("target", "id=num1Button")).ExitCode);
    }

    [Fact]
    public void UnexpectedFailuresWhileLockedAreExplained()
    {
        Ok(_app.Open());
        // SendInput fails before the desktop check would have caught it (the lock happened in between)
        _app.Driver.Input.OnPress = () =>
        {
            _app.Driver.Locked = "the workstation is locked";
            throw new System.ComponentModel.Win32Exception(5);
        };
        var r = _app.Run("press", ("keys", "Enter"));
        Assert.Equal(ExitCodes.DesktopUnavailable, r.ExitCode);
        Assert.Contains("the workstation is locked", r.Error);
        Assert.Contains("Win32Exception", r.Error);
    }

    [Fact]
    public void LogLevelsDecideWhichSectionsArePrinted()
    {
        Ok(_app.Open());
        _app.Dispatcher.AutoSnapshot = true;
        // Two touching buttons outlined together: a warning about overlapping outlines
        (string, string)[] shot = [("highlight", "id=num1Button" + CommandCall.MultiValueSeparator + "id=num2Button"), ("highlight-padding", "20")];

        var info = _app.Run("click", ("target", "id=num1Button"));
        Assert.Contains("### Result", info.Text);
        Assert.Contains("### Snapshot", info.Text);
        Assert.DoesNotContain("### Timing", info.Text);

        var error = _app.Run("click", ("target", "id=num1Button"), (CommandCall.LogLevelKey, "error"));
        Assert.Contains("### Result", error.Text);
        Assert.DoesNotContain("### Snapshot", error.Text);

        var quiet = _app.Run("screenshot", [.. shot, (CommandCall.LogLevelKey, "error")]);
        Assert.DoesNotContain("### Warnings", quiet.Text);
        Assert.Contains("overlap", quiet.Data!["warnings"]);
        Assert.Contains("### Warnings", _app.Run("screenshot", [.. shot, (CommandCall.LogLevelKey, "warn")]).Text);

        var debug = _app.Run("click", ("target", "id=num1Button"), (CommandCall.LogLevelKey, "debug"));
        Assert.Contains("### Timing", debug.Text);
        Assert.Contains("### Snapshot", debug.Text);

        Assert.Contains("Unknown log level", _app.Run("status", (CommandCall.LogLevelKey, "loud")).Error);
    }

    [Fact]
    public void LogLevelComesFromTheConfigFileUnlessGiven()
    {
        Ok(_app.Open());
        _app.Dispatcher.AutoSnapshot = true;
        Directory.CreateDirectory(Path.Combine(_app.Cwd, ".flaui-cli"));
        File.WriteAllText(Path.Combine(_app.Cwd, ".flaui-cli", "config.json"), """{ "logLevel": "error" }""");

        Assert.DoesNotContain("### Snapshot", _app.Run("click", ("target", "id=num1Button")).Text);
        Assert.Contains("### Snapshot", _app.Run("click", ("target", "id=num1Button"), (CommandCall.LogLevelKey, "info")).Text);
        Assert.Contains("### Timing", _app.Run("click", ("target", "id=num1Button"), (CommandCall.VerboseKey, "true")).Text);
    }

    [Fact]
    public void YamlStyleAssertions()
    {
        Ok(_app.Open());
        Ok(_app.Run("assert", ("target", "id=CalculatorResults"), ("contains", "is 0")));
        Ok(_app.Run("assert", ("target", "id=nope"), ("exists", "false")));
        Ok(_app.Run("assert", ("target", "id=disabledButton"), ("enabled", "false")));
        Assert.Equal(ExitCodes.AssertionFailed,
            _app.Run("assert", ("target", "id=CalculatorResults"), ("matches", "^Display is [1-9]$"), ("timeout", "100")).ExitCode);
    }

    [Fact]
    public void WaitStates()
    {
        Ok(_app.Open());
        Ok(_app.Run("wait", ("target", "id=num1Button")));
        Ok(_app.Run("wait", ("target", "id=nope"), ("state", "gone")));
        var r = _app.Run("wait", ("target", "id=disabledButton"), ("state", "enabled"), ("timeout", "200"));
        Assert.Contains("Timed out", r.Error);
    }

    [Fact]
    public void OpenDoesNotMistakeAnExistingWindowWithASimilarTitleForTheApp()
    {
        // An editor that was already open shows a file whose name starts with the window title being waited for
        var editor = new FakeElement(ControlKind.Window, "calculator.flow.yaml - flaui-cli - Visual Studio Code")
            { ProcessId = 900, WindowHandle = 900, Bounds = new(0, 0, 800, 600) };
        _app.Driver.TopLevel.Add(editor);
        // The application shows its window only after a while (like a UWP app)
        _app.Window.Alive = false;
        using var shown = new Timer(_ => _app.Window.Alive = true, null, 700, Timeout.Infinite);

        Ok(_app.Open());
        Assert.Same(_app.Window, _app.Session.CurrentWindow);
        Ok(_app.Run("close"));
        Assert.DoesNotContain("close", editor.Actions);
    }

    [Fact]
    public void OpenStillAcceptsAnAlreadyOpenWindowWithTheExactTitle()
    {
        // A single-instance app (UWP Calculator) reuses the window that was already open
        _app.Driver.TopLevel.Add(_app.Window);
        Ok(_app.Open());
        Assert.Same(_app.Window, _app.Session.CurrentWindow);
    }

    [Fact]
    public void VerboseEndsTheResultWithATimingBreakdown()
    {
        Ok(_app.Open());
        var r = _app.Run("click", ("target", "id=num1Button"), ("verbose", "true"));
        Ok(r);
        Assert.Matches(@"### Timing\r?\n- find: \d+\.\d\d s\r?\n- action: \d+\.\d\d s\r?\n- total: \d+\.\d\d s$", r.Text);
        Assert.True(r.Data?.ContainsKey("timing.findMs"));

        // Without --verbose there is no breakdown
        Assert.DoesNotContain("### Timing", _app.Run("click", ("target", "id=num1Button")).Text);

        // A lookup that times out reports where the time went
        var miss = _app.Run("click", ("target", "id=noSuchButton"), ("timeout", "200"), ("verbose", "true"));
        Assert.False(miss.Ok);
        Assert.Contains("- find: 0.", miss.Text);
    }

    [Fact]
    public void VerboseIsNotRecorded()
    {
        Ok(_app.Open());
        Ok(_app.Run("record", ("action", "start")));
        Ok(_app.Run("click", ("target", "id=num1Button"), ("verbose", "true")));
        Ok(_app.Run("record", ("action", "stop"), ("out", "verbose.yaml")));
        var step = Assert.Single(ScriptYaml.Load(Path.Combine(_app.Cwd, "verbose.yaml")).Steps);
        Assert.Null(step.Get("verbose"));
    }

    [Fact]
    public void RecordingReplacesRefsWithStableSelectors()
    {
        Ok(_app.Open());
        Ok(_app.Run("record", ("action", "start"), ("name", "demo")));
        _app.Run("snapshot");
        var oneRef = _app.Session.Refs.GetOrAssign(_app.One);

        Ok(_app.Run("click", ("target", oneRef)));
        Ok(_app.Run("fill", ("target", "id=nameInput"), ("text", "Bob"), ("note", "Enter the name")));
        Ok(_app.Run("click", ("target", "name=Dup&&nth=1")));

        var stop = _app.Run("record", ("action", "stop"), ("out", "rec.yaml"));
        Ok(stop);
        var doc = ScriptYaml.Load(Path.Combine(_app.Cwd, "rec.yaml"));
        Assert.Equal("demo", doc.Name);
        Assert.Equal("calc.exe", doc.App?.Launch);
        Assert.Equal("Calculator", doc.App?.Window);
        Assert.Equal(3, doc.Steps.Count);
        Assert.Equal("id=num1Button", doc.Steps[0].Get("target"));
        Assert.Equal("Enter the name", doc.Steps[1].Get("note"));
        Assert.Equal("name=Dup&&nth=1", doc.Steps[2].Get("target"));
    }

    [Fact]
    public void PasswordsAreMaskedInRecordingsAndDocuments()
    {
        _app.NameBox.IsPassword = true;
        Ok(_app.Open());
        Ok(_app.Run("record", ("action", "start"), ("name", "login")));
        Ok(_app.Run("doc", ("action", "start"), ("title", "Login")));

        var r = _app.Run("fill", ("target", "id=nameInput"), ("text", "S3cret!"));
        Ok(r);
        Assert.Equal("S3cret!", _app.NameBox.Value); // the application still receives the real value
        Assert.DoesNotContain("S3cret!", r.Text);

        Ok(_app.Run("record", ("action", "stop"), ("out", "login.yaml")));
        Ok(_app.Run("doc", ("action", "stop"), ("out", "login")));
        var yaml = File.ReadAllText(Path.Combine(_app.Cwd, "login.yaml"));
        var md = File.ReadAllText(Path.Combine(_app.Cwd, "login", "index.md"));
        var html = File.ReadAllText(Path.Combine(_app.Cwd, "login", "index.html"));
        Assert.DoesNotContain("S3cret!", yaml);
        Assert.Contains(CommandCall.Masked, yaml);
        Assert.DoesNotContain("S3cret!", md);
        Assert.DoesNotContain("S3cret!", html);
        Assert.Contains("Enter the password", md);
    }

    [Fact]
    public void ReadOnlyCommandsAreNotRecorded()
    {
        Ok(_app.Open());
        Ok(_app.Run("record", ("action", "start")));
        _app.Run("snapshot");
        _app.Run("get", ("kind", "name"), ("target", "id=num1Button"));
        _app.Run("find", ("text", "One"));
        Assert.Equal(0, _app.Session.Recorder!.Count);
    }

    [Fact]
    public void DocumentModeCapturesStepsAndExportsMarkdownAndHtml()
    {
        Ok(_app.Open());
        Ok(_app.Run("doc", ("action", "start"), ("title", "Calculator tutorial")));
        Ok(_app.Run("click", ("target", "id=num1Button")));
        Ok(_app.Run("click", ("target", "id=plusButton"), ("note", "Press plus")));
        Ok(_app.Run("doc", ("action", "step"), ("text", "Done")));
        var r = _app.Run("doc", ("action", "stop"), ("out", "manual"));
        Ok(r);

        var dir = Path.Combine(_app.Cwd, "manual");
        var md = File.ReadAllText(Path.Combine(dir, "index.md"));
        Assert.Contains("# Calculator tutorial", md);
        Assert.Contains("## Step 1: Click the \"One\" button", md);
        Assert.Contains("## Step 2: Press plus", md);
        Assert.Contains("## Step 3: Done", md);
        Assert.Equal(3, Directory.GetFiles(Path.Combine(dir, "images"), "*.png").Length);
        Assert.Contains("data:image/png;base64,", File.ReadAllText(Path.Combine(dir, "index.html")));
    }

    [Fact]
    public void CloseEndsTheSessionAndClosesTheWindow()
    {
        Ok(_app.Open());
        var r = _app.Run("close");
        Ok(r);
        Assert.Equal("true", r.Data?["shutdown"]);
        Assert.Contains("close", _app.Window.Actions);
        Assert.Null(_app.Session.CurrentWindow);
    }

    [Fact]
    public void WindowsAndWindowState()
    {
        Ok(_app.Open());
        Assert.Contains("[0] window \"Calculator\"", _app.Run("windows").Text);
        Ok(_app.Run("maximize"));
        Assert.Contains("state:Maximized", _app.Window.Actions);
        Ok(_app.Run("resize", ("width", "300"), ("height", "200")));
        Assert.Equal(300, _app.Window.Bounds.Width);
    }

    [Fact]
    public void FindSearchesText()
    {
        Ok(_app.Open());
        var r = _app.Run("find", ("text", "display"));
        Ok(r);
        Assert.Contains("Found 1 element(s)", r.Text);
        Assert.Contains("CalculatorResults", r.Text);
        Assert.False(_app.Run("find", ("text", "("), ("regex", "true")).Ok);
    }

    [Fact]
    public void ScreenshotWritesAFile()
    {
        Ok(_app.Open());
        var r = _app.Run("screenshot", ("filename", "shot.png"), ("highlight", "id=num1Button\nid=num2Button"));
        Ok(r);
        Assert.True(File.Exists(Path.Combine(_app.Cwd, "shot.png")));
    }

    [Fact]
    public void OtherWindowsOfASharedUwpHostAreExcluded()
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
    public void OtherTopLevelWindowsOfANormalProcessArePopups()
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
    public void StaleWindowIsRecoveredByTitle()
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
    public void OpenPrefersANewWindowWithTheSameTitle()
    {
        var old = new FakeElement(ControlKind.Window, "Calculator") { ProcessId = _app.Window.ProcessId, Bounds = new(600, 0, 100, 100), WindowHandle = 7 };
        _app.Driver.TopLevel.Add(old);
        Ok(_app.Open());
        Assert.Same(_app.Window, _app.Session.CurrentWindow);
    }

    [Fact]
    public void OwnedDialogsAreFoundByWaitWindow()
    {
        Ok(_app.Open());
        var dialog = new FakeElement(ControlKind.Window, "Confirm") { Bounds = new(50, 50, 200, 100) };
        _app.Window.Add(dialog);

        Assert.Contains("\"Confirm\"", _app.Run("windows").Text);
        Ok(_app.Run("wait-window", ("title", "Confirm")));
        Assert.Same(dialog, _app.Session.CurrentWindow);
    }

    [Fact]
    public void DialogsOwnedByAnotherTopLevelWindowAreFound()
    {
        // WinForms MDI: the current window is an MDI child, the dialog it opens is owned by the MDI parent
        var parent = new FakeElement(ControlKind.Window, "Main") { ProcessId = _app.Window.ProcessId, Bounds = new(0, 0, 800, 600), WindowHandle = 8 };
        _app.Driver.TopLevel.Add(parent);
        Ok(_app.Open());
        var dialog = new FakeElement(ControlKind.Window, "Add") { Bounds = new(50, 50, 200, 100) };
        parent.Add(dialog);

        Assert.Contains("\"Add\"", _app.Run("windows").Text);
        Ok(_app.Run("window", ("window", "Add")));
        Assert.Same(dialog, _app.Session.CurrentWindow);
    }

    /// <summary>WinForms MDI: the parent's title contains the active child's title.</summary>
    private (FakeElement Parent, FakeElement Child) Mdi()
    {
        var parent = new FakeElement(ControlKind.Window, "MainApp - [Module-Function(1.0.0.4)]")
            { ProcessId = _app.Window.ProcessId, Bounds = new(0, 0, 800, 600), WindowHandle = 8 };
        var child = new FakeElement(ControlKind.Window, "Module-Function(1.0.0.4)") { Bounds = new(10, 40, 600, 400) };
        child.Add(new FakeElement(ControlKind.Button, "Query", "btnQuery") { Bounds = new(20, 50, 80, 30) });
        parent.Add(new FakeElement(ControlKind.MenuBar, "Menu"), child);
        _app.Driver.TopLevel.Add(parent);
        return (parent, child);
    }

    [Fact]
    public void WaitWindowPrefersATitleThatStartsWithTheTextOverOneThatContainsIt()
    {
        var (_, child) = Mdi();
        Ok(_app.Open());
        Ok(_app.Run("wait-window", ("title", "Module-Function(")));
        Assert.Same(child, _app.Session.CurrentWindow);
    }

    [Fact]
    public void WaitWindowFindsTheMdiChildThatIsAlreadyCurrent()
    {
        // The child is neither a top-level window nor one of the owned windows, which leave out the current window
        var (_, child) = Mdi();
        Ok(_app.Open());
        Ok(_app.Run("window", ("window", "Module-Function(1.0.0.4)")));

        Ok(_app.Run("wait-window", ("title", "Module-Function(")));
        Assert.Same(child, _app.Session.CurrentWindow);
        Ok(_app.Run("wait-window", ("title", "^Module-Function"), ("regex", "true"), ("timeout", "500")));
        Assert.Same(child, _app.Session.CurrentWindow);
    }

    [Fact]
    public void WaitWindowExactAndRegex()
    {
        var (parent, child) = Mdi();
        Ok(_app.Open());

        var r = _app.Run("wait-window", ("title", "Module-Function("), ("exact", "true"), ("timeout", "200"));
        Assert.False(r.Ok);
        Assert.Contains("exactly", r.Error);

        Ok(_app.Run("wait-window", ("title", @"^Module-Function\s*\("), ("regex", "true")));
        Assert.Same(child, _app.Session.CurrentWindow);
        Ok(_app.Run("wait-window", ("title", "^mainapp"), ("regex", "true")));
        Assert.Same(parent, _app.Session.CurrentWindow);

        Assert.False(_app.Run("wait-window", ("title", "x"), ("regex", "true"), ("exact", "true")).Ok);
        Assert.Contains("Invalid regular expression", _app.Run("wait-window", ("title", "("), ("regex", "true")).Error);
    }

    [Fact]
    public void SnapshotOfAnMdiChildDoesNotWalkItTwice()
    {
        var (parent, child) = Mdi();
        Ok(_app.Open());
        Ok(_app.Run("window", ("window", "Module-Function(1.0.0.4)")));
        Assert.Same(child, _app.Session.CurrentWindow);

        var r = _app.Run("snapshot");
        Ok(r);
        Assert.Single(r.Text.Split('\n'), l => l.Contains("\"Query\""));
        Assert.Contains("(current window, shown above)", r.Text);
        Assert.Contains("menubar \"Menu\"", r.Text);
        Assert.StartsWith("- window \"Module-Function(1.0.0.4)\"", r.Text.Split("```yaml\n")[1]);
        Assert.Equal(0, child.CaptureTreeCalls);
        Assert.Equal(1, parent.CaptureTreeCalls);
    }

    [Fact]
    public void SnapshotDepthLimitsTheCapture()
    {
        Ok(_app.Open());
        Ok(_app.Run("snapshot", ("depth", "2")));
        Assert.Equal(2, _app.Window.LastCaptureDepth?.MaxDepth);
        Ok(_app.Run("snapshot"));
        Assert.Null(_app.Window.LastCaptureDepth);
    }

    [Fact]
    public void SnapshotHintsWhenTheWindowIsMinimizedAndEmpty()
    {
        Ok(_app.Open());
        Assert.DoesNotContain("minimized", _app.Run("snapshot").Text);

        // Minimized with its content still listed (WinForms): no hint that would contradict the listing
        _app.Window.Bounds = new(-32000, -32000, 160, 28);
        Assert.DoesNotContain("minimized", _app.Run("snapshot").Text);

        // Minimized and empty (UWP)
        _app.Window.Children.Clear();
        Assert.Contains("The window is minimized", _app.Run("snapshot").Text);
    }

    [Fact]
    public void CloseWindowClosesOneDialogAndKeepsTheSession()
    {
        Ok(_app.Open());
        var dialog = new FakeElement(ControlKind.Window, "Confirm") { Bounds = new(50, 50, 200, 100) };
        _app.Window.Add(dialog);

        Ok(_app.Run("close-window", ("window", "Confirm")));
        Assert.Contains("close", dialog.Actions);
        Assert.False(dialog.IsAlive);
        Assert.True(_app.Window.IsAlive);
        Assert.Same(_app.Window, _app.Session.CurrentWindow);
        Ok(_app.Run("click", ("target", "id=num1Button")));
    }

    [Fact]
    public void ClosingTheCurrentWindowSwitchesToAnotherWindow()
    {
        var (parent, _) = Mdi();
        Ok(_app.Open());
        var dialog = new FakeElement(ControlKind.Window, "Add") { Bounds = new(50, 50, 200, 100) };
        parent.Add(dialog);
        Ok(_app.Run("window", ("window", "Add")));

        var r = _app.Run("close-window");
        Ok(r);
        Assert.False(dialog.IsAlive);
        Assert.True(_app.Session.CurrentWindow is { IsAlive: true });
        Assert.NotSame(dialog, _app.Session.CurrentWindow);
    }

    [Fact]
    public void ClosingADialogReturnsToTheWindowItWasOpenedFrom()
    {
        var (parent, child) = Mdi();
        Ok(_app.Open());
        Ok(_app.Run("window", ("window", child.Name)));
        // The dialog of an MDI child is owned by the MDI parent
        var dialog = new FakeElement(ControlKind.Window, "Add") { Bounds = new(50, 50, 200, 100) };
        parent.Add(dialog);
        Ok(_app.Run("window", ("window", "Add")));

        Ok(_app.Run("close-window"));
        Assert.False(dialog.IsAlive);
        Assert.Same(child, _app.Session.CurrentWindow);
    }

    [Fact]
    public void WindowsDoesNotListTooltipsOrUnnamedPopups()
    {
        Ok(_app.Open());
        var pid = _app.Window.ProcessId;
        _app.Driver.TopLevel.Add(new FakeElement(ControlKind.ToolTip, "Adds two numbers") { ProcessId = pid, WindowHandle = 101 });
        _app.Driver.TopLevel.Add(new FakeElement(ControlKind.Pane, "") { ProcessId = pid, WindowHandle = 102 });
        _app.Driver.TopLevel.Add(new FakeElement(ControlKind.Window, "Settings") { ProcessId = pid, WindowHandle = 103 });

        var text = _app.Run("windows").Text;
        Assert.DoesNotContain("Adds two numbers", text);
        Assert.Contains("Settings", text);
        Assert.Equal(2, text.Split('\n').Count(l => l.StartsWith("- [")));
    }

    [Fact]
    public void InvokeThatDoesNotReturnFailsAfterTheTimeoutAndLaterCommandsFailFast()
    {
        Ok(_app.Open());
        using var release = new ManualResetEventSlim();
        // Simulates InvokePattern on a WinForms button whose handler calls ShowDialog()
        _app.One.OnClick = () => release.Wait(TimeSpan.FromSeconds(30));
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var r = _app.Run("click", ("target", "id=num1Button"), ("invoke", "true"), ("timeout", "300"));
            sw.Stop();
            Assert.False(r.Ok);
            Assert.Equal(ExitCodes.Error, r.ExitCode);
            Assert.Contains("did not return", r.Error);
            Assert.True(sw.ElapsedMilliseconds < 10_000, $"click --invoke took {sw.ElapsedMilliseconds}ms");

            // status answers without UI Automation; other commands fail fast instead of waiting for the dialog
            var status = _app.Run("status");
            Ok(status);
            Assert.Contains("has not returned", status.Text);
            var snapshot = _app.Run("snapshot");
            Assert.False(snapshot.Ok);
            Assert.Contains("has not returned yet", snapshot.Error);

            // Keyboard input still reaches the dialog
            Ok(_app.Run("press", ("keys", "Escape")));
            Assert.Contains("press 0x1B", _app.Driver.Input.Log);
        }
        finally
        {
            release.Set();
        }

        // Once the invoke has returned, commands work again
        Assert.True(DispatcherPoll(() => _app.Run("snapshot").Ok));
    }

    private static bool DispatcherPoll(Func<bool> condition)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 5000)
        {
            if (condition()) return true;
            Thread.Sleep(50);
        }
        return false;
    }

    [Fact]
    public void DocumentModeCapturesValueChangesAfterTheAction()
    {
        Ok(_app.Open());
        Ok(_app.Run("doc", ("action", "start")));
        string? valueWhenCaptured = null;
        ((FakeScreen)_app.Driver.Screen).OnCapture = () => valueWhenCaptured = _app.NameBox.Value;
        Ok(_app.Run("fill", ("target", "id=nameInput"), ("text", "Alice")));
        Assert.Equal("Alice", valueWhenCaptured);
        Assert.Single(_app.Session.Doc!.Steps);
    }

    [Fact]
    public void WindowElementsInsideASharedHostAreNotDialogs()
    {
        _app.Window.ClassName = "ApplicationFrameWindow";
        _app.Window.Add(new FakeElement(ControlKind.Window, "Calculator", "TitleBar"));
        Ok(_app.Open());
        Assert.DoesNotContain("TitleBar", _app.Run("windows").Text);
    }

    [Fact]
    public void StaleRefIsResolvedAgainByItsProperties()
    {
        Ok(_app.Open());
        var r = _app.Session.Refs.GetOrAssign(_app.One);
        // Simulate the element being recreated: the old object is gone, the new one has the same AutomationId
        _app.One.Alive = false;
        var replacement = new FakeElement(ControlKind.Button, "One", "num1Button") { Bounds = _app.One.Bounds };
        _app.Window.Add(replacement);
        Ok(_app.Run("click", ("target", r)));
        Assert.Contains("click:Left", replacement.Actions);
    }
}
