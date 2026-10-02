# TabTower Connector

**This extension does nothing on its own.** It is the VS Code half of
[TabTower](https://github.com/tabtower/tabtower) — a Windows app that shows every
VS Code window running Claude Code as a live card: a real thumbnail of the window, the
git branch, and a status light per session driven by Claude Code's hooks. Without that
app installed there is nothing on the other end of the pipe and nothing to see.

> **Install the app, not this extension.** The
> [TabTower installer](https://github.com/tabtower/tabtower/releases/latest)
> installs the connector for you and keeps the two versions matched. This Marketplace
> entry exists so the project is findable — installing from here alone leaves you with
> an inert extension.

Windows only, and it needs the Claude Code extension for VS Code.

## What it does

- Reports a snapshot to TabTower over the named pipe `\\.\pipe\tabtower` — the
  workspace folder, the current git branch and the open Claude Code tabs — on startup
  and on every change.
- Handles TabTower's requests to open or resume a session, revealing the tab through
  Claude Code's own editor command (falling back to `claude --resume` in a terminal).
- Closes a session's tab on request, locating it through the same editor command and
  refusing (with a line in the Output channel) whenever the tab cannot be identified
  with certainty.

The pipe is polled with a reconnect, so the order the app and VS Code start in doesn't
matter, and closing TabTower is harmless.

## Building it yourself

Only needed if you are working on the extension — the installer ships a built copy.

```powershell
cd vscode-extension
npm install
npm run package
code --install-extension tabtower-connector-<version>.vsix
```

Then run **Reload Window** in every VS Code window.

## Troubleshooting

| | |
|---|---|
| Log | Output panel → the **TabTower** channel |
| Force a report | Command Palette → **TabTower: Sync Now** |
| Cards don't appear | Check TabTower itself is running, then Reload Window |

## License

MIT — see the [LICENSE](https://github.com/tabtower/tabtower/blob/main/LICENSE) in the
repository root.
