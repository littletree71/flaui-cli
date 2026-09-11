using System.Drawing;
using FlauiCli.Core.Abstractions;
using FlauiCli.Core.Engine;
using FlauiCli.Core.Protocol;

namespace FlauiCli.Core.Tests.Fakes;

/// <summary>
/// A fake "calculator + form" application wired to a dispatcher, so dispatcher tests need no real UI.
/// </summary>
public sealed class TestApp : IDisposable
{
    public TestApp()
    {
        Cwd = Path.Combine(Path.GetTempPath(), "flaui-cli-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Cwd);

        Window = new FakeElement(ControlKind.Window, "Calculator") { Bounds = new Rectangle(0, 0, 400, 600), WindowHandle = 42 };
        Display = new FakeElement(ControlKind.Text, "Display is 0", "CalculatorResults") { Bounds = new Rectangle(10, 10, 380, 50) };
        One = Button("One", "num1Button", 10, 100);
        Two = Button("Two", "num2Button", 110, 100);
        Plus = Button("Plus", "plusButton", 210, 100);
        Equal = Button("Equals", "equalButton", 310, 100);
        NameBox = new FakeElement(ControlKind.Edit, "Name", "nameInput") { Value = "", Bounds = new Rectangle(10, 200, 200, 30) };
        Agree = new FakeElement(ControlKind.CheckBox, "I agree", "agreeCheck") { Toggle = ToggleValue.Off, Bounds = new Rectangle(10, 240, 100, 20) };
        Colors = new FakeElement(ControlKind.ComboBox, "Color", "colorCombo") { Value = "", Bounds = new Rectangle(10, 270, 150, 25) }
            .Add(new FakeElement(ControlKind.ListItem, "Red"), new FakeElement(ControlKind.ListItem, "Green"));
        Fruits = new FakeElement(ControlKind.List, "Fruit", "fruitList") { Bounds = new Rectangle(10, 300, 150, 80) }
            .Add(
                new FakeElement(ControlKind.ListItem, "Apple") { IsSelected = false, Bounds = new Rectangle(10, 300, 150, 20) },
                new FakeElement(ControlKind.ListItem, "Banana") { IsSelected = false, Bounds = new Rectangle(10, 320, 150, 20) });
        Disabled = Button("Disabled", "disabledButton", 10, 400);
        Disabled.IsEnabled = false;
        // Two buttons with the same name and no AutomationId (for nth tests)
        DupA = Button("Dup", "", 10, 450);
        DupB = Button("Dup", "", 110, 450);

        var keypad = new FakeElement(ControlKind.Group) { Bounds = new Rectangle(0, 90, 400, 60) }.Add(One, Two, Plus, Equal);
        Window.Add(Display, keypad, NameBox, Agree, Colors, Fruits, Disabled, DupA, DupB);

        var total = 0;
        var pending = 0;
        One.OnClick = () => { pending = pending * 10 + 1; Display.Name = $"Display is {pending}"; };
        Two.OnClick = () => { pending = pending * 10 + 2; Display.Name = $"Display is {pending}"; };
        Plus.OnClick = () => { total += pending; pending = 0; };
        Equal.OnClick = () => { total += pending; pending = 0; Display.Name = $"Display is {total}"; };

        Driver = new FakeDriver { OnLaunch = _ => Window };
        Session = new AutomationSession("test", () => Driver);
        Dispatcher = new CommandDispatcher(Session) { AutoSnapshot = false };
    }

    public string Cwd { get; }
    public FakeDriver Driver { get; }
    public AutomationSession Session { get; }
    public CommandDispatcher Dispatcher { get; }
    public FakeElement Window { get; }
    public FakeElement Display { get; }
    public FakeElement One { get; }
    public FakeElement Two { get; }
    public FakeElement Plus { get; }
    public FakeElement Equal { get; }
    public FakeElement NameBox { get; }
    public FakeElement Agree { get; }
    public FakeElement Colors { get; }
    public FakeElement Fruits { get; }
    public FakeElement Disabled { get; }
    public FakeElement DupA { get; }
    public FakeElement DupB { get; }

    private static FakeElement Button(string name, string id, int x, int y) =>
        new(ControlKind.Button, name, id) { Bounds = new Rectangle(x, y, 90, 40) };

    /// <summary>Runs a command: <c>Run("click", ("target", "id=num1Button"))</c>.</summary>
    public CommandResult Run(string command, params (string Key, string Value)[] args)
    {
        var call = new CommandCall(command) { Cwd = Cwd };
        foreach (var (k, v) in args) call.Set(k, v);
        return Dispatcher.Execute(call);
    }

    public CommandResult Open() => Run("open", ("app", "calc.exe"), ("window", "Calculator"));

    public void Dispose()
    {
        Session.Dispose();
        try { Directory.Delete(Cwd, recursive: true); } catch { /* ignore */ }
    }
}
