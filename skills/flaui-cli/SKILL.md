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
| `class=TextBox` | ClassName |
| `type=Button&&name=OK` | Combined conditions (type is the ControlType: button, edit, checkbox, combobox...) |
| `id=panel >> name=OK` | Search inside the previous match |
| `type=ListItem&&nth=2` | Third match |
| `xpath=//Button[@AutomationId='ok']` | FlaUI XPath |

Selectors wait for the element to appear (5 s by default, change with `--timeout`); assertions retry until the timeout too.

## Common commands

| Area | Commands |
|---|---|
| Application | `open <exe\|AUMID> [args] [--window title]`, `attach [pid\|name] [--title title]`, `close [--keep-app]`, `status`, `list`, `close-all`, `kill-all`, `lock-status` |
| Windows | `windows`, `window <index\|ref\|title>`, `focus [t]`, `wait-window <title> [--exact\|--regex] [--no-switch]`, `close-window [index\|ref\|title]`, `maximize`, `minimize`, `restore`, `resize W H`, `move X Y` |
| Inspection | `snapshot`, `find <text> [--regex]`, `inspect <target>` (all properties, patterns and a suggested selector) |
| Actions | `click <t> [--button right] [--double] [--invoke]`, `dblclick`, `hover`, `fill <t> <text> [--keyboard]`, `type <text>`, `press <keys> [--target t] [--repeat n]` (`Enter`, `Ctrl+S`), `select <t> <item or #index> [--keyboard]`, `check`, `uncheck`, `expand`, `collapse`, `invoke`, `scroll <t> [--dir down] [--amount 3]`, `drag <src> <dst>` |
| Reading | `get text\|value\|name\|state\|rect <t>`, `get prop <t> HelpText` |
| Waiting / asserting | `wait <t> --state visible\|hidden\|exists\|gone\|enabled\|disabled`, `assert exists\|not-exists\|visible\|hidden\|enabled\|disabled\|checked\|unchecked <t>`, `assert text\|contains\|matches\|value <t> <expected>`, `sleep <ms>` |
| Screenshots | `screenshot [t] [--filename f.png] [--highlight t ...] [--highlight-padding px] [--no-outline] [--badge-position p] [--screen]` |

Element commands take `--timeout <ms>`; action commands take `--note <text>` (the step description in documents).
`close` closes the window and stops the session's daemon; `close --keep-app` ends the session and leaves the app running.
`open` treats a value containing `!` as a Store app AUMID (for example `Microsoft.WindowsCalculator_8wekyb3d8bbwe!App`).

Exit codes: 0 success, 1 assertion failed, 2 error, 3 session not running, 4 workstation locked (mouse/keyboard input and
screenshots impossible; reading the UI and `click --invoke` still work). When actions fail or seem to do nothing, run
`flaui-cli lock-status` (no session needed; exit 0 = unlocked, 4 = locked) before looking for other causes; while locked, ask the
user to unlock and retry. Add `--json` for structured output and
`--verbose` for a timing breakdown (`find` / `action` / `snapshot` / `total`) at the end of the result.
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
- `daemonIdleMinutes`: idle minutes before a session's daemon exits (read when the daemon starts).

## Notes

- The window of a UWP app (Calculator etc.) belongs to ApplicationFrameHost, so `open` always needs `--window`.
- Actions move the real mouse; do not use the computer while they run. Use `click --invoke` to avoid moving the mouse.
- On WinForms apps, InvokePattern does not return while a modal dialog opened by the control is shown. `click --invoke` /
  `invoke` then fail after `--timeout` (exit code 2), and until the dialog closes every command except `status`, `sleep` and
  `press <keys>` (without `--target`) fails immediately, because UI Automation calls to the app would wait. Close the dialog
  with `press Escape` / `press Enter`. Use a plain `click` (without `--invoke`) for buttons that open a dialog.
- `fill` on a password field is masked: the value is replaced with `********` in the command result, recorded scripts, generated
  documents and the daemon log, so never expect a real password to appear in the output. Scripts produced by `record capture`
  contain the same placeholder for password fields; replace it with the real value before running them.
- `fill` uses ValuePattern when possible; add `--keyboard` when the app only reacts to real key presses.
- `select` goes through UI Automation, which changes the value without the app's selection events: a WinForms combo box shows
  the new item but `SelectedIndexChanged` never runs, so later steps fail ("please select an item"). Use `select <t> <item>
  --keyboard` there: it presses Up/Down from the current item (every item passed on the way raises the event too, as for a
  person). Selecting the item that is already selected raises no event in either mode.
- Applications often move the focus while handling a key (a WinForms drop-down jumping to the next field from
  `SelectedIndexChanged`), so a second `press` without `--target` lands elsewhere; `press` then says where the focus went.
  Use `press Down --target <t> --repeat 3`: the target is focused again before every press.
- Use `inspect <target>` to get a suggested stable selector for an element found in a snapshot.
- When an element cannot be found, check with `snapshot` or `find <text>`; popups such as drop-downs and menus also appear in snapshots.
- `wait-window` prefers an exact title, then one starting with the text, then one containing it. In MDI apps the parent's title
  contains the child's title, so use `--exact` or `--regex "^Child title"` when the child's title varies.
- `close-window [title]` closes one window or dialog (also dialogs without a Cancel button, where `press Escape` does nothing)
  and keeps the session; closing the current window switches back to the window that was current before it (the MDI
  child a dialog came from). `close` ends the whole session.
- `select` on a combo box checks that the value changed and otherwise sets it through ValuePattern; if it still fails, use `fill`.
- WinForms ListView rows: the `listitem` box spans the whole row, so `click` may hit empty space. Use `select <list> <item>` or
  click the text inside the row (`id=lvItems >> type=text&&name=BRAVO`).
- Large windows (hundreds of controls) make every lookup slow. Set `"autoSnapshot": false` in `.flaui-cli/config.json`, use
  `snapshot --depth N` / `--root <t>`, prefer `id=` / `name=` / `type=` selectors (they stop at the first match), scope with `>>`,
  close unused windows and raise `--timeout`. Past about 512 elements UIA3 truncates searches silently; flaui-cli fills in
  the rest level by level within `--timeout`, so a selector that matches nothing waits out the timeout and a deep one may
  need a larger timeout: scope it from a nearby container. Add `--verbose` to see whether the time goes into finding the
  element, the action or the automatic snapshot.
- A snapshot of a minimized window has no content; run `restore` first.
