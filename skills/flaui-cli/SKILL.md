---
name: flaui-cli
description: Automate Windows desktop applications (WPF / WinForms / Win32 / UWP) with flaui-cli - open apps, take UI tree snapshots, click, type, assert, take screenshots, record YAML test scripts and generate step-by-step documents with annotated screenshots. Use when the user asks to automate, test or document operations in a Windows desktop program.
allowed-tools: Bash(flaui-cli:*)
---

# flaui-cli: Windows desktop UI automation

`flaui-cli` is a command-line tool modelled on playwright-cli. The first `open` / `attach` starts a background
daemon automatically; every later command runs in the same session and element refs (`e12`) stay valid between commands.

## Basic flow

```bash
flaui-cli open calc.exe --window Calculator   # launch and attach (UWP apps / launchers need --window <title>)
flaui-cli snapshot                            # UI tree with refs
flaui-cli click e21                           # act on a ref
flaui-cli click id=num1Button                 # or on a selector
flaui-cli assert text id=CalculatorResults "Display is 1"
flaui-cli screenshot --highlight id=CalculatorResults
flaui-cli close
```

Action commands print a `### Snapshot` link to a file (`.flaui-cli/snapshot-*.yml`); read it or run `snapshot` again when you need fresh refs.

## Snapshot format

```yaml
- window "Calculator" [ref=e1]
  - button "One" [ref=e21] id=num1Button
  - edit "Name" [ref=e30] id=nameInput value="Alice" [focused]
  - checkbox "I agree" [ref=e31] [checked]
```

- `id=` is the AutomationId, the best choice for scripts.
- State markers: `[disabled]` `[checked]` `[mixed]` `[expanded]` `[collapsed]` `[selected]` `[focused]`.
- Unnamed containers and off-screen elements are omitted; use `snapshot --all` for the full tree, `--depth N` to limit depth,
  `--root e5` for a subtree, `--boxes` to add bounds (`[box=x,y,w,h]`) and `--filename f.yml` to save to a file.

## Targeting elements

| Syntax | Meaning |
|---|---|
| `e21` | Snapshot ref (the same element keeps its ref across snapshots) |
| `id=num1Button` | AutomationId |
| `name="One"` or just `One` | Exact name |
| `text=Disp` | Name contains (case-insensitive) |
| `regex="^\s*Row 0$"` | Name matches a regular expression (case-insensitive); quote it when it has spaces |
| `class=TextBox` | ClassName |
| `type=Button&&name=OK` | Combined conditions (type is the ControlType: button, edit, checkbox, combobox...) |
| `id=panel >> name=OK` | Search inside the previous match |
| `type=ListItem&&nth=2` | Third match |
| `xpath=//Button[@AutomationId='ok']` | FlaUI XPath |

Unquoted values are trimmed: for names with leading / trailing spaces (snapshot shows `dataitem " Row 0"`, typical of DataGridView
check box cells without header text) use `name=" Row 0"`, `text=` or `regex=` instead of parsing snapshot text for a ref.
Selectors wait for the element to appear (5 s by default, change with `--timeout`); assertions retry until the timeout too.

## Common commands

