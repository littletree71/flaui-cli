namespace FlauiCli.Core.Abstractions;

// 自有列舉：輸出格式、selector 與腳本只依賴這些值，不直接依賴 FlaUI 的列舉，
// FlaUI 改版時由 Driver 層的對應表吸收差異（並由契約測試檢查對應是否完整）。

/// <summary>元素控制項類型（對應 UIA ControlType）。</summary>
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
