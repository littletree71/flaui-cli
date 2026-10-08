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
| Application | `open <exe\|AUMID> [args] [--window title]`, `attach [pid\|name] [--title title]`, `close [--keep-app]`, `status`, `list`, `close-all`, `kill-all` |
| Windows | `windows`, `window <index\|ref\|title>`, `focus [t]`, `wait-window <title> [--no-switch]`, `maximize`, `minimize`, `restore`, `resize W H`, `move X Y` |
| Inspection | `snapshot`, `find <text> [--regex]`, `inspect <target>` (all properties, patterns and a suggested selector) |
| Actions | `click <t> [--button right] [--double] [--invoke]`, `dblclick`, `hover`, `fill <t> <text> [--keyboard]`, `type <text>`, `press <keys> [--target t]` (`Enter`, `Ctrl+S`), `select <t> <item or #index>`, `check`, `uncheck`, `expand`, `collapse`, `invoke`, `scroll <t> [--dir down] [--amount 3]`, `drag <src> <dst>` |
| Reading | `get text\|value\|name\|state\|rect <t>`, `get prop <t> HelpText` |
| Waiting / asserting | `wait <t> --state visible\|hidden\|exists\|gone\|enabled\|disabled`, `assert exists\|not-exists\|visible\|hidden\|enabled\|disabled\|checked\|unchecked <t>`, `assert text\|contains\|matches\|value <t> <expected>`, `sleep <ms>` |
| Screenshots | `screenshot [t] [--filename f.png] [--highlight t ...] [--screen]` |

Element commands take `--timeout <ms>`; action commands take `--note <text>` (the step description in documents).
`close` closes the window and stops the session's daemon; `close --keep-app` ends the session and leaves the app running.
`open` treats a value containing `!` as a Store app AUMID (for example `Microsoft.WindowsCalculator_8wekyb3d8bbwe!App`).

Exit codes: 0 success, 1 assertion failed, 2 error, 3 session not running. Add `--json` for structured output.
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

- Top-level fields: `name`, `app`, `timeout` (default ms per step), `steps`.
- `app`: `launch` (exe or AUMID), `args`, `window`, `attach` (PID or process name), `close` (default `true` for launch, `false` for attach).
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

Scripts can produce documents directly: `flaui-cli run flow.yaml --doc docs/flow`.

## Notes

- The window of a UWP app (Calculator etc.) belongs to ApplicationFrameHost, so `open` always needs `--window`.
- Actions move the real mouse; do not use the computer while they run. Use `click --invoke` to avoid moving the mouse.
- `fill` on a password field is masked: the value is replaced with `********` in the command result, recorded scripts, generated
  documents and the daemon log, so never expect a real password to appear in the output. Scripts produced by `record capture`
  contain the same placeholder for password fields; replace it with the real value before running them.
- `fill` uses ValuePattern when possible; add `--keyboard` when the app only reacts to real key presses.
- Use `inspect <target>` to get a suggested stable selector for an element found in a snapshot.
- When an element cannot be found, check with `snapshot` or `find <text>`; popups such as drop-downs and menus also appear in snapshots.