| Area | Commands |
|---|---|
| Application | `open <exe\|AUMID> [args] [--window title]`, `attach [pid\|name] [--title title]`, `close [--keep-app]`, `status`, `list`, `close-all`, `kill-all`, `lock-status` |
| Windows | `windows`, `window <index\|ref\|title>`, `focus [t]`, `wait-window <title> [--exact\|--regex] [--no-switch]`, `close-window [index\|ref\|title]`, `maximize`, `minimize`, `restore`, `resize W H`, `move X Y` |
| Inspection | `snapshot`, `find <text> [--regex]`, `inspect <target>` (all properties, patterns and a suggested selector) |
| Actions | `click <t> [--button right] [--double] [--invoke]`, `dblclick`, `hover`, `fill <t> <text> [--keyboard]`, `type <text>`, `press <keys> [--target t] [--repeat n]` (`Enter`, `Ctrl+S`), `select <t> <item or #index> [--keyboard] [--click]`, `check`, `uncheck`, `expand`, `collapse`, `invoke`, `scroll <t> [--dir down] [--amount 3]`, `drag <src> <dst>` |
| Reading | `get text\|value\|name\|state\|rect <t>`, `get prop <t> HelpText` |
| Waiting / asserting | `wait <t> --state visible\|hidden\|exists\|gone\|enabled\|disabled`, `assert exists\|not-exists\|visible\|hidden\|enabled\|disabled\|checked\|unchecked <t>`, `assert text\|contains\|matches\|value <t> <expected>`, `sleep <ms>` |
| Screenshots | `screenshot [t] [--filename f.png] [--highlight t ...] [--highlight-padding px] [--no-outline] [--badge-position p] [--mask t ...] [--mask-column header ...] [--screen]` |

Element commands take `--timeout <ms>`; action commands take `--note <text>` (the step description in documents).
`close` closes the window and stops the session's daemon; `close --keep-app` ends the session and leaves the app running.
`open` treats a value containing `!` as a Store app AUMID (for example `Microsoft.WindowsCalculator_8wekyb3d8bbwe!App`).

Exit codes: 0 success, 1 assertion failed, 2 error, 3 session not running, 4 workstation locked (mouse/keyboard input and
screenshots impossible; reading the UI and `click --invoke` still work). When actions fail or seem to do nothing, run
`flaui-cli lock-status` (no session needed; exit 0 = unlocked, 4 = locked) before looking for other causes; while locked, ask the
user to unlock and retry. Add `--json` for structured output and
`--verbose` for a timing breakdown (`find` / `action` / `snapshot` / `total`) at the end of the result.
`--log-level error|warn|info|debug` (or `logLevel` in the config file) sets how much is printed besides the result: `warn` drops
the window and snapshot link added after every action, which saves tokens in long runs; `debug` is the same as `--verbose`.
Use `-s <name>` (or the `FLAUI_CLI_SESSION` environment variable) to run several sessions.

## Producing YAML test scripts

```bash
flaui-cli record start --name Login       # every later action command is recorded (refs become stable selectors)
flaui-cli click id=submitButton
flaui-cli record stop --out tests/login.flow.yaml
flaui-cli run tests/login.flow.yaml --reporter junit --output out/report.xml
```

Recording real user input: `flaui-cli record capture` starts capturing in the current window and the user operates the app;
Ctrl+Shift+Q stops capturing, then `flaui-cli record stop --out flow.yaml` writes the script (`record status` shows progress).
This one command runs a separate executable, `flaui-cli-record.exe`, which must sit next to `flaui-cli.exe`; if it is missing or
blocked by security software, only `record capture` fails and everything else keeps working. Input sent to other applications
while the capture runs is ignored, so only steps in the target app are recorded.

Script format:

```yaml
name: Calculator addition
app: { launch: calc.exe, window: Calculator }
steps:
  - click: id=num1Button
    doc: Press 1              # description used in generated documents
  - fill: { target: id=nameInput, text: Alice }
  - press: Enter
  - assert: { target: id=CalculatorResults, text: Display is 1 }
```

- Top-level fields: `name`, `app`, `timeout` (default ms per step; launching waits at least `timeouts.launch`, 20 s by default),
  `highlight` (`{ padding: -3, outline: false, badge: top }`: outline distance in px, whether to draw outlines and where badges go,
  for every screenshot of the script including `run --doc`; overrides the config file, a step's `highlight-padding` /
  `no-outline` / `badge-position` overrides it), `steps`.
- `app`: `launch` (exe or AUMID), `args`, `window`, `attach` (PID or process name), `close` (default `true` for launch, `false` for attach).
  A launched app is closed even when the script fails, and terminated when its window never appeared.
