# flaui-cli

[繁體中文](README.zh-TW.md)

A **command-line tool for Windows desktop UI automation**, built on [FlaUI](https://github.com/FlaUI/FlaUI) and modelled on
[playwright-cli](https://github.com/microsoft/playwright-cli). It is designed for AI agents (such as Claude Code) and scripts. Main uses:

1. **UI testing** - drive and assert interactively, or run YAML test scripts and produce JUnit reports.
2. **Step-by-step documentation** - every step is captured with the operated element outlined by a numbered red box, exported as Markdown (with images/) and a single-file HTML page.

Supports WPF, WinForms, Win32 and UWP apps (for example Windows Calculator).

## Installation

Requirements: Windows 10/11 x64. No .NET runtime needs to be installed.

Download `flaui-cli-<version>-win-x64.zip` from [Releases](https://github.com/littletree71/flaui-cli/releases), extract it and add the folder to PATH.
Every release lists SHA-256 checksums. Once the repository is public, releases also carry a build provenance attestation; verify a download with the [GitHub CLI](https://cli.github.com/):

```bash
gh attestation verify flaui-cli.exe -R littletree71/flaui-cli
```

The executable is not code-signed yet, so Windows SmartScreen may warn on first run.

Building from source needs the .NET 10 SDK:

```bash
# Publish a self-contained single-file executable (no .NET runtime needed on the target machine)
dotnet publish src/FlauiCli -p:PublishProfile=win-x64
dotnet publish src/FlauiCli.Recorder -p:PublishProfile=win-x64   # only needed for record capture
# Output: artifacts/flaui-cli/flaui-cli.exe (+ flaui-cli-record.exe) - add that folder to PATH
```

Why not a `dotnet tool` or Native AOT?

- The .NET SDK does not allow projects whose target framework has a platform identifier (for example `net10.0-windows`) to be packed as a dotnet tool (NETSDK1146), and FlaUI only ships Windows target frameworks.
- FlaUI.UIA3 uses built-in COM interop, which Native AOT does not support (the UI Automation object fails to construct), so the executable is published self-contained instead.

Install the agent skill for Claude Code:

```bash
flaui-cli install-skill        # copies SKILL.md to ./.claude/skills/flaui-cli/SKILL.md
```

## Quick start

```bash
flaui-cli open calc.exe --window Calculator     # launch and attach (the background daemon starts automatically)
flaui-cli snapshot                              # UI tree + element refs
flaui-cli click id=num1Button
flaui-cli click e21                             # refs from a snapshot work too
flaui-cli assert text id=CalculatorResults "Display is 1"
flaui-cli screenshot --highlight id=CalculatorResults
flaui-cli close
```

Snapshot output:

```yaml
- window "Calculator" [ref=e1]
  - text "Display is 0" [ref=e5] id=CalculatorResults
  - button "One" [ref=e21] id=num1Button
  - checkbox "I agree" [ref=e31] [checked]
```

## Targeting elements

| Syntax | Meaning |
|---|---|
| `e21` | Snapshot ref (the same element keeps its ref across snapshots) |
| `id=num1Button` | AutomationId |
| `name="One"`, `One` | Exact name |
| `text=Disp` | Name contains (case-insensitive) |
| `type=Button&&name=OK` | Combined conditions |
| `id=panel >> name=OK` | Nested search |
| `type=ListItem&&nth=2` | The Nth match (zero-based) |
| `xpath=//Button[@AutomationId='ok']` | FlaUI XPath |

Selectors wait for elements to appear and assertions retry until the timeout (5 s by default; change it with `--timeout` or the config file).

## Commands

Run `flaui-cli --help` or `flaui-cli <command> --help` for details.

| Area | Commands |
|---|---|
| Application | `open` `attach` `close` `status` `list` `close-all` `kill-all` |
| Windows | `windows` `window` `focus` `maximize` `minimize` `restore` `resize` `move` `wait-window` |
| Inspection | `snapshot` `find` `inspect` |
| Actions | `click` `dblclick` `hover` `fill` `type` `press` `select` `check` `uncheck` `expand` `collapse` `invoke` `scroll` `drag` |
| Read / wait / assert | `get` `wait` `assert` `sleep` |
| Screenshots | `screenshot` |
| Recording | `record start\|capture\|status\|stop` |
| Documents | `doc start\|step\|status\|stop` |
| Scripts | `run` |

Global options: `-s, --session <name>` (parallel sessions), `--json` (structured output).
Exit codes: `0` success, `1` assertion failed, `2` error, `3` session not running.

## YAML test scripts

```yaml
name: Calculator addition
app:
  launch: calc.exe
  window: Calculator
timeout: 5000
steps:
  - click: id=num1Button
    doc: Press 1
  - click: id=plusButton
  - click: id=num2Button
  - click: id=equalButton
  - assert: { target: id=CalculatorResults, text: Display is 3 }
```

```bash
flaui-cli run samples/calculator.flow.yaml                                 # console report
flaui-cli run tests/*.flow.yaml --reporter junit --output out/report.xml    # CI
flaui-cli run samples/calculator.flow.yaml --doc out/calc-doc              # also produce a document
```

### Recording

```bash
flaui-cli record start --name Login        # records later CLI commands (refs become stable selectors)
...
flaui-cli record stop --out login.flow.yaml

flaui-cli record capture --out flow.yaml  # captures real mouse/keyboard input; Ctrl+Shift+Q stops
```

## Documents

```bash
flaui-cli doc start --title "Using the calculator"
flaui-cli click id=num1Button --note "Press 1"
flaui-cli doc step "Done"
flaui-cli doc stop --out docs/calculator --format md,html
```

## Configuration

`.flaui-cli/config.json` (relative to the current working directory, every field optional):

```json
{
  "timeouts": { "action": 5000, "launch": 20000 },
  "outputDir": ".flaui-cli",
  "autoSnapshot": true,
  "snapshot": { "depth": 0, "includeOffscreen": false },
  "daemonIdleMinutes": 30
}
```

## Architecture

```
src/
  FlauiCli/                CLI front-end (System.CommandLine), daemon client, local commands
  FlauiCli.Core/           Engine: abstractions, selectors, snapshots, dispatcher, scripts, recording, documents, daemon
  FlauiCli.Driver.FlaUI/   The only project that references FlaUI (implements IUiDriver / IUiElement)
  FlauiCli.Recorder/       flaui-cli-record.exe: the input recorder, the only project that declares the hook APIs
tests/
  FlauiCli.Core.Tests/     Unit tests (FakeDriver, no desktop needed)
  FlauiCli.E2E.Tests/      DriverContractTests (FlaUI adapter contract) + CLI end-to-end tests
  TestApps/WpfSample/      WPF application used by the tests
```

- Every CLI invocation is just a client. The first `open` / `attach` starts the same executable in daemon mode; they talk over a named pipe restricted to the current user, and every UI Automation call runs on a single daemon thread.
- **FlaUI isolation**: Core does not reference FlaUI; it depends only on the `IUiDriver` / `IUiElement` abstractions and its own enums. The FlaUI version is pinned in `Directory.Packages.props`; upgrades must pass `DriverContractTests` (which include an enum-mapping completeness check).

## Behaviour worth knowing (security and antivirus)

flaui-cli does things that security products watch closely. They are all intentional and documented here:

- **Synthetic input and screenshots** - actions send real mouse/keyboard input (`SendInput`) and screenshots capture the screen.
- **Global keyboard/mouse hooks** - live in a separate executable, `flaui-cli-record.exe`, which the daemon starts as an ordinary child process **only** while `record capture` is running; it removes the hooks and exits when the capture stops. `flaui-cli.exe` itself never installs a hook and its code does not declare the hook APIs, so everyday automation works even if the recorder is blocked or deleted (only `record capture` then fails, with a clear message). The APIs are declared openly as normal static imports - nothing is loaded dynamically or obfuscated. If your security product needs an exception, it is only needed for `flaui-cli-record.exe`.
- **Background daemon** - the same `flaui-cli.exe` started as `flaui-cli.exe daemon --session <name>`, without a console window. It only breaks away from the caller's job object when that job would otherwise kill it, and it exits after 30 idle minutes (`close` / `kill-all` stop it immediately).
- **Local files** - session files and logs under `%LOCALAPPDATA%\flaui-cli`, snapshots and screenshots under `.flaui-cli` in the working directory.
- The executable requests no elevation (`asInvoker`) and the published build is neither compressed nor packed.

## Tests

```bash
dotnet test tests/FlauiCli.Core.Tests                                   # unit tests
dotnet test tests/FlauiCli.E2E.Tests                                    # needs an interactive desktop; do not use mouse or keyboard while it runs
```

## Known limitations

- Actions move the real mouse and send real key presses; do not use the computer while they run (`click --invoke` avoids moving the mouse).
- UWP windows are hosted by ApplicationFrameHost, so `open` needs `--window <title>`; `close` only closes the window and never kills the shared process.
- Input capture relies on low-level hooks and identifies elements by the position of the mouse press, so fast-changing UI (such as animated menus) may be recorded as an unexpected element.

## License

[MIT](LICENSE). Third-party components and their licenses are listed in [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt).
