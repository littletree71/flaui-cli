using System.Drawing;
using FlauiCli.Core.Abstractions;

namespace FlauiCli.Core.Tests.Fakes;

/// <summary>In-memory UI element used to test Core logic without launching any application.</summary>
public sealed class FakeElement : IUiElement
{
    private static int _nextId;

    public FakeElement(ControlKind kind, string name = "", string automationId = "")
    {
        Kind = kind;
        Name = name;
        AutomationId = automationId;
        RuntimeId = "fake." + Interlocked.Increment(ref _nextId);
    }

    public string Name { get; set; }
    public string AutomationId { get; set; }
    public ControlKind Kind { get; set; }
    public string ClassName { get; set; } = "";
    public string FrameworkId { get; set; } = "Fake";
    public bool IsEnabled { get; set; } = true;
    public bool IsOffscreen { get; set; }
    public bool HasFocus { get; set; }
    public Rectangle Bounds { get; set; } = new(0, 0, 10, 10);
    public string? RuntimeId { get; set; }
    public int ProcessId { get; set; } = 100;
    public nint WindowHandle { get; set; }
    public bool IsPassword { get; set; }
    public bool Alive { get; set; } = true;
    public bool IsAlive => Alive;
    public ToggleValue? Toggle { get; set; }
    public string? Value { get; set; }
    public bool? IsReadOnly { get; set; }
    public ExpandValue? Expand { get; set; }
    public bool? IsSelected { get; set; }
    public bool SupportsInvoke { get; set; } = true;
    public IReadOnlyList<string> SupportedPatterns { get; set; } = [];
    public Dictionary<string, string> ExtraProperties { get; } = [];

    public FakeElement? Parent { get; private set; }

    IUiElement? IUiElement.Parent => Parent;

    public List<FakeElement> Children { get; } = [];

    /// <summary>Invoked when the element is clicked (or invoked), to simulate the application's reaction.</summary>
    public Action? OnClick { get; set; }

    /// <summary>Operations this element received.</summary>
    public List<string> Actions { get; } = [];

    public FakeElement Add(params FakeElement[] children)
    {
        foreach (var c in children)
        {
            c.Parent = this;
            c.ProcessId = ProcessId;
            Children.Add(c);
        }
        return this;
    }

    public IEnumerable<FakeElement> Descendants()
    {
        foreach (var c in Children)
        {
            yield return c;
            foreach (var d in c.Descendants()) yield return d;
        }
    }

    public string? GetProperty(string name) => ExtraProperties.GetValueOrDefault(name);

    public Point? TryGetClickablePoint() => Bounds.IsEmpty ? null : Bounds.Center();

    public IReadOnlyList<IUiElement> FindAll(ElementQuery query, SearchScope scope = SearchScope.Descendants)
    {
        var source = scope == SearchScope.Children ? Children : Descendants();
        return [.. source.Where(e => e.Alive && Matches(e, query))];
    }

    private static bool Matches(FakeElement e, ElementQuery q) =>
        (q.AutomationId is null || e.AutomationId == q.AutomationId)
        && (q.Name is null || e.Name == q.Name)
        && (q.ClassName is null || e.ClassName == q.ClassName)
        && (q.Kind is null || e.Kind == q.Kind)
        && (q.ProcessId is null || e.ProcessId == q.ProcessId);

    public IUiElement? FindFirst(ElementQuery query)
    {
        FindFirstCalls++;
        return Descendants().FirstOrDefault(e => e.Alive && Matches(e, query));
    }

    /// <summary>Number of FindFirst calls on this element.</summary>
    public int FindFirstCalls { get; private set; }

    public IReadOnlyList<IUiElement> FindByXPath(string xpath) => [];

    public string? GetXPathFrom(IUiElement root) => null;

    /// <summary>Number of CaptureTree calls on this element (not counting the recursion into children).</summary>
    public int CaptureTreeCalls { get; private set; }

    /// <summary>Depth passed to the last CaptureTree call.</summary>
    public CaptureDepth? LastCaptureDepth { get; private set; }

    public ElementNode CaptureTree(CaptureDepth? depth = null, bool complete = true)
    {
        CaptureTreeCalls++;
        LastCaptureDepth = depth;
        return Capture(depth, level: 0, transparent: false, expand: true);
    }

