namespace FlauiCli.Core.Commands;

/// <summary>位置參數規格。</summary>
public sealed record ArgSpec(string Name, string Description, bool Required = true, bool Variadic = false);

/// <summary>選項規格。</summary>
public sealed record OptSpec(string Name, string Description, string? Alias = null, bool Flag = false, bool Multiple = false);

/// <summary>指令執行位置。</summary>
public enum CommandLocation
{
    /// <summary>送到 Daemon 執行。</summary>
    Daemon,

    /// <summary>在 CLI 行程內直接處理（list、run…）。</summary>
    Local,
}

/// <summary>指令規格：CLI 解析、YAML 腳本、錄製器與說明文件共用同一份定義。</summary>
public sealed record CommandSpec(
    string Name,
    string Group,
    string Description,
    ArgSpec[] Args,
    OptSpec[] Options,
    CommandLocation Location = CommandLocation.Daemon,
    bool Hidden = false)
{
    /// <summary>YAML 以純量撰寫時（<c>- click: id=ok</c>）對應的參數名稱。</summary>
    public string? PrimaryArg => Args.Length > 0 ? Args[0].Name : null;
}

/// <summary>所有指令的定義表。</summary>
public static class CommandCatalog
{
    /// <summary>值為元素目標、錄製時需轉成穩定 selector 的參數。</summary>
    public static readonly HashSet<string> TargetArgNames = ["target", "source", "dest", "highlight", "root"];

    private static readonly OptSpec Timeout = new("timeout", "逾時毫秒數（預設讀取設定檔 timeouts.action）");
    private static readonly OptSpec Note = new("note", "操作文件模式下此步驟的說明文字");
    private static readonly ArgSpec Target = new("target", "元素：snapshot ref（e12）或 selector（id=…、name=…、type=…&&name=…、xpath=…）");

    private static CommandSpec Element(string name, string description, params OptSpec[] extra) =>
        new(name, "動作", description, [Target], [.. extra, Timeout, Note]);

