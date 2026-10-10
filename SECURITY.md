# Security policy

## Supported versions

Only the latest release receives fixes. The [Releases page](https://github.com/tabtower/tabtower/releases)
always carries exactly that one.

## Reporting a vulnerability

Please do **not** open a public issue for a security problem.

Report it privately through GitHub: open the repository's **Security** tab and choose
**Report a vulnerability**, or go directly to
<https://github.com/tabtower/tabtower/security/advisories/new>. Include the TabTower version
(shown in the ⚙ menu), your Windows version, and the steps to reproduce.

If that option is not available to you, open a public issue that only asks for a private
contact, with no details of the problem, and a maintainer will reach out.

We will acknowledge the report, keep you informed while a fix is prepared, and credit you in
the release notes unless you prefer otherwise.

## What TabTower touches on your machine

TabTower runs entirely on your machine, under your own user account, and needs no admin
rights.

**Network.** No telemetry, no analytics, no update checks. The app and the hook script make
no network connections of their own. A web page opens in your browser only when you click
for one: a link on a task from your tasks file, or the one-time "Get TabTower" notice that
the VS Code extension shows when the app is not installed.

**The named pipe.** The app, the CLI, the hooks and the VS Code extension talk over a local
named pipe, `\\.\pipe\tabtower`. It is not reachable from the network. It adds no access
restrictions beyond the Windows defaults, so treat any program running under your account
as able to send it commands, as with any other per-user tool.

**Files and settings it writes:**

| What | Where |
|---|---|
| The app | `%LOCALAPPDATA%\Programs\TabTower`, added to your user `PATH` (by `install.ps1`) |
| Settings, logs, toolbar toggle files | `%APPDATA%\TabTower` |
| Claude Code hook registrations | `~/.claude/settings.json`, backed up before every change (`install-hooks` / `uninstall-hooks`) |
| The companion VS Code extension | your VS Code extensions folder (by `install.ps1`) |
| Start with Windows | a value under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, only while that option is on |

**Files it reads:**

- Claude Code session transcripts under `~/.claude/projects`, read-only, to detect when a
  session is waiting for you. The content stays on your machine.
- The event data Claude Code hands to each hook. The hook passes the session id, its status
  and its working folder to the app.
- Your tasks file, if you configure one. It is only ever read.
- `.vscode/settings.json` in your workspaces, for the card colour.

`uninstall.ps1` removes the app, the hooks, the extension, the `PATH` entry and the
start-with-Windows value. Your settings in `%APPDATA%\TabTower` are kept unless you pass
`-PurgeConfig`.
