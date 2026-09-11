namespace FlauiCli.Core.Commands;

/// <summary>Positional argument definition.</summary>
public sealed record ArgSpec(string Name, string Description, bool Required = true, bool Variadic = false);

/// <summary>Option definition.</summary>
public sealed record OptSpec(string Name, string Description, string? Alias = null, bool Flag = false, bool Multiple = false);

/// <summary>Where a command runs.</summary>
public enum CommandLocation
{
    /// <summary>Sent to the daemon.</summary>
    Daemon,

    /// <summary>Handled inside the CLI process (list, run...).</summary>
    Local,
}

/// <summary>Command definition shared by the CLI parser, YAML scripts, the recorder and the documentation.</summary>
public sealed record CommandSpec(
    string Name,
    string Group,
    string Description,
    ArgSpec[] Args,
    OptSpec[] Options,
    CommandLocation Location = CommandLocation.Daemon,
    bool Hidden = false)
{
    /// <summary>Argument that a scalar YAML value maps to (<c>- click: id=ok</c>).</summary>
    public string? PrimaryArg => Args.Length > 0 ? Args[0].Name : null;
}

/// <summary>Definitions of every command.</summary>
public static class CommandCatalog
{
    /// <summary>Arguments whose values are element targets and are converted to stable selectors when recording.</summary>
    public static readonly HashSet<string> TargetArgNames = ["target", "source", "dest", "highlight", "root"];

    private static readonly OptSpec Timeout = new("timeout", "Timeout in milliseconds (defaults to timeouts.action in the config file)");
    private static readonly OptSpec Note = new("note", "Description of this step when recording a document");
    private static readonly ArgSpec Target = new("target", "Element: snapshot ref (e12) or selector (id=..., name=..., type=...&&name=..., xpath=...)");

    private static CommandSpec Element(string name, string description, params OptSpec[] extra) =>
        new(name, "Actions", description, [Target], [.. extra, Timeout, Note]);

