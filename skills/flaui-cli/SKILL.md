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
- State markers: `[disabled]` `[checked]` `[expanded]` `[collapsed]` `[selected]` `[focused]`.
- Unnamed containers are omitted; use `snapshot --all` for the full tree, `--depth N` to limit depth and `--root e5` for a subtree.

## Targeting elements

| Syntax | Meaning |
|---|---|
| `e21` | Snapshot ref (the same element keeps its ref across snapshots) |
| `id=num1Button` | AutomationId |
| `name="One"` or just `One` | Exact name |
| `text=Disp` | Name contains |
| `type=Button&&name=OK` | Combined conditions (type is the ControlType: button, edit, checkbox, combobox...) |
| `id=panel >> name=OK` | Search inside the previous match |
| `type=ListItem&&nth=2` | Third match |
| `xpath=//Button[@AutomationId='ok']` | FlaUI XPath |

Selectors wait for the element to appear (5 s by default, change with `--timeout`); assertions retry until the timeout too.

## Common commands

| Area | Commands |
|---|---|
| Application | `open <exe> [args] [--window title]`, `attach <pid\|name> [--title title]`, `close`, `status`, `list` |
| Windows | `windows`, `window <index\|title>`, `wait-window <title>`, `maximize`, `minimize`, `restore`, `resize W H`, `move X Y` |
| Inspection | `snapshot`, `find <text> [--regex]`, `inspect <target>` |
| Actions | `click <t> [--button right] [--double] [--invoke]`, `dblclick`, `hover`, `fill <t> <text>`, `type <text>`, `press <keys>` (`Enter`, `Ctrl+S`), `select <t> <item or #index>`, `check`, `uncheck`, `expand`, `collapse`, `invoke`, `scroll <t> --dir down`, `drag <src> <dst>` |
| Reading | `get text\|value\|name\|state\|rect <t>`, `get prop <t> HelpText` |
| Waiting / asserting | `wait <t> --state visible\|hidden\|enabled\|gone`, `assert exists\|not-exists\|visible\|hidden\|enabled\|disabled\|checked\|unchecked <t>`, `assert text\|contains\|matches\|value <t> <expected>`, `sleep <ms>` |
| Screenshots | `screenshot [t] [--filename f.png] [--highlight t ...] [--screen]` |

Exit codes: 0 success, 1 assertion failed, 2 error, 3 session not running. Add `--json` for structured output.
Use `-s <name>` (or the `FLAUI_CLI_SESSION` environment variable) to run several sessions.

## Producing YAML test scripts

```bash
flaui-cli record start --name Login       # every later action command is recorded (refs become stable selectors)
flaui-cli click id=submitButton
flaui-cli record stop --out tests/login.flow.yaml
flaui-cli run tests/login.flow.yaml --reporter junit --output out/report.xml
```

Recording real user input: `flaui-cli record capture --out flow.yaml` (the user operates the app; Ctrl+Shift+Q stops).

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

## Producing documents with screenshots

```bash
flaui-cli doc start --title "Using the calculator"
flaui-cli click id=num1Button --note "Press 1"   # each action is captured and its target outlined with a numbered red box
flaui-cli doc step "Done, the display shows 1"   # add a manual step
flaui-cli doc stop --out docs/calculator         # writes index.md + images/ and a single-file index.html
```

Scripts can produce documents directly: `flaui-cli run flow.yaml --doc docs/flow`.

## Notes

- The window of a UWP app (Calculator etc.) belongs to ApplicationFrameHost, so `open` always needs `--window`.
- Actions move the real mouse; do not use the computer while they run. Use `click --invoke` to avoid moving the mouse.
- When an element cannot be found, check with `snapshot` or `find <text>`; popups such as drop-downs and menus also appear in snapshots.
