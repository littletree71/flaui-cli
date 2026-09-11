# flaui-cli

以 [FlaUI](https://github.com/FlaUI/FlaUI) 驅動、仿 [playwright-cli](https://github.com/microsoft/playwright-cli) 的 **Windows 桌面 UI 自動化命令列工具**。
設計給 AI Agent（例如 Claude Code）與腳本使用，主要用途：

1. **UI 測試**：Agent 即時操作 + 斷言，或執行 YAML 測試腳本並輸出 JUnit 報告。
2. **產生操作文件**：每個步驟自動截圖並以紅框、編號標註操作的元素，輸出 Markdown（含 images/）與單檔 HTML。

支援 WPF、WinForms、Win32 與 UWP（例如 Windows 小算盤）。

## 安裝

需求：Windows 10/11；從原始碼建置需要 .NET 10 SDK。

```bash
# 發佈成單一執行檔（自含 runtime，目標電腦不需要安裝 .NET）
dotnet publish src/FlauiCli -p:PublishProfile=win-x64
# 產出 artifacts/flaui-cli/flaui-cli.exe，把這個資料夾加入 PATH 即可
```

> 為什麼不是 `dotnet tool`？.NET SDK 不允許目標框架含平台識別碼（例如 `net10.0-windows`）的專案封裝成 dotnet tool（NETSDK1146），而 FlaUI 只提供 Windows 目標框架，因此改用單一執行檔發佈。

安裝給 Claude Code 使用的技能說明：

```bash
flaui-cli install-skill        # 複製到 ./.claude/skills/flaui-cli/SKILL.md
```

## 快速開始

```bash
flaui-cli open calc.exe --window Calculator     # 啟動並附加（背景 daemon 自動啟動）
flaui-cli snapshot                              # UI 樹 + 元素 ref
flaui-cli click id=num1Button
flaui-cli click e21                             # 也可以用 snapshot 的 ref
flaui-cli assert text id=CalculatorResults "Display is 1"
flaui-cli screenshot --highlight id=CalculatorResults
flaui-cli close
```

snapshot 輸出範例：

```yaml
- window "Calculator" [ref=e1]
  - text "Display is 0" [ref=e5] id=CalculatorResults
  - button "One" [ref=e21] id=num1Button
  - checkbox "I agree" [ref=e31] [checked]
```

## 元素定位

| 寫法 | 說明 |
|---|---|
| `e21` | snapshot ref（同一元素在多次 snapshot 之間保持相同） |
| `id=num1Button` | AutomationId |
| `name="One"`、`One` | Name 完全相符 |
| `text=Disp` | Name 包含（不分大小寫） |
| `type=Button&&name=OK` | 條件組合 |
| `id=panel >> name=OK` | 階層 |
| `type=ListItem&&nth=2` | 第 N 個（從 0 起算） |
| `xpath=//Button[@AutomationId='ok']` | FlaUI XPath |

selector 會自動等待元素出現，斷言會自動重試直到逾時（預設 5 秒，`--timeout` 或設定檔可調）。

## 指令一覽

執行 `flaui-cli --help` 或 `flaui-cli <指令> --help` 查看完整說明。

| 類別 | 指令 |
|---|---|
| 應用程式 | `open` `attach` `close` `status` `list` `close-all` `kill-all` |
| 視窗 | `windows` `window` `focus` `maximize` `minimize` `restore` `resize` `move` `wait-window` |
| 檢視 | `snapshot` `find` `inspect` |
| 動作 | `click` `dblclick` `hover` `fill` `type` `press` `select` `check` `uncheck` `expand` `collapse` `invoke` `scroll` `drag` |
| 讀取 / 等待 / 斷言 | `get` `wait` `assert` `sleep` |
| 截圖 | `screenshot` |
| 錄製 | `record start\|capture\|status\|stop` |
| 文件 | `doc start\|step\|status\|stop` |
| 腳本 | `run` |

全域選項：`-s, --session <名稱>`（多個 session 並行）、`--json`（結構化輸出）。
結束碼：`0` 成功、`1` 斷言失敗、`2` 錯誤、`3` session 未啟動。

## YAML 測試腳本

```yaml
name: 小算盤加法
app:
  launch: calc.exe
  window: Calculator
timeout: 5000
steps:
  - click: id=num1Button
    doc: 按下數字 1
  - click: id=plusButton
  - click: id=num2Button
  - click: id=equalButton
  - assert: { target: id=CalculatorResults, text: Display is 3 }
```

```bash
flaui-cli run samples/calculator.flow.yaml                                 # console 報告
flaui-cli run tests/*.flow.yaml --reporter junit --output out/report.xml    # CI
flaui-cli run samples/calculator.flow.yaml --doc out/calc-doc              # 同時產生操作文件
```

### 錄製

```bash
flaui-cli record start --name 登入         # 記錄之後執行的 CLI 指令（ref 自動轉成穩定 selector）
...
flaui-cli record stop --out login.flow.yaml

flaui-cli record capture --out flow.yaml  # 捕捉真人滑鼠鍵盤操作，Ctrl+Shift+Q 結束
```

## 操作文件

```bash
flaui-cli doc start --title "小算盤使用說明"
flaui-cli click id=num1Button --note "按下數字 1"
flaui-cli doc step "完成"
flaui-cli doc stop --out docs/calculator --format md,html
```

## 設定檔

`.flaui-cli/config.json`（相對於目前工作目錄，全部選填）：

```json
{
  "timeouts": { "action": 5000, "launch": 20000 },
  "outputDir": ".flaui-cli",
  "autoSnapshot": true,
  "snapshot": { "depth": 0, "includeOffscreen": false },
  "daemonIdleMinutes": 30
}
```

## 架構

```
src/
  FlauiCli/                CLI 前端（System.CommandLine）、daemon client、本機指令
  FlauiCli.Core/           自動化引擎：抽象介面、selector、snapshot、dispatcher、腳本、錄製、文件、daemon
  FlauiCli.Driver.FlaUI/   唯一引用 FlaUI 的轉接層（實作 IUiDriver / IUiElement）
tests/
  FlauiCli.Core.Tests/     單元測試（FakeDriver，不需要桌面）
  FlauiCli.E2E.Tests/      DriverContractTests（FlaUI 轉接層契約）+ CLI 端對端測試
  TestApps/WpfSample/      測試用 WPF 程式
```

- CLI 每次執行只是 client，第一次 `open` / `attach` 時會啟動同一個 exe 的 daemon 模式，透過 Named Pipe（僅限目前使用者）通訊；所有 UIA 呼叫都在 daemon 的單一執行緒上執行。
- **FlaUI 隔離**：Core 不引用 FlaUI，只依賴 `IUiDriver` / `IUiElement` 等抽象與自有列舉。FlaUI 版本在 `Directory.Packages.props` 精確鎖定；升級時必須通過 `DriverContractTests`（包含列舉對應完整性檢查）。

## 測試

```bash
dotnet test tests/FlauiCli.Core.Tests                                   # 單元測試
dotnet test tests/FlauiCli.E2E.Tests                                    # 需要互動式桌面，執行期間請勿操作滑鼠鍵盤
```

## 已知限制

- 動作會移動真實滑鼠、送出真實按鍵，執行期間請勿操作電腦（`click --invoke` 可避免移動滑鼠）。
- UWP 程式的視窗由 ApplicationFrameHost 承載，`open` 需要 `--window` 指定標題；`close` 只會關閉視窗，不會結束共用程序。
- 真人操作錄製依賴低階 hook，以滑鼠按下時的座標判斷元素，快速變動的 UI（例如動畫中的選單）可能錄到非預期元素。
