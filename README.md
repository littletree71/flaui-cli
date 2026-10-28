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
The zip contains `flaui-cli.exe`, the input recorder `flaui-cli-record.exe` (only used by `record capture`; keep it next to
`flaui-cli.exe`), the license notices and the agent skill (`skills/SKILL.md`). Both executables are also attached on their own.
Every release lists SHA-256 checksums. Once the repository is public, releases also carry a build provenance attestation; verify a download with the [GitHub CLI](https://cli.github.com/):

```bash
gh attestation verify flaui-cli.exe -R littletree71/flaui-cli
```

The executables are not code-signed yet, so Windows SmartScreen may warn on first run, and some antivirus products block
an unsigned build they have not seen before (usually a reputation check for unknown files, not a detection of what it does). If that happens, report
it to the vendor as a false positive rather than weakening your protection.

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
flaui-cli install-skill                  # copies SKILL.md to ./.claude/skills/flaui-cli/SKILL.md
flaui-cli install-skill --dir <folder>   # or into another folder
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
  - edit "Name" [ref=e30] id=nameInput value="Alice" [focused]
  - checkbox "I agree" [ref=e31] [checked]
```

State markers: `[disabled]` `[checked]` `[mixed]` `[expanded]` `[collapsed]` `[selected]` `[focused]`.
Unnamed containers and off-screen elements are left out; `snapshot --all` prints the full tree, `--depth N` limits the depth,
`--root <target>` prints a subtree, `--boxes` adds element bounds and `--filename f.yml` saves to a file.
After every action command a snapshot is saved automatically (`.flaui-cli/snapshot-*.yml`, turn off with `autoSnapshot`).

`open` also accepts a Store app AUMID (any value containing `!`, for example
`Microsoft.WindowsCalculator_8wekyb3d8bbwe!App`); `attach` takes a PID or process name, `--title <window title>`, or both.
`open --window` matches the new window's title loosely (exact, starts with, contains), but a window that was already open
before the launch only by its exact title, so an unrelated window such as an editor showing `calculator.flow.yaml` is
never taken for the app.

## Targeting elements

| Syntax | Meaning |
|---|---|
| `e21` | Snapshot ref (the same element keeps its ref across snapshots) |
| `id=num1Button` | AutomationId |
| `name="One"`, `One` | Exact name |
| `text=Disp` | Name contains (case-insensitive) |
| `type=Button` | ControlType (button, edit, checkbox, combobox, listitem...) |
| `class=TextBox` | ClassName |
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
| Windows | `windows` `window` `focus` `maximize` `minimize` `restore` `resize` `move` `wait-window` `close-window` |
| Inspection | `snapshot` `find` `inspect` |
| Actions | `click` `dblclick` `hover` `fill` `type` `press` `select` `check` `uncheck` `expand` `collapse` `invoke` `scroll` `drag` |
| Read / wait / assert | `get` `wait` `assert` `sleep` |
| Screenshots | `screenshot` |
| Recording | `record start\|capture\|status\|stop` |
| Documents | `doc start\|step\|status\|stop` |
| Scripts | `run` |
| Other | `install-skill` |

Global options: `-s, --session <name>` (parallel sessions; defaults to the `FLAUI_CLI_SESSION` environment variable, then `default`),
`--json` (structured output), `--verbose` (end the result with a timing breakdown: finding elements, the action, the
automatic snapshot, the total in the daemon and the total including the round trip). Element commands take
`--timeout <ms>`, and action commands take `--note <text>` for documents.
`close` closes the window and stops the session's daemon; `close --keep-app` ends the session but leaves the application running.
`close-window [index|ref|title]` closes a single window or dialog (WindowPattern, else `WM_CLOSE`, so dialogs without a Cancel
button close too) and keeps the session; when it was the current window, the session switches back to the window that was
current before it (for example the MDI child a dialog was opened from). `windows` lists the windows you can switch to;
tooltips and unnamed popups are left out.
`wait-window <title>` prefers an exact title, then one that starts with the text, then one that contains it; `--exact` only
accepts an exact title and `--regex` treats the title as a regular expression (useful for MDI children, whose title also
appears in the MDI parent's title).
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

- Top-level fields: `name`, `app`, `timeout` (default ms for every step; launching the app waits at least
  `timeouts.launch`), `highlight` (`padding` / `outline` / `badge` for every screenshot of the script, overriding `highlight`
  in the config file; a step's own `highlight-padding` / `no-outline` / `badge-position` still wins), `steps`.
- `app`: `launch` (exe or AUMID), `args`, `window`, `attach` (PID or process name) and `close` (close the app afterwards;
  defaults to `true` for `launch` and `false` for `attach`). A plain string means `launch`. A launched app is closed even
  when the script fails, and terminated when its window never appeared. With `close: false` the app keeps running and
  keeps the standard handles it inherited, so a caller reading `run`'s output through a pipe waits until it exits.
- A step is one command: a scalar is its first argument (`- click: id=ok`), a mapping gives named arguments and options
  (`- fill: { target: id=name, text: Alice }`), and `doc:` describes the step in generated documents.
  `run`, `list`, `install-skill` and the other local commands cannot be used in scripts.

```bash
flaui-cli run samples/calculator.flow.yaml                                 # console report
flaui-cli run tests/*.flow.yaml --reporter junit --output out/report.xml    # CI
flaui-cli run tests/*.flow.yaml --bail                                     # stop after the first failing script
flaui-cli run samples/calculator.flow.yaml --doc out/calc-doc              # also produce a document (--doc-format md,html)
```

`run` drives the application in its own process and does not need a running session. A failing script saves a failure screenshot,
and the exit code is `1` when any script fails.

### Recording

```bash
flaui-cli record start --name Login        # records later CLI commands (refs become stable selectors)
...
flaui-cli record stop --out login.flow.yaml

flaui-cli record capture --name Login     # captures real mouse/keyboard input in the current window
flaui-cli record status
flaui-cli record stop --out login.flow.yaml   # or press Ctrl+Shift+Q in the app to stop capturing, then record stop
```

`record capture` starts `flaui-cli-record.exe`, which must sit next to `flaui-cli.exe`. Input sent to other applications while
the capture runs is ignored, so only steps in the target app are recorded.

## Documents

```bash
flaui-cli doc start --title "Using the calculator"
flaui-cli click id=num1Button --note "Press 1"   # each action is captured with its target outlined by a numbered red box
flaui-cli doc step "Done" --highlight id=CalculatorResults   # manual step; --highlight can be repeated
flaui-cli doc status
flaui-cli doc stop --out docs/calculator --format md,html    # index.md + images/ and a single-file index.html
```

Generated step descriptions are in English (for example `Click the "One" button`); `--note` and the script `doc:` field accept any language.
Most actions are captured before they run; `fill`, `type`, `select`, `check` and `uncheck` are captured after they run so the screenshot shows the new value.

### Hiding private data

Screenshots for published documents often show names or other data that must not appear. `--mask <target>` covers an element
with a solid box and `--mask-column <column header>` covers every visible cell of a table column; both can be repeated:

```bash
flaui-cli screenshot --mask id=customerName --mask-column "id=dataGridView1 >> name=Partner" --highlight id=okButton
flaui-cli doc start --title "Orders" --mask-column "id=dataGridView1 >> name=Partner"   # every screenshot of the document
```

Masks are drawn before outlines and badges, so those stay visible. `screenshot` and `doc step` fail without saving anything when a
mask is not found; masks given to `doc start` are applied to every screenshot of the document wherever they are shown.

## Configuration

`.flaui-cli/config.json` in the current working directory. Every field is optional; the values below are the defaults.

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

| Field | Default | Meaning |
|---|---|---|
| `timeouts.action` | `5000` | Milliseconds to wait for elements (selectors, `wait`, `assert`). `--timeout` overrides it for one command, `timeout:` for a script |
| `timeouts.launch` | `20000` | Milliseconds `open` waits for the application's window |
| `outputDir` | `.flaui-cli` | Folder for automatic snapshots, screenshots and documents without an explicit path; relative to the working directory, or absolute |
| `autoSnapshot` | `true` | Save a snapshot after every action command. Turn it off for large windows |
| `snapshot.depth` | `0` | Default depth of `snapshot` and of the automatic snapshot; `0` is unlimited. `snapshot --depth` overrides it |
| `snapshot.includeOffscreen` | `false` | Include off-screen elements in snapshots (like `snapshot --all`) |
| `highlight.padding` | `3` | Pixels between an element and its red outline: positive outside, `0` on the edge, negative inside |
| `highlight.outline` | `true` | `false` draws only the number badges, without outlines |
| `highlight.badge` | `auto` | Where number badges go: `auto` (chosen per element), `top`, `bottom`, `left`, `right`, `top-left`, `top-right`, `bottom-left`, `bottom-right` or `inside` (inside the outline's top-left corner) |
| `daemonIdleMinutes` | `30` | Minutes without commands after which a session's daemon exits |

- The file is read again for every command, so changes apply right away. `daemonIdleMinutes` is the exception: it is read once,
  when the session's daemon starts.
- `//` comments and trailing commas are allowed, and field names are not case-sensitive. An invalid file makes commands fail with
  the file's path instead of silently using the defaults.
- `highlight` applies to `screenshot --highlight`, `doc step --highlight` and the screenshots `doc` takes for actions. When
  highlighted elements touch each other (a grid of buttons), a negative padding such as `-3` keeps their outlines apart. Without
  outlines the badges sit where they would with them, preferring the middle above or below the element.
- `auto` keeps a badge next to its own element and away from other badges. A fixed `badge` puts every badge on the same side of
  its outline, even where it then lands next to a neighbouring element (for example `top` on the second row of a button grid).
- `screenshot` and `doc step` add a `### Warnings` section (and `warnings` in `--json` data) when the annotations may be hard to
  read: outlines running into each other, a badge covering another highlighted element or another badge, or a badge moved to stay
  inside the image. The screenshot is saved anyway and the exit code stays 0. Only highlighted elements are checked.
- Precedence for highlights: a command's `--highlight-padding` / `--no-outline` / `--badge-position`, then a script's top-level
  `highlight`, then this file, then the defaults.

## Large applications

Every command looks elements up in the current window and the application's other top-level windows through UI Automation,
which is slow for windows with thousands of elements (especially .NET Framework WinForms apps). To keep commands fast:

- Turn off the snapshot saved after every action with `"autoSnapshot": false` in the config file; scripts that only use selectors do not need it.
- Use `snapshot --depth N` or `snapshot --root <target>`: only that part of the tree is read. `find <text>` searches without printing the tree.
- Prefer selectors with `id=`, `name=`, `class=` or `type=`: they stop at the first match. `text=`, `nth=` and `xpath=` need every match.
- Scope selectors with `>>` (for example `id=toolStrip1 >> name=Query`), and close windows you no longer need (`close-window`).
- Add `--verbose` to see where the time goes (`find` / `action` / `snapshot`); include it when reporting a slow command.
- Raise `--timeout` (or `timeouts.action`) when lookups take longer than the default 5 seconds.

UIA3 descendant searches stop after visiting about 512 elements on some providers (seen with WinForms), without an error.
flaui-cli keeps what UIA found and fills in the rest: windows by enumerating child and owned windows, other selectors by
searching level by level within the command's `--timeout`, and `snapshot` by reading only the part that was cut (up to
10 seconds). The snapshot saved after every action is not filled in, to keep actions fast. A selector that matches nothing
in such a window keeps searching until the timeout; scoping with `>>` from a container near the element keeps lookups small.
Raise `--timeout` if an element deep in a large window is not found in time. All of this goes through UI Automation and
window enumeration only: flaui-cli never reads or writes the memory of the application under test.

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
  TestApps/WinFormsLargeTree/  WinForms window with more than 512 UIA elements (the UIA3 search limit)
```

- Every CLI invocation is just a client. The first `open` / `attach` starts the same executable in daemon mode; they talk over a named pipe restricted to the current user, and every UI Automation call runs on a single daemon thread.
- **FlaUI isolation**: Core does not reference FlaUI; it depends only on the `IUiDriver` / `IUiElement` abstractions and its own enums. The FlaUI version is pinned in `Directory.Packages.props`; upgrades must pass `DriverContractTests` (which include an enum-mapping completeness check).

## Behaviour worth knowing (security and antivirus)

flaui-cli does things that security products watch closely. They are all intentional and documented here:

- **Synthetic input and screenshots** - actions send real mouse/keyboard input (`SendInput`) and screenshots capture the screen.
- **Global keyboard/mouse hooks** - live in a separate executable, `flaui-cli-record.exe`, which the daemon starts as an ordinary child process **only** while `record capture` is running; it removes the hooks and exits when the capture stops. `flaui-cli.exe` itself never installs a hook and its code does not declare the hook APIs, so everyday automation works even if the recorder is blocked or deleted (only `record capture` then fails, with a clear message). The APIs are declared openly as normal static imports - nothing is loaded dynamically or obfuscated. If your security product needs an exception, it is only needed for `flaui-cli-record.exe`.
- **No access to other processes' memory** - flaui-cli reads applications only through UI Automation and finds windows by
  enumerating them. It never opens another process's memory, injects code or creates remote threads; a test
  (`NativeImportTests`, run on every CI build) fails if `flaui-cli.exe` ever imports such APIs.
- **Terminating processes** - only processes flaui-cli launched itself: `close` when the app does not exit within 3 seconds,
  and `run` when a launched app never showed its window. Processes you attached to are never terminated.
- **Background daemon** - the same `flaui-cli.exe` started as `flaui-cli.exe daemon --session <name>`, without a console window. It only breaks away from the caller's job object when that job would otherwise kill it, and it exits after 30 idle minutes (`close` / `kill-all` stop it immediately).
- **Passwords stay out of output** - text typed into a password field with `fill` is replaced with `********` in command results,
  recorded scripts and generated documents; `record capture` writes the same placeholder for password fields (replace it with the
  real value before running the script). The daemon log never stores the text of `fill` / `type` at all.
- **Local files** - session files and logs under `%LOCALAPPDATA%\flaui-cli`, snapshots and screenshots under `.flaui-cli` in the working directory.
- The executable requests no elevation (`asInvoker`) and the published build is neither compressed nor packed.

## Tests

```bash
dotnet test tests/FlauiCli.Core.Tests                                   # unit tests
dotnet test tests/FlauiCli.E2E.Tests                                    # needs an interactive desktop; do not use mouse or keyboard while it runs
```

## Known limitations

- Actions move the real mouse and send real key presses; do not use the computer while they run (`click --invoke` avoids moving the mouse).
- On WinForms apps, InvokePattern does not return while a modal dialog it opened is shown: `click --invoke` / `invoke` fail after `--timeout`, and until the dialog closes only `status`, `sleep` and `press <keys>` work. Use a plain `click` for buttons that open a dialog.
- UWP windows are hosted by ApplicationFrameHost, so `open` needs `--window <title>`; `close` only closes the window and never kills the shared process.
- In a WinForms `ListView` (Details view without `FullRowSelect`) the `listitem` element spans the whole row, so `click` on it
  hits the empty space right of the text and does not select the item. Use `select <list> <item>` or click the text inside the row
  (`click "id=lvItems >> type=text&&name=BRAVO"`).
- Input capture relies on low-level hooks and identifies elements by the position of the mouse press, so fast-changing UI (such as animated menus) may be recorded as an unexpected element.

## License

[MIT](LICENSE). Third-party components and their licenses are listed in [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt).
