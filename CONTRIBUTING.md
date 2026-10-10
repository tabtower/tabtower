# Contributing to TabTower

Thanks for taking the time. TabTower is a small Windows tool with one target setup:
Claude Code running inside VS Code on Windows 10/11. Changes that keep it fast, quiet and
predictable on that setup are the ones most likely to be merged.

For anything bigger than a small fix, please open an issue first so the approach can be
agreed before you spend time on it.

## Build from source

You need Windows 10/11 and the [.NET 10 SDK](https://dotnet.microsoft.com/download). The
VS Code extension also needs Node.js and npm.

```powershell
git clone https://github.com/tabtower/tabtower.git
cd tabtower
dotnet build -c Debug
.\bin\Debug\net10.0-windows\TabTower.exe
```

The first launch starts the window and the named-pipe server (`\\.\pipe\tabtower`). Any
later launch with arguments acts as a CLI client against it.

Things that will bite you otherwise:

- **The running app locks its own exe.** Building over a live instance fails with MSB3027.
  Quit it first, then build:

  ```powershell
  .\bin\Debug\net10.0-windows\TabTower.exe quit
  Start-Sleep -Milliseconds 1500
  dotnet build -c Debug
  ```

- **Never end the app with `Stop-Process -Force`.** The Reserved Zone is released only on a
  normal close; a forced kill leaves the Windows work area shrunk until the next sign-in.
- **The app is a singleton.** An installed copy and a dev build share one mutex and one pipe,
  so quit one before starting the other.
- To check that the code compiles without touching a running instance:

  ```powershell
  dotnet msbuild TabTower.csproj -t:Compile -p:Configuration=Debug -v:m
  ```

### Hooks and the VS Code extension

`TabTower.exe install-hooks` registers the Claude Code hooks in `~/.claude/settings.json`
(it backs the file up first and is safe to run twice); `uninstall-hooks` removes them.
`TabTower.exe doctor` checks that the hooks and the extension are in place.

To build the companion extension:

```powershell
cd vscode-extension
npm install
npm run compile
npx @vscode/vsce package
code --install-extension .\tabtower-connector-*.vsix
```

## Run the tests

The tests are PowerShell scripts under `tests/`. Run them against the exe you actually built:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\install-hooks.tests.ps1 -Exe <path to TabTower.exe>
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\doctor.tests.ps1 -Exe <path to TabTower.exe>
```

`tests\close-tab.tests.ps1` needs the installed app running and must be run in the calling
PowerShell process, not a nested `pwsh -File`. See [`CLAUDE.md`](CLAUDE.md) for why.

These suites cover the hook installer, `doctor` and tab closing. They do **not** prove that
card status and blinking behave. If your change touches status, blink or tab tracking, test
it by hand with real sessions, and say in the pull request what you tried. The manual
checklist is in [`.claude/skills/manual-verify`](.claude/skills/manual-verify).

## Pull requests

1. Fork the repository and branch from `main`. One change per pull request.
2. Bump `<Version>` in `TabTower.csproj` for every code change (a patch bump for a fix).
   Check the version on the current `main` right before you bump, not the one in your
   branch. A documentation-only change needs no bump. The extension has its own version in
   `vscode-extension/package.json`, bumped only when the extension changes. The hook script
   header is synced by the release script; do not edit it by hand.
3. Write commit subjects in the style already in the history: `feat: ...`, `fix: ...`,
   `docs: ...`, `chore: ...`, optionally with a scope such as `fix(sessions): ...`. Release
   notes are built from commit subjects, so write the subject for someone reading the notes.
4. Run the tests above, then fill in the pull request template, including how you verified
   the change.

## Code style

There is no formatter configured; match the code around your change.

- Start with [`ARCHITECTURE.md`](ARCHITECTURE.md): it says which file owns what and what a
  change there can break. Most of the engine lives in `MainWindow.xaml.cs`.
- C# has nullable reference types enabled; keep new code null-safe rather than suppressing.
- Comments explain *why*, not what. Many existing comments record a measurement or a bug
  that shaped the code; keep them accurate when you change the code they describe.
- [`hooks/README.md`](hooks/README.md) is the authoritative description of the hooks, and
  `Cli/HookInstaller.cs` must match it exactly. Change both together.
- `hooks/tabtower-hook.ps1` is saved as UTF-8 **with BOM** and CRLF line endings. Keep it
  that way: Windows PowerShell 5.1 misreads a BOM-less script. The hook runs on every Claude
  Code event, so keep it cheap.
- The UI chrome is English and left-to-right, but text that comes from outside the app
  (workspace, session and task names, branch names, search input) must keep its own
  direction. Bind `FlowDirection` through `Services/FlowDirectionConverter.cs` rather than
  hard-coding it.
- When tuning any detection, prefer precision over coverage. A false alarm teaches people to
  ignore the deck, which costs more than a missed one. Measure the false-alarm rate before
  lowering a threshold.

The design decisions behind existing behaviour are listed in [`CLAUDE.md`](CLAUDE.md).

## License

By contributing, you agree that your contribution is licensed under the [MIT License](LICENSE).
