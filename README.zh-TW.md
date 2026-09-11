# flaui-cli

[English](README.md)

以 [FlaUI](https://github.com/FlaUI/FlaUI) 驅動、仿 [playwright-cli](https://github.com/microsoft/playwright-cli) 的 **Windows 桌面 UI 自動化命令列工具**。
設計給 AI Agent（例如 Claude Code）與腳本使用，主要用途：

1. **UI 測試**：Agent 即時操作並斷言，或執行 YAML 測試腳本並輸出 JUnit 報告。
2. **產生操作文件**：每個步驟自動截圖，並以紅框與編號標註操作的元素，輸出 Markdown（含 images/）與單檔 HTML。

支援 WPF、WinForms、Win32 與 UWP（例如 Windows 小算盤）。

> 工具的輸出訊息、`--help` 與 Agent 技能說明（SKILL.md）皆為英文。

## 安裝

需求：Windows 10/11 x64，不需要安裝 .NET runtime。

從 [Releases](https://github.com/littletree71/flaui-cli/releases) 下載 `flaui-cli-<版本>-win-x64.zip`，解壓縮後把資料夾加入 PATH 即可。
每個 release 都附有 SHA-256 雜湊值；repo 公開後，release 另會附上建置來源證明（build provenance attestation），可以用 [GitHub CLI](https://cli.github.com/) 驗證下載的檔案確實由本 repo 的 CI 建置：

```bash
gh attestation verify flaui-cli.exe -R littletree71/flaui-cli
```

執行檔目前尚未做程式碼簽章，第一次執行時 Windows SmartScreen 可能會跳出警告。

從原始碼建置需要 .NET 10 SDK：

```bash
# 發佈成單一執行檔（自含 runtime，目標電腦不需要安裝 .NET）
dotnet publish src/FlauiCli -p:PublishProfile=win-x64
# 產出 artifacts/flaui-cli/flaui-cli.exe，把這個資料夾加入 PATH 即可
```

為什麼不是 `dotnet tool` 或 Native AOT？

- .NET SDK 不允許目標框架含平台識別碼（例如 `net10.0-windows`）的專案封裝成 dotnet tool（NETSDK1146），而 FlaUI 只提供 Windows 目標框架。
- FlaUI.UIA3 使用內建 COM interop，Native AOT 不支援（UI Automation 物件無法建立），因此改用自含單檔發佈。

安裝給 Claude Code 使用的技能說明：

```bash
flaui-cli install-skill        # 複製到 ./.claude/skills/flaui-cli/SKILL.md
```

## 快速開始

```bash
flaui-cli open calc.exe --window Calculator     # 啟動並附加（背景 daemon 會自動啟動；中文系統標題為「小算盤」）
flaui-cli snapshot                              # UI 樹與元素 ref
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

selector 會自動等待元素出現，斷言會自動重試直到逾時（預設 5 秒，可用 `--timeout` 或設定檔調整）。

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
name: Calculator addition
app:
  launch: calc.exe
  window: Calculator
timeout: 5000
steps:
  - click: id=num1Button
    doc: 按下數字 1          # 操作文件中的步驟說明，可以用任何語言
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

自動產生的步驟說明是英文（例如 `Click the "One" button`）；用 `--note` 或腳本的 `doc:` 可以寫任何語言。

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

- CLI 每次執行只是 client，第一次 `open` / `attach` 時會以同一個 exe 的 daemon 模式啟動背景程序，透過 Named Pipe（僅限目前使用者）通訊；所有 UI Automation 呼叫都在 daemon 的單一執行緒上執行。
- **FlaUI 隔離**：Core 不引用 FlaUI，只依賴 `IUiDriver` / `IUiElement` 等抽象與自有列舉。FlaUI 版本在 `Directory.Packages.props` 精確鎖定；升級時必須通過 `DriverContractTests`（包含列舉對應完整性檢查）。

## 值得知道的行為（安全性與防毒）

flaui-cli 的某些行為會被安全軟體特別留意，以下都是刻意的設計：

- **模擬輸入與截圖**：動作會送出真實的滑鼠鍵盤輸入（`SendInput`），截圖會擷取螢幕。
- **全域鍵盤滑鼠 hook**：**只有**在執行 `record capture` 期間才會安裝，結束即移除，平時完全不安裝。
- **背景 daemon**：就是同一個 `flaui-cli.exe`，以 `flaui-cli.exe daemon --session <名稱>` 執行、不開主控台視窗。只有在呼叫端的 Job 會連帶結束它時才脫離 Job；閒置 30 分鐘自動結束（`close` / `kill-all` 可立即結束）。
- **本機檔案**：session 檔與記錄檔在 `%LOCALAPPDATA%\flaui-cli`，snapshot 與截圖在工作目錄的 `.flaui-cli`。
- 執行檔不要求提權（`asInvoker`），發佈版不壓縮、不加殼。

## 測試

```bash
dotnet test tests/FlauiCli.Core.Tests                                   # 單元測試
dotnet test tests/FlauiCli.E2E.Tests                                    # 需要互動式桌面，執行期間請勿操作滑鼠鍵盤
```

## 已知限制

- 動作會移動真實滑鼠、送出真實按鍵，執行期間請勿操作電腦（`click --invoke` 可避免移動滑鼠）。
- UWP 程式的視窗由 ApplicationFrameHost 承載，`open` 需要 `--window` 指定標題；`close` 只會關閉視窗，不會結束共用程序。
- 真人操作錄製依賴低階 hook，以滑鼠按下時的座標判斷元素，快速變動的 UI（例如動畫中的選單）可能錄到非預期元素。

## 授權

[MIT](LICENSE)。第三方元件與其授權列於 [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt)。
