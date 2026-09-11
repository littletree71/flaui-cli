namespace FlauiCli.Core.Abstractions;

// Core-owned enums: output formats, selectors and scripts depend only on these values and
// never on FlaUI's enums directly. Differences between FlaUI versions are absorbed by the
// mapping table in the driver layer (and its completeness is checked by the contract tests).

/// <summary>Control type of an element (maps to UIA ControlType).</summary>
public enum ControlKind
{
    Unknown,
    AppBar,
    Button,
    Calendar,
    CheckBox,
    ComboBox,
    Custom,
    DataGrid,
    DataItem,
    Document,
    Edit,
    Group,
    Header,
    HeaderItem,
    Hyperlink,
    Image,
    List,
    ListItem,
    MenuBar,
    Menu,
    MenuItem,
    Pane,
    ProgressBar,
    RadioButton,
    ScrollBar,
    SemanticZoom,
    Separator,
    Slider,
    Spinner,
    SplitButton,
    StatusBar,
    Tab,
    TabItem,
    Table,
    Text,
    Thumb,
    TitleBar,
    ToolBar,
    ToolTip,
    Tree,
    TreeItem,
    Window,
}

public enum ToggleValue
{
    Off,
    On,
    Indeterminate,
}

public enum ExpandValue
{
    Collapsed,
    Expanded,
    PartiallyExpanded,
    LeafNode,
}

public enum MouseButtonKind
{
    Left,
    Right,
    Middle,
}

public enum WindowStateKind
{
    Normal,
    Maximized,
    Minimized,
}

public enum ScrollDirection
{
    Up,
    Down,
    Left,
    Right,
}

public enum SearchScope
{
    Children,
    Descendants,
}