    /// <summary>Builds the node; with a depth, leaves out what the driver would not fetch (see TreeCapture in the FlaUI driver).</summary>
    private ElementNode Capture(CaptureDepth? depth, int level, bool transparent, bool expand)
    {
        var node = new ElementNode
        {
            Element = this,
            Name = Name,
            AutomationId = AutomationId,
            Kind = Kind,
            ClassName = ClassName,
            IsEnabled = IsEnabled,
            IsOffscreen = IsOffscreen,
            Bounds = Bounds,
            RuntimeId = RuntimeId,
            Toggle = Toggle,
            Value = Value,
            Expand = Expand,
            IsSelected = IsSelected,
            HasFocus = HasFocus,
        };
        if (!expand) return node;
        var expandChildren = depth?.ExpandsChildren(level, transparent) ?? true;
        var childLevel = CaptureDepth.ChildLevel(level, transparent);
        foreach (var c in Children.Where(c => c.Alive))
        {
            var childNode = c.Capture(null, 0, false, expand: false);
            var childTransparent = depth?.IsTransparent(childNode) ?? false;
            node.Children.Add(c.Capture(depth, childLevel, childTransparent, expandChildren));
        }
        return node;
    }

    public void Focus()
    {
        Actions.Add("focus");
        HasFocus = true;
    }

    public void SetForeground() => Actions.Add("foreground");

    public bool TryInvoke()
    {
        if (!SupportsInvoke) return false;
        Actions.Add("invoke");
        OnClick?.Invoke();
        return true;
    }

    public bool TrySetValue(string value)
    {
        if (Value is null || IsReadOnly == true) return false;
        Actions.Add("setvalue:" + value);
        Value = value;
        return true;
    }

    public bool TryToggle()
    {
        if (Toggle is null) return false;
        Actions.Add("toggle");
        Toggle = Toggle == ToggleValue.On ? ToggleValue.Off : ToggleValue.On;
        return true;
    }

    public bool TrySelectItem()
    {
        if (IsSelected is null) return false;
        Actions.Add("select");
        if (Parent is not null)
            foreach (var s in Parent.Children.Where(s => s.IsSelected is not null)) s.IsSelected = false;
        IsSelected = true;
        return true;
    }

    public bool TryExpand()
    {
        if (Expand is null) return false;
        Actions.Add("expand");
        Expand = ExpandValue.Expanded;
        return true;
    }

    public bool TryCollapse()
    {
        if (Expand is null) return false;
        Actions.Add("collapse");
        Expand = ExpandValue.Collapsed;
        return true;
    }

    public bool TryScrollIntoView()
    {
        Actions.Add("scrollintoview");
        IsOffscreen = false;
        return true;
    }

    public bool TryScroll(ScrollDirection direction) => false;

    /// <summary>False simulates a combo box that accepts the selection without applying it (WinForms ToolStripComboBox).</summary>
    public bool SelectAppliesValue { get; set; } = true;

    public bool TrySelectComboBoxItem(string? text, int? index, out string? selected)
    {
        selected = null;
        if (Kind != ControlKind.ComboBox) return false;
        var items = Children.Where(c => c.Kind == ControlKind.ListItem).ToList();
        var item = index is int i ? items.ElementAtOrDefault(i) : items.FirstOrDefault(c => c.Name == text);
        if (item is null) return false;
        if (SelectAppliesValue) Value = item.Name;
        selected = item.Name;
        Actions.Add("combo:" + item.Name);
        return true;
    }

    public IReadOnlyList<string>? TryGetComboBoxItems() =>
        Kind == ControlKind.ComboBox ? [.. Children.Where(c => c.Kind == ControlKind.ListItem).Select(c => c.Name)] : null;

    public string? TryGetDocumentText() => null;

    /// <summary>Thrown by TrySetWindowState, as WindowPattern does while a modal dialog disables the window.</summary>
    public Exception? WindowStateError { get; set; }

    public bool TrySetWindowState(WindowStateKind state)
    {
        if (WindowStateError is { } error) throw error;
        Actions.Add("state:" + state);
        return Kind == ControlKind.Window;
    }

    public bool TryResize(int width, int height)
    {
        Actions.Add($"resize:{width}x{height}");
        Bounds = new Rectangle(Bounds.X, Bounds.Y, width, height);
        return true;
    }

    public bool TryMove(int x, int y)
    {
        Actions.Add($"move:{x},{y}");
        Bounds = new Rectangle(x, y, Bounds.Width, Bounds.Height);
        return true;
    }

    public bool TryClose()
    {
        Actions.Add("close");
        Alive = false;
        return true;
    }

    public bool Equals(IUiElement? other) => ReferenceEquals(this, other);

    public override bool Equals(object? obj) => ReferenceEquals(this, obj);

    public override int GetHashCode() => RuntimeId?.GetHashCode() ?? 0;

    public override string ToString() => $"{Kind} \"{Name}\" id={AutomationId}";
}
