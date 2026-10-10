# TabTower: working notes for Claude Code

A WPF (.NET 10, Windows-only) control deck for Claude Code sessions. The UI and the CLI
are **the same exe**: launched with no arguments it raises the window plus a named-pipe
server (`\\.\pipe\tabtower`); launched with arguments it acts as a client against the
running instance. A companion VSCode extension (`vscode-extension/`) reports tabs and the
git branch over that pipe and opens sessions on request.

Public-facing overview: [`README.md`](README.md). Hook wiring, the waiting-detection
thresholds and the toggles: [`hooks/README.md`](hooks/README.md). That file is the
authoritative source `Cli/HookInstaller.cs` must match exactly.

**Which file holds what:** [`ARCHITECTURE.md`](ARCHITECTURE.md), the code map. Read it
before hunting for where a behaviour lives; `MainWindow.xaml.cs` holds most of the engine
and that is not obvious from the file list.

## Build, run, deploy

**The running app locks its own exe.** Building over a live instance fails with MSB3027.
Always:

```powershell
.\bin\Debug\net10.0-windows\TabTower.exe quit     # graceful, releases the AppBar
Start-Sleep -Milliseconds 1500
dotnet build -c Debug
Start-Process .\bin\Debug\net10.0-windows\TabTower.exe
```

Never `Stop-Process -Force`: the Reserved Zone is released in `OnClosing` only, and a
forced kill leaves Windows' work area permanently shrunk with no way for the user to
tell why.

To compile without touching a running instance (a syntax check that skips the copy step):

```powershell
dotnet msbuild TabTower.csproj -t:Compile -p:Configuration=Debug -v:m
```

`WinExe` means CLI output only reaches a parent console through
`AttachConsole(ATTACH_PARENT_PROCESS)`: expect no captured stdout from tooling.

The app is a singleton: one named mutex and one pipe are shared by every build, so an
installed copy and a dev build never run side by side. Quit one before starting the other.

## Tests

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\install-hooks.tests.ps1 -Exe <path to TabTower.exe>
```

Cases over the `install-hooks` / `uninstall-hooks` merge: a missing settings file, an empty
one, hooks from an old path, another tool's hooks on the same event, malformed JSON, a second
run in a row, dry-run, a shared group where only our entry may be removed, and registrations
left by the app's former name. Run it against the exe you actually built.

`tests\doctor.tests.ps1 -Exe <path>` covers `doctor` the same way (temp settings files and a
temp extensions folder). `release.ps1` runs both suites against the published exe.

`tests\user-path.tests.ps1` (no `-Exe`, nothing is started) covers what `install.ps1` and
`uninstall.ps1` do to the user PATH. It loads `Edit-UserPath` out of each script's own text
and aims it at a scratch registry key it creates and deletes: one entry in, the same entry
out, every other character and the value's registry type exactly as they were. The function
is duplicated in the two scripts on purpose (each must run alone from the zip) and the suite
fails when the copies differ. `release.ps1` does not run it yet.

**These tests passing does not mean the status lifecycle works.** They cover the
installer only. A card can stay blue, blink wrongly or go quiet while all of them pass:
see "Debugging status and blink" below.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\close-tab.tests.ps1
```

Cases over `session close-tab` / `session end --close-tab`. It needs the INSTALLED app
running, because it stands a fake VSCode connector on the named pipe and reads back the exact
JSON the deck pushes, which proves the deck half without touching a real window, including
the capability check that refuses an older connector. It must run **in the calling
PowerShell process**, not a nested `pwsh -File`: `WinExe` means the CLI writes through
`AttachConsole(ATTACH_PARENT_PROCESS)`, so a parent that HAS a console gets the output on
screen and the redirect file comes back empty. The extension half is out of its reach and
still needs a window running the new connector.

## Versioning

Every code change bumps `<Version>` in `TabTower.csproj`. Three versions move
independently and are printed together by `install.ps1` so a mismatch is visible:

| Part | Where |
|---|---|
| App | `TabTower.csproj` → `<Version>` |
| Hook script | the `# Version:` header in `hooks/tabtower-hook.ps1`: bump it with the csproj; the build fails while they differ |
| VSCode extension | `vscode-extension/package.json` → `version`, bumped only when the extension changes |

`hooks/tabtower-hook.ps1` is saved **UTF-8 with BOM** and must stay that way:
PowerShell 5.1 reads a BOM-less `.ps1` as ANSI and mangles the non-ASCII characters in
its comments.

**Two branches that pick the same version do NOT conflict, and nothing else warns either.**
Both sides write the identical string, so git merges the line clean: a version collision is
invisible precisely because it is a collision, and the result is two different binaries under
one number. **Read the version on `origin/main`, not the one in your own base, immediately
before bumping.** `git show origin/main:TabTower.csproj | grep '<Version>'` after a
`git fetch` is the whole check.

## Git

- Branch before editing. Never work directly on `main`.
- Commit whenever a change is coherent and verified, with a message that explains the why.
- **Verify a push against the remote, not against the exit code.** `git ls-remote origin main`,
  or a `git fetch` then `git log --oneline origin/main -1`, is the check.
- Temporary zip/publish artifacts: add the pattern to `.gitignore` *before* creating them.

## UI language and text direction

The UI chrome is **English and LTR**.

Text that comes from **outside** the app is a different matter and must stay
direction-aware: workspace, session and task names, descriptions, tooltips, status values
from the tasks file, search input, git branch names. Hebrew or Arabic content there renders
right-to-left.

One deliberate exception: the **task card** header (`TaskItemView.xaml`) is pinned
RightToLeft rather than derived per string. Deriving it left a right-to-left task and a
left-to-right task in the same list aligned differently, which breaks scanning the list.
Same card: id hard left, then status, then the name.

