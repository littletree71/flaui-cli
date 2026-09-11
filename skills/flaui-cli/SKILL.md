---
name: flaui-cli
description: 以 flaui-cli 操作 Windows 桌面應用程式（WPF / WinForms / Win32 / UWP）：開啟程式、取得 UI 樹快照、點擊、輸入、斷言、截圖、錄製 YAML 測試腳本、產生附截圖的操作文件。當使用者要求自動化、測試或記錄 Windows 桌面程式操作時使用。
allowed-tools: Bash(flaui-cli:*)
---

# flaui-cli：Windows 桌面 UI 自動化

`flaui-cli` 是仿 playwright-cli 的命令列工具。第一次 `open` / `attach` 時會自動啟動背景 daemon，
之後每個指令都在同一個 session 中執行，元素 ref（`e12`）會在指令之間保留。

## 基本流程

```bash
flaui-cli open calc.exe --window Calculator   # 啟動並附加（UWP / 啟動器程式必須給 --window 標題）
flaui-cli snapshot                            # 取得 UI 樹與 ref
flaui-cli click e21                           # 用 ref 操作
flaui-cli click id=num1Button                 # 或用 selector
flaui-cli assert text id=CalculatorResults "Display is 1"
flaui-cli screenshot --highlight id=CalculatorResults
flaui-cli close
```

動作指令執行後會輸出 `### Snapshot` 檔案連結（`.flaui-cli/snapshot-*.yml`）；需要最新 ref 時讀那個檔或再執行 `snapshot`。

## Snapshot 格式

```yaml
- window "Calculator" [ref=e1]
  - button "One" [ref=e21] id=num1Button
  - edit "Name" [ref=e30] id=nameInput value="Alice" [focused]
  - checkbox "I agree" [ref=e31] [checked]
```

- `id=` 是 AutomationId，最適合寫進腳本。
- 狀態標記：`[disabled]` `[checked]` `[expanded]` `[collapsed]` `[selected]` `[focused]`。
- 無名稱的容器會被省略；需要完整樹時用 `snapshot --all`，限制深度用 `--depth N`，只看子樹用 `--root e5`。

## 元素定位（target）

| 寫法 | 說明 |
|---|---|
| `e21` | snapshot ref（同一元素在多次 snapshot 間保持相同） |
| `id=num1Button` | AutomationId |
| `name="One"` 或直接寫 `One` | Name 完全相符 |
| `text=Disp` | Name 包含 |
| `type=Button&&name=OK` | 條件組合（type 為 ControlType：button、edit、checkbox、combobox…） |
| `id=panel >> name=OK` | 在前者之內尋找 |
| `type=ListItem&&nth=2` | 第 3 個符合者 |
| `xpath=//Button[@AutomationId='ok']` | FlaUI XPath |

selector 會自動等待元素出現（預設 5 秒，`--timeout` 可調）；斷言也會自動重試直到逾時。

## 常用指令

| 類別 | 指令 |
|---|---|
| 應用程式 | `open <exe> [args] [--window 標題]`、`attach <pid\|名稱> [--title 標題]`、`close`、`status`、`list` |
| 視窗 | `windows`、`window <索引\|標題>`、`wait-window <標題>`、`maximize`、`minimize`、`restore`、`resize W H`、`move X Y` |
| 檢視 | `snapshot`、`find <文字> [--regex]`、`inspect <target>` |
| 動作 | `click <t> [--button right] [--double] [--invoke]`、`dblclick`、`hover`、`fill <t> <文字>`、`type <文字>`、`press <按鍵>`（`Enter`、`Ctrl+S`）、`select <t> <項目或 #索引>`、`check`、`uncheck`、`expand`、`collapse`、`invoke`、`scroll <t> --dir down`、`drag <src> <dst>` |
| 讀取 | `get text\|value\|name\|state\|rect <t>`、`get prop <t> HelpText` |
| 等待 / 斷言 | `wait <t> --state visible\|hidden\|enabled\|gone`、`assert exists\|not-exists\|visible\|hidden\|enabled\|disabled\|checked\|unchecked <t>`、`assert text\|contains\|matches\|value <t> <預期>`、`sleep <ms>` |
| 截圖 | `screenshot [t] [--filename f.png] [--highlight t ...] [--screen]` |

結束碼：0 成功、1 斷言失敗、2 錯誤、3 session 未啟動。加 `--json` 可取得結構化輸出。
多個 session 用 `-s <名稱>` 區分（或環境變數 `FLAUI_CLI_SESSION`）。

## 產生 YAML 測試腳本

```bash
flaui-cli record start --name 登入流程     # 之後的操作指令都會被記錄（ref 自動轉成穩定 selector）
flaui-cli click id=submitButton
flaui-cli record stop --out tests/login.flow.yaml
flaui-cli run tests/login.flow.yaml --reporter junit --output out/report.xml
```

真人操作錄製：`flaui-cli record capture --out flow.yaml`（使用者直接操作程式，Ctrl+Shift+Q 結束）。

腳本格式：

```yaml
name: 小算盤加法
app: { launch: calc.exe, window: Calculator }
steps:
  - click: id=num1Button
    doc: 按下數字 1          # 操作文件的說明
  - fill: { target: id=nameInput, text: Alice }
  - press: Enter
  - assert: { target: id=CalculatorResults, text: Display is 1 }
```

## 產生操作文件（附截圖）

```bash
flaui-cli doc start --title "如何使用小算盤"
flaui-cli click id=num1Button --note "按下數字 1"   # 每個動作自動截圖並以紅框 + 編號標註目標
flaui-cli doc step "完成，結果顯示為 1"              # 手動加入步驟
flaui-cli doc stop --out docs/calculator             # 產生 index.md + images/ 與單檔 index.html
```

也可以直接由腳本產生：`flaui-cli run flow.yaml --doc docs/flow`。

## 注意事項

- UWP 程式（小算盤等）的視窗屬於 ApplicationFrameHost，`open` 時一定要給 `--window`。
- 動作會移動真實滑鼠，執行期間請勿操作電腦；需要不動滑鼠時用 `click --invoke`。
- 找不到元素時先 `snapshot` 或 `find <文字>` 確認，下拉選單、選單等 popup 也會出現在 snapshot 中。