    public static readonly IReadOnlyList<CommandSpec> All =
    [
        // ── Session / application ──
        new("open", "Application", "Launch an application and attach to it (exe path, program on PATH, or Store app AUMID)",
            [new("app", "Executable path or AUMID (a value containing ! is treated as a Store app)"), new("args", "Arguments passed to the application", Required: false, Variadic: true)],
            [new("window", "Find the main window by title (exact or contains); required for UWP apps and launchers that hand off to another process", "w"), new("timeout", "Timeout in milliseconds to wait for the window")]),
        new("attach", "Application", "Attach to a running application",
            [new("process", "PID or process name", Required: false)],
            [new("title", "Find the window by title (can be combined with process)"), new("timeout", "Timeout in milliseconds to wait for the window")]),
        new("close", "Application", "Close the current application window and stop this session's daemon", [], [new("keep-app", "Only end the session and leave the application running", Flag: true)]),
        new("status", "Application", "Show the current session status", [], []),
        new("list", "Application", "List all sessions", [], [], CommandLocation.Local),
        new("close-all", "Application", "Close all sessions", [], [], CommandLocation.Local),
        new("kill-all", "Application", "Forcefully stop all daemon processes", [], [], CommandLocation.Local),

        // ── Windows ──
        new("windows", "Windows", "List the top-level windows of the current application", [], []),
        new("window", "Windows", "Switch the window that commands operate on", [new("window", "Index (as listed by windows), ref, or title")], []),
        new("focus", "Windows", "Bring the window to the foreground; with a target, focus that element instead", [new("target", "Element (omit for the current window)", Required: false)], [Timeout]),
        new("maximize", "Windows", "Maximize the current window", [], []),
        new("minimize", "Windows", "Minimize the current window", [], []),
        new("restore", "Windows", "Restore the current window", [], []),
        new("resize", "Windows", "Resize the current window", [new("width", "Width in pixels"), new("height", "Height in pixels")], []),
        new("move", "Windows", "Move the current window", [new("x", "Left edge X"), new("y", "Top edge Y")], []),

        // ── Inspection ──
        new("snapshot", "Inspection", "Print the UI tree of the current window with element refs",
            [],
            [new("depth", "Maximum depth (0 = unlimited)"), new("boxes", "Include element bounds [x,y,w,h]", Flag: true),
             new("filename", "Save to a file instead of printing"), new("root", "Only print the subtree under this element"), new("all", "Include off-screen elements and unnamed containers", Flag: true)]),
        new("find", "Inspection", "Search elements by text (matches Name / AutomationId / Value)",
            [new("text", "Text to search for")], [new("regex", "Treat the text as a regular expression", Flag: true)]),
        new("inspect", "Inspection", "Show every property, supported pattern and a suggested selector for an element", [Target], [Timeout]),

        // ── Actions ──
        Element("click", "Click an element",
            new("button", "Mouse button: left|right|middle"), new("double", "Double-click", Flag: true),
            new("invoke", "Use InvokePattern instead of moving the mouse", Flag: true)),
        Element("dblclick", "Double-click an element"),
        Element("hover", "Move the mouse over an element"),
        new("fill", "Actions", "Clear an input and type text (uses ValuePattern when available)",
            [Target, new("text", "Text to enter")], [new("keyboard", "Always simulate keyboard input", Flag: true), Timeout, Note]),
        new("type", "Actions", "Type text into the focused element", [new("text", "Text")], [Note]),
        new("press", "Actions", "Press a key or key combination, for example Enter, Ctrl+A, Alt+F4", [new("keys", "Keys")], [new("target", "Focus this element first"), Timeout, Note]),
        new("select", "Actions", "Select an item in a ComboBox / ListBox / Tab / Tree container",
            [Target, new("item", "Item name or index (#2 is the third item)")], [Timeout, Note]),
        Element("check", "Check a check box or toggle button"),
        Element("uncheck", "Uncheck a check box or toggle button"),
        Element("expand", "Expand a tree node, drop-down or menu"),
        Element("collapse", "Collapse a tree node, drop-down or menu"),
        Element("invoke", "Trigger the element's InvokePattern"),
        new("scroll", "Actions", "Scroll an element",
            [Target], [new("dir", "Direction: up|down|left|right (default down)"), new("amount", "Number of scroll steps (default 3)"), Timeout, Note]),
        new("drag", "Actions", "Drag from source to dest",
            [new("source", "Element to drag"), new("dest", "Element to drop onto")], [Timeout, Note]),

        // ── Read / wait / assert ──
        new("get", "Read", "Read information from an element",
            [new("kind", "text|value|name|state|rect|prop"), Target, new("prop", "Property name when kind=prop, for example HelpText", Required: false)],
            [Timeout]),
        new("wait", "Wait", "Wait until an element reaches a state",
            [Target], [new("state", "visible|hidden|enabled|disabled|exists|gone (default visible)"), Timeout]),
        new("wait-window", "Wait", "Wait for a window whose title matches and switch to it",
            [new("title", "Window title (exact or contains)")], [new("no-switch", "Do not switch the current window", Flag: true), Timeout]),
        new("assert", "Assert", "Assert an element state; exits with code 1 on failure",
            [new("kind", "exists|not-exists|visible|hidden|enabled|disabled|checked|unchecked|text|contains|matches|value"),
             Target, new("expected", "Expected value (required for text/contains/matches/value)", Required: false)],
            [Timeout]),
        new("sleep", "Wait", "Pause for a number of milliseconds", [new("ms", "Milliseconds")], []),

        // ── Screenshots ──
        new("screenshot", "Screenshots", "Capture the current window or an element",
            [new("target", "Only capture this element", Required: false)],
            [new("filename", "Output file name (defaults to the output directory)"), new("highlight", "Element to outline in red; can be repeated", Multiple: true),
             new("screen", "Capture the whole screen", Flag: true), Timeout]),

        // ── Recording ──
        new("record", "Recording", "Record actions and export them as a YAML script",
            [new("action", "start (record CLI commands) | capture (capture real mouse/keyboard input) | stop | status")],
            [new("out", "YAML file written on stop"), new("name", "Script name")]),

        // ── Documentation ──
        new("doc", "Documentation", "Produce step-by-step documentation with annotated screenshots",
            [new("action", "start | step | stop | status"), new("text", "Description for step", Required: false)],
            [new("title", "Document title (start)"), new("out", "Output folder (stop)"), new("format", "md,html (default both)"),
             new("highlight", "Element to outline for step; can be repeated", Multiple: true)]),

        // ── Scripts ──
        new("run", "Scripts", "Run YAML test scripts",
            [new("files", "Script files (one or more)", Variadic: true)],
            [new("reporter", "console|junit (default console)"), new("output", "JUnit report path"),
             new("doc", "Also produce documentation into this folder"), new("doc-format", "md,html"), new("bail", "Stop after the first failing script", Flag: true)],
            CommandLocation.Local),

        // ── Other ──
        new("install-skill", "Other", "Install the agent skill (SKILL.md) into .claude/skills/flaui-cli",
            [], [new("dir", "Custom install directory")], CommandLocation.Local),
        new("daemon", "Other", "(internal) Run in daemon mode", [], [], CommandLocation.Local, Hidden: true),
    ];

    private static readonly Dictionary<string, CommandSpec> ByName =
        All.ToDictionary(s => s.Name, StringComparer.OrdinalIgnoreCase);

    public static CommandSpec? Find(string name) => ByName.GetValueOrDefault(name);

    /// <summary>Commands that do not change the UI and are skipped by the recorder.</summary>
    public static readonly HashSet<string> ReadOnlyCommands =
        new(["status", "windows", "snapshot", "find", "inspect", "get", "record", "doc", "list"], StringComparer.OrdinalIgnoreCase);

    /// <summary>Commands that operate on elements; they save an automatic snapshot and are captured in documents.</summary>
    public static readonly HashSet<string> ActionCommands =
        new(["click", "dblclick", "hover", "fill", "type", "press", "select", "check", "uncheck", "expand", "collapse",
             "invoke", "scroll", "drag", "focus"], StringComparer.OrdinalIgnoreCase);
}