- A scalar step value is the command's first argument; a mapping gives named arguments and options. Local commands (`run`, `list`...) are not allowed.
- `run a.flow.yaml b.flow.yaml [--reporter junit --output report.xml] [--bail] [--doc out/docs --doc-format md,html]` runs in its
  own process (no session needed), saves a screenshot when a script fails and exits with 1 if any script fails.

## Producing documents with screenshots

```bash
flaui-cli doc start --title "Using the calculator"
flaui-cli click id=num1Button --note "Press 1"   # each action is captured and its target outlined with a numbered red box
flaui-cli doc step "Done, the display shows 1" --highlight id=CalculatorResults   # add a manual step (--highlight optional, repeatable)
flaui-cli doc stop --out docs/calculator         # writes index.md + images/ and a single-file index.html (--format md,html)
```

`doc status` shows the document being recorded. Generated step descriptions are in English; `--note` accepts any language.
Most actions are captured before they run (showing what to operate); `fill`, `type`, `select`, `check` and `uncheck` are
captured after they run, so the screenshot shows the entered value. Tooltips left over from an earlier `hover` are hidden first.

To hide names or other private data, add `--mask <t>` (an element) or `--mask-column <column header>` (every visible cell of a
table column, for example `--mask-column "id=dataGridView1 >> name=Partner"`) to `screenshot` / `doc step`, or to `doc start` to
cover them in every screenshot of the document. A solid box is drawn before outlines and badges. `screenshot` / `doc step` fail
without saving when a mask is not found; do not post-process images to hide data.

Scripts can produce documents directly: `flaui-cli run flow.yaml --doc docs/flow`.

## Configuration

`.flaui-cli/config.json` in the working directory (every field optional, re-read for every command):

```json
{
  "timeouts": { "action": 5000, "launch": 20000, "unlock": 0 },
  "outputDir": ".flaui-cli",
  "autoSnapshot": true,
  "snapshot": { "depth": 0, "includeOffscreen": false },
  "highlight": { "padding": 3, "outline": true, "badge": "auto" },
  "logLevel": "info",
  "daemonIdleMinutes": 30
}
```

- `timeouts.action`: element wait in ms (`--timeout` per command). `timeouts.launch`: how long `open` waits for the window.
  `timeouts.unlock`: how long input / screenshot commands wait for a locked workstation to be unlocked (0 = fail with exit 4).