    public static readonly IReadOnlyList<CommandSpec> All =
    [
        // ── Session / 應用程式 ──
        new("open", "應用程式", "啟動應用程式並附加（exe 路徑、PATH 上的程式或 Store App 的 AUMID）",
            [new("app", "執行檔路徑或 AUMID（含 ! 視為 Store App）"), new("args", "傳給應用程式的參數", Required: false, Variadic: true)],
            [new("window", "依視窗標題（完全相符或包含）尋找主視窗，UWP / 會轉交其他程序的程式必填", "w"), new("timeout", "等待視窗逾時毫秒數")]),
        new("attach", "應用程式", "附加到執行中的應用程式",
            [new("process", "PID 或程序名稱", Required: false)],
            [new("title", "依視窗標題尋找（可與 process 並用）"), new("timeout", "等待視窗逾時毫秒數")]),
        new("close", "應用程式", "關閉目前的應用程式視窗並結束此 session 的 daemon", [], [new("keep-app", "只結束 session，不關閉應用程式", Flag: true)]),
        new("status", "應用程式", "顯示目前 session 狀態", [], []),
        new("list", "應用程式", "列出所有 session", [], [], CommandLocation.Local),
        new("close-all", "應用程式", "關閉所有 session", [], [], CommandLocation.Local),
        new("kill-all", "應用程式", "強制結束所有 daemon 程序", [], [], CommandLocation.Local),

        // ── 視窗 ──
        new("windows", "視窗", "列出目前應用程式的所有頂層視窗", [], []),
        new("window", "視窗", "切換目前操作的視窗", [new("window", "索引（windows 的編號）、ref 或標題")], []),
        new("focus", "視窗", "將視窗帶到前景；指定 target 時改為讓元素取得焦點", [new("target", "元素（省略則為目前視窗）", Required: false)], [Timeout]),
        new("maximize", "視窗", "最大化目前視窗", [], []),
        new("minimize", "視窗", "最小化目前視窗", [], []),
        new("restore", "視窗", "還原目前視窗", [], []),
        new("resize", "視窗", "調整目前視窗大小", [new("width", "寬（像素）"), new("height", "高（像素）")], []),
        new("move", "視窗", "移動目前視窗", [new("x", "左上角 X"), new("y", "左上角 Y")], []),

        // ── 檢視 ──
        new("snapshot", "檢視", "輸出目前視窗的 UI 樹與元素 ref",
            [],
            [new("depth", "最大深度（0 為不限）"), new("boxes", "附上元素座標 [x,y,w,h]", Flag: true),
             new("filename", "另存到檔案"), new("root", "只輸出此元素之下的子樹"), new("all", "包含畫面外與無名稱的容器", Flag: true)]),
        new("find", "檢視", "以文字搜尋元素（比對 Name / AutomationId / Value）",
            [new("text", "要搜尋的文字")], [new("regex", "以正規表示式比對", Flag: true)]),
        new("inspect", "檢視", "列出元素所有屬性、支援的 pattern 與建議 selector", [Target], [Timeout]),

        // ── 動作 ──
        Element("click", "點擊元素",
            new("button", "滑鼠按鍵 left|right|middle"), new("double", "雙擊", Flag: true),
            new("invoke", "使用 InvokePattern 而不移動滑鼠", Flag: true)),
        Element("dblclick", "雙擊元素"),
        Element("hover", "滑鼠移到元素上"),
        new("fill", "動作", "清空並填入文字（優先使用 ValuePattern）",
            [Target, new("text", "要填入的文字")], [new("keyboard", "強制以鍵盤模擬輸入", Flag: true), Timeout, Note]),
        new("type", "動作", "在目前焦點元素上以鍵盤輸入文字", [new("text", "文字")], [Note]),
        new("press", "動作", "按下按鍵或組合鍵，例如 Enter、Ctrl+A、Alt+F4", [new("keys", "按鍵")], [new("target", "先讓此元素取得焦點"), Timeout, Note]),
        new("select", "動作", "在 ComboBox / ListBox / Tab 等容器中選取項目",
            [Target, new("item", "項目名稱或索引（#2 表示第 3 項）")], [Timeout, Note]),
        Element("check", "勾選核取方塊 / 切換按鈕"),
        Element("uncheck", "取消勾選"),
        Element("expand", "展開（樹狀節點、下拉選單、選單）"),
        Element("collapse", "收合"),
        Element("invoke", "觸發 InvokePattern"),
        new("scroll", "動作", "捲動元素",
            [Target], [new("dir", "方向 up|down|left|right（預設 down）"), new("amount", "捲動次數（預設 3）"), Timeout, Note]),
        new("drag", "動作", "拖放：從 source 拖到 dest",
            [new("source", "起點元素"), new("dest", "終點元素")], [Timeout, Note]),

        // ── 讀取 / 等待 / 斷言 ──
        new("get", "讀取", "讀取元素資訊",
            [new("kind", "text|value|name|state|rect|prop"), Target, new("prop", "kind=prop 時的屬性名稱，例如 HelpText", Required: false)],
            [Timeout]),
        new("wait", "等待", "等待元素達到指定狀態",
            [Target], [new("state", "visible|hidden|enabled|disabled|exists|gone（預設 visible）"), Timeout]),
        new("wait-window", "等待", "等待標題符合的視窗出現並切換過去",
            [new("title", "視窗標題（完全相符或包含）")], [new("no-switch", "不切換目前視窗", Flag: true), Timeout]),
        new("assert", "斷言", "斷言元素狀態，失敗時結束碼為 1",
            [new("kind", "exists|not-exists|visible|hidden|enabled|disabled|checked|unchecked|text|contains|matches|value"),
             Target, new("expected", "預期值（text/contains/matches/value 必填）", Required: false)],
            [Timeout]),
        new("sleep", "等待", "暫停指定毫秒數", [new("ms", "毫秒")], []),

        // ── 截圖 ──
        new("screenshot", "截圖", "擷取目前視窗或元素畫面",
            [new("target", "只擷取此元素", Required: false)],
            [new("filename", "輸出檔名（預設存到輸出目錄）"), new("highlight", "以紅框標註的元素，可重複", Multiple: true),
             new("screen", "擷取整個螢幕", Flag: true), Timeout]),

        // ── 錄製 ──
        new("record", "錄製", "錄製操作並匯出成 YAML 腳本",
            [new("action", "start（記錄 CLI 指令）| capture（捕捉真人滑鼠鍵盤）| stop | status")],
            [new("out", "stop 時輸出的 YAML 檔"), new("name", "腳本名稱")]),

        // ── 操作文件 ──
        new("doc", "文件", "產生附截圖的操作文件",
            [new("action", "start | step | stop | status"), new("text", "step 的說明文字", Required: false)],
            [new("title", "文件標題（start）"), new("out", "輸出資料夾（stop）"), new("format", "md,html（預設兩者）"),
             new("highlight", "step 時要標註的元素，可重複", Multiple: true)]),

        // ── 腳本 ──
        new("run", "腳本", "執行 YAML 測試腳本",
            [new("files", "腳本檔（可多個）", Variadic: true)],
            [new("reporter", "console|junit（預設 console）"), new("output", "JUnit 報告輸出路徑"),
             new("doc", "同時產生操作文件到此資料夾"), new("doc-format", "md,html"), new("bail", "第一個腳本失敗就停止", Flag: true)],
            CommandLocation.Local),

        // ── 其他 ──
        new("install-skill", "其他", "安裝 Agent 使用說明（SKILL.md）到 .claude/skills/flaui-cli",
            [], [new("dir", "自訂安裝目錄")], CommandLocation.Local),
        new("daemon", "其他", "（內部）以 daemon 模式執行", [], [], CommandLocation.Local, Hidden: true),
    ];

    private static readonly Dictionary<string, CommandSpec> ByName =
        All.ToDictionary(s => s.Name, StringComparer.OrdinalIgnoreCase);

    public static CommandSpec? Find(string name) => ByName.GetValueOrDefault(name);

    /// <summary>不會改變 UI 狀態、錄製時略過的指令。</summary>
    public static readonly HashSet<string> ReadOnlyCommands =
        new(["status", "windows", "snapshot", "find", "inspect", "get", "record", "doc", "list"], StringComparer.OrdinalIgnoreCase);

    /// <summary>會操作元素、執行後自動存 snapshot 並記錄到操作文件的指令。</summary>
    public static readonly HashSet<string> ActionCommands =
        new(["click", "dblclick", "hover", "fill", "type", "press", "select", "check", "uncheck", "expand", "collapse",
             "invoke", "scroll", "drag", "focus"], StringComparer.OrdinalIgnoreCase);
}