One refinement of that, per list and never per card: when no loaded task title has a
right-to-left letter anywhere in it, the names of that list are drawn left-aligned, and the
id-and-status column takes one shared width, so every name starts on the same vertical line.
`TasksPanelViewModel.Apply` decides it once per load, over every task in the file, and stamps
the same `NameFlow` on each card, so a search that hides some cards cannot flip the rest. One
right-to-left letter in any title keeps the whole list on the fixed header above, unchanged.

- `Services/FlowDirectionConverter.cs` (keyed `Rtl` in `App.xaml`) resolves direction from
  the first strong character. Bind a control's `FlowDirection` through it rather than
  hard-coding a direction.
- `App.xaml` already applies it to every `ToolTip`, `MenuItem` and `ComboBoxItem`, and to
  the ComboBox selection box. New controls that display external text need it explicitly.
- Free-text `TextBox`es bind `FlowDirection` to their own `Text`. ASCII-only fields
  (paths, ids, colors, sizes) override with `FlowDirection="LeftToRight"`.
- **Remember that `FlowDirection` mirrors layout, not just glyphs.** `HorizontalAlignment`,
  `DockPanel.Dock` and `Margin` all flip with it. When changing a container's direction,
  re-check the alignment of everything inside it.
- The toolbar's `IconStrip` in `MainWindow.xaml` is `RightToLeft` as a **layout device**: it
  controls which icons overflow to the second row first, so ⚙ keeps the top row. It holds no
  text. Don't "fix" it.

## Debugging status and blink

Card status comes from four sources: the hooks, the transcript scanner, Claude Code's
`background_tasks`, and pending `tool_use` witnesses. Most bugs live in the seam between them,
so before touching status, blink or tab-tracking code, find which source set the state you are
looking at. The debug log (`tabtower log --debug on`, files in `%APPDATA%\TabTower\logs`)
records every status transition; read it before reasoning about one.

- **The session group a session runs in comes from the hook, and only from the hook.** The
  bridge sends the NAME of the configuration folder the session's VSCode instance was started
  with (`CLAUDE_SECURESTORAGE_CONFIG_DIR`) on `SessionStart`, `UserPromptSubmit` and `Stop`, and
  the deck maps it to a group through the group's `ConfigDir`. Do not try to infer it from tab
  labels or from a connection's tab list: two extension hosts on one folder report identical
  tab lists, and labels collide. A recorded group is never cleared.
- **A dead session's tab is closed through the connector, by a unique label, and a dead
  session is never revealed.**
- When tuning any detection: **precision over coverage**. A false alarm teaches the user to
  ignore the deck, which costs more than a missed one. Measure the false-alarm rate before
  lowering a threshold.

## Why the code looks like this (design notes)

Numbered as they were in the original spec, because code comments cite them by number. They
explain existing behaviour so a change is made knowingly.

| # | Decision |
|---|---|
| 11 | **Status scheme.** `idle` = grey, `working` = steady blue (orange is reserved for `waiting`), `waiting` = blinking orange, `done` = blinking purple → steady on acknowledge, `error` = blinking red → steady, `wrapped` = green (the session was closed out for good), `replaced` = steady white (handed off to a successor and killed; not an alert, so no blink). The status → colour/blink map lives in config (`StatusStyles`), so it changes without touching hooks or code. |
| 12 | **A session that closes** disappears from the normal view and stays available in the card's expanded view (▼) with a resume option. Retention: the last ~20 closed sessions per workspace (`ClosedSessionRetention`). The expanded view collapses itself after five minutes, because being expanded is invisible once the header scrolls away. |
| 13 | **VSCode only in the UI.** The engine underneath stays generic (any top-level window can be tracked, pinned and driven), but the UI and the flows are filtered to VSCode. |
| 15 | **The app is a control deck for Claude Code sessions**, not a generic window grid. Tile data from the pre-cards era is still round-tripped in config as a legacy field so nothing is lost, but it is never displayed. |
| 16 | **Workspaces are persistent entities**: remembered with no window open. Active ones (bound window or live session) float to the top; old ones can be hidden. Wide cards with a minimum size, wrapping by width, inside a vertical scroll area. |
| 17 | **Main card content:** project name + the current git branch. Custom title and description are supported on both card levels. |
| 18 | **Card colour comes from VSCode**: `.vscode/settings.json`, either `peacock.color` or `workbench.colorCustomizations."titleBar.activeBackground"`, when present. Precedence: manual override > Peacock > default. |
| 21 | **Adding a workspace**, in priority order: (1) picking a folder: primary, the path is known immediately so branch and colour resolve before any window exists; (2) reported by the VSCode extension; (3) drag-in: secondary, blocked for non-VSCode windows and duplicates; (4) the hook's `cwd`: the safety net, creating a workspace for a session that reports one the deck doesn't have. |

Two limits that keep coming back and are not bugs:

- **An inactive VSCode tab has no thumbnail.** VSCode/DWM don't render it, so session cards
  are text plus border. A hard platform limit, not a missing feature.
- **A minimized window freezes its DWM thumbnail** on the last frame. Being covered by
  other windows is fine; minimized is not.

## Procedures

Longer step-by-step procedures live as skills under `.claude/skills/`, loaded on demand:

- `release`: cutting and publishing a release. GitHub keeps exactly one release (the
  current version); older releases are deleted, their tags are kept, and the accumulated
  history lives in [`CHANGELOG.md`](CHANGELOG.md), which `release.ps1` prepends to.
- `manual-verify`: the manual test checklist for what automation can't cover.
