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
zip 內含 `flaui-cli.exe`、真人操作錄製工具 `flaui-cli-record.exe`（只有 `record capture` 會用到，請與 `flaui-cli.exe` 放在同一個資料夾）、
授權聲明與 Agent 技能說明（`skills/SKILL.md`）；兩個執行檔也會單獨附在 release 上。
每個 release 都附有 SHA-256 雜湊值；repo 公開後，release 另會附上建置來源證明（build provenance attestation），可以用 [GitHub CLI](https://cli.github.com/) 驗證下載的檔案確實由本 repo 的 CI 建置：

```bash
gh attestation verify flaui-cli.exe -R littletree71/flaui-cli
```

執行檔目前尚未做程式碼簽章，第一次執行時 Windows SmartScreen 可能會跳出警告；部分防毒軟體也會封鎖沒看過的未簽章執行檔
（通常是對陌生檔案的信譽判斷，不是偵測到它的行為）。遇到時請向防毒廠商回報誤判，而不是降低防護。

從原始碼建置需要 .NET 10 SDK：

```bash
# 發佈成單一執行檔（自含 runtime，目標電腦不需要安裝 .NET）
dotnet publish src/FlauiCli -p:PublishProfile=win-x64
dotnet publish src/FlauiCli.Recorder -p:PublishProfile=win-x64   # 只有 record capture 需要
# 產出 artifacts/flaui-cli/flaui-cli.exe（以及 flaui-cli-record.exe），把這個資料夾加入 PATH 即可
```

為什麼不是 `dotnet tool` 或 Native AOT？

- .NET SDK 不允許目標框架含平台識別碼（例如 `net10.0-windows`）的專案封裝成 dotnet tool（NETSDK1146），而 FlaUI 只提供 Windows 目標框架。
- FlaUI.UIA3 使用內建 COM interop，Native AOT 不支援（UI Automation 物件無法建立），因此改用自含單檔發佈。

安裝給 Claude Code 使用的技能說明：

```bash
flaui-cli install-skill                  # 複製到 ./.claude/skills/flaui-cli/SKILL.md
flaui-cli install-skill --dir <資料夾>    # 或安裝到其他資料夾
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
  - edit "Name" [ref=e30] id=nameInput value="Alice" [focused]
  - checkbox "I agree" [ref=e31] [checked]
```

狀態標記：`[disabled]` `[checked]` `[mixed]` `[expanded]` `[collapsed]` `[selected]` `[focused]`。
未命名的容器與畫面外的元素預設不列出；`snapshot --all` 列出完整樹，`--depth N` 限制深度，`--root <目標>` 只列子樹，
`--boxes` 附上元素座標，`--filename f.yml` 存成檔案。每個動作指令執行後都會自動存一份 snapshot（`.flaui-cli/snapshot-*.yml`，可用 `autoSnapshot` 關閉）。

`open` 也接受 Store App 的 AUMID（含 `!` 的值，例如 `Microsoft.WindowsCalculator_8wekyb3d8bbwe!App`）；
`attach` 可指定 PID 或程序名稱、`--title <視窗標題>`，或兩者併用。
`open --window` 對新出現的視窗採寬鬆比對（完全相同、開頭相同、包含），但啟動前就已開著的視窗只接受完全相同的標題，
所以像開著 `calculator.flow.yaml` 的編輯器這類無關的視窗不會被當成目標程式。

## 元素定位

| 寫法 | 說明 |
|---|---|
| `e21` | snapshot ref（同一元素在多次 snapshot 之間保持相同） |
| `id=num1Button` | AutomationId |
| `name="One"`、`One` | Name 完全相符 |
| `text=Disp` | Name 包含（不分大小寫） |
| `type=Button` | ControlType（button、edit、checkbox、combobox、listitem…） |
| `class=TextBox` | ClassName |
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
| 視窗 | `windows` `window` `focus` `maximize` `minimize` `restore` `resize` `move` `wait-window` `close-window` |
| 檢視 | `snapshot` `find` `inspect` |
| 動作 | `click` `dblclick` `hover` `fill` `type` `press` `select` `check` `uncheck` `expand` `collapse` `invoke` `scroll` `drag` |
| 讀取 / 等待 / 斷言 | `get` `wait` `assert` `sleep` |
| 截圖 | `screenshot` |
| 錄製 | `record start\|capture\|status\|stop` |
| 文件 | `doc start\|step\|status\|stop` |
| 腳本 | `run` |
| 其他 | `install-skill` |

全域選項：`-s, --session <名稱>`（多個 session 並行；未指定時用環境變數 `FLAUI_CLI_SESSION`，再沒有則為 `default`）、
`--json`（結構化輸出）、`--verbose`（在結果最後附上時間分解：尋找元素、動作本身、自動 snapshot、daemon 內的總時間，以及含往返的
總時間）。元素相關指令可加 `--timeout <毫秒>`，動作指令可加 `--note <文字>` 作為文件的步驟說明。
`close` 會關閉視窗並結束該 session 的 daemon；`close --keep-app` 只結束 session，保留應用程式繼續執行。
`close-window [索引|ref|標題]` 只關閉單一視窗或對話框（優先用 WindowPattern，否則送 `WM_CLOSE`，沒有取消按鈕的對話框也能關），
session 會繼續；關掉的是目前視窗時，會切回在它之前的目前視窗（例如開出對話框的 MDI 子視窗）。`windows` 列出可以切換的視窗，
不會列出 tooltip 與沒有名稱的 popup。
`wait-window <標題>` 依序比對完全相同、以該文字開頭、包含該文字的標題；`--exact` 只接受完全相同的標題，
`--regex` 把標題當成正規表示式（適合 MDI 子視窗，因為 MDI 主視窗的標題也會包含子視窗標題）。
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

- 最上層欄位：`name`、`app`、`timeout`（每個步驟的預設逾時毫秒數；啟動程式至少等待 `timeouts.launch`）、
  `highlight`（`padding` / `outline` / `badge`，套用到這份腳本的所有截圖，覆蓋設定檔的 `highlight`；步驟自己的
  `highlight-padding` / `no-outline` / `badge-position` 優先）、`steps`。
- `app`：`launch`（exe 或 AUMID）、`args`、`window`、`attach`（PID 或程序名稱）、`close`（結束後是否關閉程式；
  `launch` 預設為 `true`、`attach` 預設為 `false`）。直接寫字串等同 `launch`。
  啟動的程式在 script 失敗時也會關閉，一直沒出現視窗時會被終止。設定 `close: false` 時程式會繼續執行，並保有它繼承的標準輸出入，
  所以用 pipe 讀取 `run` 輸出的呼叫端會等到程式結束。
- 每個步驟是一個指令：純量值是第一個參數（`- click: id=ok`），mapping 則是具名參數與選項
  （`- fill: { target: id=name, text: Alice }`），`doc:` 是操作文件中的步驟說明。
  `run`、`list`、`install-skill` 等本機指令不能用在腳本中。

```bash
flaui-cli run samples/calculator.flow.yaml                                 # console 報告
flaui-cli run tests/*.flow.yaml --reporter junit --output out/report.xml    # CI
flaui-cli run tests/*.flow.yaml --bail                                     # 第一個腳本失敗就停止
flaui-cli run samples/calculator.flow.yaml --doc out/calc-doc              # 同時產生操作文件（--doc-format md,html）
```

`run` 在自己的行程中操作應用程式，不需要事先啟動 session。腳本失敗時會存一張失敗截圖，任何腳本失敗時結束碼為 `1`。

### 錄製

```bash
flaui-cli record start --name 登入         # 記錄之後執行的 CLI 指令（ref 自動轉成穩定 selector）
...
flaui-cli record stop --out login.flow.yaml

flaui-cli record capture --name 登入       # 捕捉目前視窗中真人的滑鼠鍵盤操作
flaui-cli record status
flaui-cli record stop --out login.flow.yaml   # 或在程式中按 Ctrl+Shift+Q 停止捕捉，再執行 record stop
```

`record capture` 會啟動 `flaui-cli-record.exe`，它必須與 `flaui-cli.exe` 放在同一個資料夾。捕捉期間送往其他程式的輸入會被忽略，
只會錄下目標程式中的步驟。

## 操作文件

```bash
flaui-cli doc start --title "小算盤使用說明"
flaui-cli click id=num1Button --note "按下數字 1"            # 每個動作都會截圖，並以編號紅框標註操作的元素
flaui-cli doc step "完成" --highlight id=CalculatorResults   # 手動加入步驟；--highlight 可重複
flaui-cli doc status
flaui-cli doc stop --out docs/calculator --format md,html    # index.md + images/ 與單檔 index.html
```

自動產生的步驟說明是英文（例如 `Click the "One" button`）；用 `--note` 或腳本的 `doc:` 可以寫任何語言。
大部分動作在執行前截圖；`fill`、`type`、`select`、`check`、`uncheck` 則在執行後截圖，讓畫面顯示輸入的值。

## 設定檔

`.flaui-cli/config.json`，放在目前工作目錄。所有欄位都可以省略，下面的值就是預設值。

```json
{
  "timeouts": { "action": 5000, "launch": 20000 },
  "outputDir": ".flaui-cli",
  "autoSnapshot": true,
  "snapshot": { "depth": 0, "includeOffscreen": false },
  "highlight": { "padding": 3, "outline": true, "badge": "auto" },
  "daemonIdleMinutes": 30
}
```

| 欄位 | 預設 | 說明 |
|---|---|---|
| `timeouts.action` | `5000` | 等待元素的毫秒數（selector、`wait`、`assert`）。單一指令用 `--timeout`、腳本用 `timeout:` 覆寫 |
| `timeouts.launch` | `20000` | `open` 等待應用程式視窗出現的毫秒數 |
| `outputDir` | `.flaui-cli` | 自動 snapshot、未指定路徑的截圖與文件放置的資料夾；相對於工作目錄，也可以是絕對路徑 |
| `autoSnapshot` | `true` | 每個動作指令後自動存一份 snapshot。大型視窗建議關閉 |
| `snapshot.depth` | `0` | `snapshot` 與自動 snapshot 的預設深度，`0` 為不限。`snapshot --depth` 可覆寫 |
| `snapshot.includeOffscreen` | `false` | snapshot 是否包含畫面外的元素（同 `snapshot --all`） |
| `highlight.padding` | `3` | 元素與紅框之間的像素：正數在元素外、`0` 貼齊邊緣、負數在元素內 |
| `highlight.outline` | `true` | 設為 `false` 時不畫紅框，只畫編號 |
| `highlight.badge` | `auto` | 編號的位置：`auto`（逐一自動決定）、`top`、`bottom`、`left`、`right`、`top-left`、`top-right`、`bottom-left`、`bottom-right` 或 `inside`（紅框內左上角） |
| `daemonIdleMinutes` | `30` | 工作階段的 daemon 閒置多少分鐘後結束 |

- 每個指令都會重新讀取設定檔，修改後立即生效；只有 `daemonIdleMinutes` 是在工作階段的 daemon 啟動時讀一次。
- 可以寫 `//` 註解、結尾多一個逗號，欄位名稱不分大小寫。檔案格式錯誤時指令會失敗並顯示檔案路徑，不會默默改用預設值。
- `highlight` 套用在 `screenshot --highlight`、`doc step --highlight` 以及 `doc` 為動作拍的截圖。標示的元素彼此相鄰（例如一排排
  按鈕）時，設成負數（如 `-3`）可以讓紅框分開。不畫紅框時，編號位置與有紅框時相同，並優先放在元素正上方或正下方的中間。
- `auto` 會讓編號貼著自己的元素，並避開其他編號。指定固定位置時，每個編號都放在紅框的同一側，即使因此落在相鄰元素旁邊
  （例如按鈕兩排排列時，第二排用 `top`）。
- 標註可能不易辨識時，`screenshot` 與 `doc step` 會在結果加上 `### Warnings`（`--json` 的 data 也有 `warnings`）：紅框互相交疊、
  編號蓋到其他標示的元素或其他編號、編號為了留在圖片內被移動。截圖照樣儲存，退出碼仍為 0。只檢查有標示的元素。
- 標示設定的優先順序：指令的 `--highlight-padding` / `--no-outline` / `--badge-position` > 腳本最上層的 `highlight` > 設定檔 > 預設值。

## 大型應用程式

每個指令都透過 UI Automation 在目前視窗與應用程式其他頂層視窗中尋找元素，元素數以千計的視窗（特別是 .NET Framework 的
WinForms 程式）會比較慢。加快的方法：

- 在設定檔設 `"autoSnapshot": false`，關閉每個動作後自動存的 snapshot；只用 selector 的腳本不需要它。
- 使用 `snapshot --depth N` 或 `snapshot --root <目標>`：只會讀取那一部分的樹。`find <文字>` 不會印出整棵樹。
- 優先使用 `id=`、`name=`、`class=`、`type=` 的 selector，找到第一個符合的就會停止；`text=`、`nth=`、`xpath=` 需要列出所有符合的元素。
- 用 `>>` 縮小範圍（例如 `id=toolStrip1 >> name=Query`），並關掉用不到的視窗（`close-window`）。
- 尋找元素超過預設 5 秒時，調高 `--timeout`（或 `timeouts.action`）。
- 加上 `--verbose` 可以看到時間花在哪裡（`find` / `action` / `snapshot`）；回報指令太慢時請附上它的輸出。

UIA3 的子孫搜尋在某些 provider（例如 WinForms）走訪約 512 個元素後就會停止，而且不會回報錯誤。flaui-cli 會保留 UIA 找到的
結果並補上其餘部分：視窗透過列舉子視窗與擁有的視窗取得，其他 selector 在命令的 `--timeout` 內逐層往下找，`snapshot` 只重讀被
截斷的部分（最多 10 秒）。每個動作後自動存的 snapshot 不會補完，以維持動作速度。在這種視窗中找不到任何符合的 selector 時，會一直
找到逾時為止；用 `>>` 從靠近目標的容器開始找，可以讓搜尋維持在小範圍。大型視窗深處的元素來不及找到時，請調高 `--timeout`。
以上都只透過 UI Automation 與視窗列舉，flaui-cli 不會讀寫受測程式的記憶體。

## 架構

```
src/
  FlauiCli/                CLI 前端（System.CommandLine）、daemon client、本機指令
  FlauiCli.Core/           自動化引擎：抽象介面、selector、snapshot、dispatcher、腳本、錄製、文件、daemon
  FlauiCli.Driver.FlaUI/   唯一引用 FlaUI 的轉接層（實作 IUiDriver / IUiElement）
  FlauiCli.Recorder/       flaui-cli-record.exe：真人操作錄製工具，唯一宣告 hook API 的專案
tests/
  FlauiCli.Core.Tests/     單元測試（FakeDriver，不需要桌面）
  FlauiCli.E2E.Tests/      DriverContractTests（FlaUI 轉接層契約）+ CLI 端對端測試
  TestApps/WpfSample/      測試用 WPF 程式
  TestApps/WinFormsLargeTree/  超過 512 個 UIA 元素的 WinForms 視窗（UIA3 搜尋上限）
```

- CLI 每次執行只是 client，第一次 `open` / `attach` 時會以同一個 exe 的 daemon 模式啟動背景程序，透過 Named Pipe（僅限目前使用者）通訊；所有 UI Automation 呼叫都在 daemon 的單一執行緒上執行。
- **FlaUI 隔離**：Core 不引用 FlaUI，只依賴 `IUiDriver` / `IUiElement` 等抽象與自有列舉。FlaUI 版本在 `Directory.Packages.props` 精確鎖定；升級時必須通過 `DriverContractTests`（包含列舉對應完整性檢查）。

## 值得知道的行為（安全性與防毒）

flaui-cli 的某些行為會被安全軟體特別留意，以下都是刻意的設計：

- **模擬輸入與截圖**：動作會送出真實的滑鼠鍵盤輸入（`SendInput`），截圖會擷取螢幕。
- **全域鍵盤滑鼠 hook**：放在獨立的執行檔 `flaui-cli-record.exe`，**只有**在執行 `record capture` 期間由 daemon 以一般子行程啟動，結束擷取時移除 hook 並結束行程。`flaui-cli.exe` 本身從不安裝 hook，程式碼裡也沒有宣告 hook API，所以即使錄製工具被擋或被刪除，平常的自動化仍可正常使用（只有 `record capture` 會失敗並顯示明確訊息）。這些 API 以一般靜態匯入公開宣告，不做動態載入或混淆。若安全軟體需要設定例外，只需針對 `flaui-cli-record.exe`。
- **不存取其他程式的記憶體**：flaui-cli 只透過 UI Automation 讀取應用程式，並以列舉視窗的方式找視窗；從不開啟其他行程的記憶體、
  注入程式碼或建立遠端執行緒。若 `flaui-cli.exe` 匯入這類 API，每次 CI 都會跑的測試 `NativeImportTests` 會失敗。
- **終止行程**：只會終止 flaui-cli 自己啟動的程式：`close` 時程式 3 秒內沒有結束，以及 `run` 啟動的程式一直沒出現視窗。
  attach 上去的程式不會被終止。
- **背景 daemon**：就是同一個 `flaui-cli.exe`，以 `flaui-cli.exe daemon --session <名稱>` 執行、不開主控台視窗。只有在呼叫端的 Job 會連帶結束它時才脫離 Job；閒置 30 分鐘自動結束（`close` / `kill-all` 可立即結束）。
- **密碼不會出現在輸出中**：用 `fill` 輸入密碼欄位的文字，在指令結果、錄製的腳本與產生的文件中都會以 `********` 取代；
  `record capture` 遇到密碼欄位也會寫入同樣的佔位字（執行腳本前請換成真正的值）。daemon 記錄檔完全不保存 `fill` / `type` 的文字。
- **本機檔案**：session 檔與記錄檔在 `%LOCALAPPDATA%\flaui-cli`，snapshot 與截圖在工作目錄的 `.flaui-cli`。
- 執行檔不要求提權（`asInvoker`），發佈版不壓縮、不加殼。

## 測試

```bash
dotnet test tests/FlauiCli.Core.Tests                                   # 單元測試
dotnet test tests/FlauiCli.E2E.Tests                                    # 需要互動式桌面，執行期間請勿操作滑鼠鍵盤
```

## 已知限制

- 動作會移動真實滑鼠、送出真實按鍵，執行期間請勿操作電腦（`click --invoke` 可避免移動滑鼠）。
- `select` 透過 UI Automation 變更選取，不會觸發應用程式自己的選取事件：WinForms 下拉選單畫面上顯示新項目，但
  `SelectedIndexChanged` 不會執行。`select --keyboard` 改用方向鍵選取，應用程式的反應與真人操作相同（途中經過的項目也會觸發事件）。
  選取已經選取的項目，兩種方式都不會觸發事件。
- WinForms 程式中，若控制項開啟了強制回應（modal）對話框，InvokePattern 會等到對話框關閉才返回：`click --invoke` / `invoke` 會在 `--timeout` 後失敗，對話框關閉前只有 `status`、`sleep`、`press <按鍵>` 可用。會開對話框的按鈕請用一般 `click`。
- UWP 程式的視窗由 ApplicationFrameHost 承載，`open` 需要 `--window` 指定標題；`close` 只會關閉視窗，不會結束共用程序。
- WinForms `ListView`（Details 檢視、未開 `FullRowSelect`）的 `listitem` 元素範圍是整列，`click` 會點到文字右邊的空白處而選不到項目。
  請改用 `select <清單> <項目>`，或點列中的文字元素（`click "id=lvItems >> type=text&&name=BRAVO"`）。
- 真人操作錄製依賴低階 hook，以滑鼠按下時的座標判斷元素，快速變動的 UI（例如動畫中的選單）可能錄到非預期元素。

## 授權

[MIT](LICENSE)。第三方元件與其授權列於 [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt)。