- `outputDir`: where automatic snapshots and unnamed screenshots go. `autoSnapshot: false` speeds up large windows.
- `snapshot.depth` / `snapshot.includeOffscreen`: defaults for `snapshot` and the automatic snapshot.
- `highlight.padding`: px between element and outline (negative = inside, keeps touching elements' outlines apart);
  `highlight.outline: false` draws only number badges. `highlight.badge`: `auto` (per element, avoids neighbours) or a fixed
  side for every badge: `top|bottom|left|right|top-left|top-right|bottom-left|bottom-right|inside`. Overridden by a script's
  `highlight` and a command's `--highlight-padding` / `--no-outline` / `--badge-position`.
  `screenshot` / `doc step` report hard-to-read annotations under `### Warnings` (overlapping outlines, badges covering other
  highlights); the screenshot is still saved, so follow the suggested option and take it again if needed.
- `logLevel`: `error` / `warn` / `info` (default) / `debug`, like `--log-level`; `--json` data is complete at every level.
- `daemonIdleMinutes`: idle minutes before a session's daemon exits (read when the daemon starts).

## Notes

### Windows and sessions

- UWP apps (Calculator etc.): you can open them with `open calc.exe --window Calculator`.
  Why: `calc.exe` hands off to the app and exits, so `open` cannot find a window without `--window`. Opening by AUMID works
  without it, but takes the inner window, where `minimize` / `maximize` do not work.
- If the current window is minimized, mouse and keyboard actions fail; you can run `restore` first.
  Why: its controls are off screen, so the input would miss. `click --invoke` and `fill` through ValuePattern still work.
- Some minimized windows (UWP apps) have no content in snapshots; `snapshot` then says so.
- If `wait-window` picks the MDI parent instead of the child, you can add `--exact` or `--regex "^Child title"`.
  Why: it prefers an exact title, then one starting with the text, then one containing it, and the parent's title contains
  the child's.
- To close one window or dialog and keep the session, you can use `close-window [title]`. `close` ends the whole session.
  Closing the current window switches back to the window that was current before it.

### Finding elements

- If an element cannot be found, you can look for it with `snapshot` or `find <text>`. Drop-downs and menus also appear in
  snapshots.
- To get a stable selector for an element from a snapshot, you can use `inspect <target>`.

### Input

- Actions move the real mouse; ask the user not to use the computer while they run. If you do not want the mouse to move, you
  can use `click --invoke`.
- If the app only reacts to real key presses, you can add `--keyboard` to `fill`.
  Why: `fill` uses ValuePattern when it can.
- Passwords never appear in the output: `fill` on a password field shows `********` in the result, recorded scripts, generated
  documents and the daemon log. If a script from `record capture` contains `********`, replace it with the real value before
  running it.

### WinForms

- Buttons that open a dialog: you can use a plain `click`, not `click --invoke`.
  Why: InvokePattern does not return while the dialog is shown. `click --invoke` then fails after `--timeout` (exit code 2),
  and until the dialog closes every command except `status`, `sleep` and `press <keys>` (without `--target`) fails at once,
  `close-window` too. To get out, you can close the dialog with `press Escape` or `press Enter`.
- If a dialog does not close on Escape (a form without a Cancel button), you can use `close-window <title>`.
- Combo boxes: if later steps act as if nothing was selected ("please select an item"), you can use
  `select <t> <item> --keyboard`.
  Why: plain `select` shows the new item but does not raise `SelectedIndexChanged`. `--keyboard` presses Up/Down from the
  current item like a person, so every item passed on the way raises the event too.
- If a form moves the focus while handling a key, you can use `press <keys> --target <t> --repeat N`.
  Why: a second `press` without `--target` lands in the new field; `press` says when this happens. With `--target`, the
  target is focused again before every press.
- ListView rows: you can use `click` on the `listitem`, or `select <list> <item> --click` with the first column's text as the
  item.
  Why: many apps load the row's details in the ListView's `Click` handler. Plain `select` highlights the row and `get state`
  says `selected`, but `Click` does not run. `--keyboard` does not raise `Click` either.
- If you click a ListView row yourself, you can click its first column.
  Why: in details view only the first column selects a row. Clicking the text of another column clears the selection.
- DataGridView check box cells: you can set them with `fill <cell> True` / `False` and check them with `assert value <cell> True`.
  Why: they have no TogglePattern, so `check`, `uncheck` and `assert checked` fail. Cell refs change between commands, so
  use a selector; cell names follow the system language (`" Row 0"`, `" 資料列 0"`).

### Large windows

- If every lookup is slow in a window with hundreds of controls, you can:
  - set `"autoSnapshot": false` in `.flaui-cli/config.json`
  - use `snapshot --depth N` or `--root <t>`
  - prefer `id=` / `name=` / `type=` selectors, which stop at the first match, and scope them with `>>`
  - close unused windows and raise `--timeout`
- To see whether the time goes into finding the element, the action or the automatic snapshot, you can add `--verbose`.
- If a selector waits out `--timeout` in a large window, you can scope it from a nearby container.
  Why: past about 512 elements UIA3 truncates searches silently, and flaui-cli fills in the rest level by level within
  `--timeout`.

## Tip: saving tokens with screenshots

If you want to save tokens:

- To check whether an action worked, you can ask with text commands (`get`, `assert`, `snapshot`) instead of taking a
  screenshot and looking at it.
- When a long task needs many screenshots looked at, you can have a subagent look at them and tell you what it sees, so the
  images never enter your own conversation.

Why: every screenshot you read stays in the conversation until the task ends, and with some providers, such as Claude, adding an
image means the whole conversation is processed again instead of coming from the cache.
