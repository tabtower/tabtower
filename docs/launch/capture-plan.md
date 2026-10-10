# Capture plan: demo GIF and screenshots

What to record for the launch, and how to stage it so no real project, path or session
ever reaches a frame. The set in the repo (the hero, the deck, `blink.gif`, the tasks page, the
previews grid and the token tooltip) was captured from 0.11.17; the phone page has not been
captured yet.

## Deliverables

| File | What | Size target |
|---|---|---|
| `site/assets/hero.gif` (the README hero, and the landing page's fallback) | The main loop, scenes 1-5 below | 1280 px wide or less, 12-15 fps, under 18 s, under 5 MB |
| `site/assets/hero.mp4` (what the landing page plays in a narrow window) | The same take as the GIF | H.264, same frame size as the GIF |
| `site/assets/hero-2x.mp4` (what the landing page plays in a wide window) | The same frames, every pixel doubled | H.264, twice the frame size of the GIF |
| `assets/screenshots/deck.png` | The deck with four workspaces, mixed states | PNG, 100% display scale |
| `assets/screenshots/blink.gif` | One session card blinking orange | about 430 px wide, 3 s loop |
| `assets/screenshots/tasks-page.png` | The tasks page with a sample tasks file | PNG |
| `assets/screenshots/grid.png` | The deck as a first run shows it, window previews on: four cards in two columns | PNG, 100% display scale |
| `assets/screenshots/token-tooltip.png` | The hover text of a session's token chip | PNG |
| `assets/screenshots/phone.png` (new) | The phone page | phone portrait, about 390 px wide |

The August `deck.png` and `tasks-page.png` showed the former product name, a workspace with a
non-English name, and `done` drawn green (the old colour scheme: it has been purple since
config schema 3, and green now means `wrapped`). Both were replaced in 0.11.17 with captures of
0.11.15, and again in 0.11.18 with captures of 0.11.17. The August `blink.gif`, which showed
`done` in green, was replaced in 0.11.18 as well.

The hero has one home. Its three files live under `site/assets/`, next to the landing page, so the
`site/` folder can be published as it is; the README shows the GIF from that same path. Keep
the file names when a newer take replaces them.

Four things on screen changed in 0.11.17. The captures in the repo show all four; a capture
of 0.11.15 that shows one of them is out of date:

- The line under a session waiting on a permission dialog. It reads
  `Waiting for permission: Bash: <command>` for up to the first ten seconds, then
  `Waiting for permission: Bash` for as long as the session waits. Record after the line has
  settled, so it does not change in the middle of a take.
- The status line after a click: `Opening the session in VS Code: <title>`, two words.
- The tasks page. With a tasks file whose titles are all left-to-right, every title is
  left-aligned and they start on one vertical line.
- The search row. On a narrow deck the search box is now a real box (about 250 px in a 40%
  zone) and no longer a sliver. If a tasks file is configured while the deck is that narrow,
  the Run box sits on a second line under it.

Colour check for any take that is recorded as video: the app draws `waiting` as (251, 140, 0),
`idle` as (158, 158, 158), `working` as (30, 136, 229) and `your turn` as (142, 36, 170), and
every still in the repo shows exactly those values (the hero GIF is within one). The earlier
hero was recorded through the Desktop Duplication API, which on that display returned every
colour about 1.44 times too bright (idle read (227, 227, 227), near white); the current take
was recorded with GDI, which returns the app's own values. Sample one border in the finished
file before it ships; the idle border must not read as white, because white is the colour of
a session that handed off.

An MP4 needs one more step. 4:2:0 video keeps colour at half the resolution of brightness, so
in `hero.mp4` the 2 px card borders come out dull: `waiting` reads about (218, 143, 56). The
fix is `hero-2x.mp4`: the same frames scaled to twice the size with nearest-neighbour, so that
every pixel of the take owns a whole colour sample (`waiting` reads about (244, 139, 9)).
Encode it from the lossless take, never from the GIF or the 1280 file:

```
ffmpeg -framerate 15 -i f%03d.png -vf "scale=2560:1824:flags=neighbor,scale=in_range=pc:out_range=tv:out_color_matrix=bt709,format=yuv420p" -c:v libx264 -profile:v high -preset slow -crf 23 -colorspace bt709 -color_primaries bt709 -color_trc bt709 -color_range tv -movflags +faststart -an hero-2x.mp4
```

The landing page gives the 2x file to wide windows only. Its frame is above 1080p (H.264
level 5.0), which some phone decoders do not take, so a narrow window keeps `hero.mp4`.

A first run shows window previews: the setting is on by default, so each workspace card with
an open window carries a 170 px live picture of it. The hero, `deck.png` and `blink.gif` were
taken with it switched off, and the README says so under `grid.png`, which shows it on.

## Scenes (in order, under 20 s)

1. **0-3 s. The deck at rest.** TabTower on the right third of the screen (Reserved Zone
   on), four workspace cards: `api-server`, `web-app`, `docs`, `data-jobs`. Sessions in
   mixed states: one blue (working), one purple (done), one grey (idle). A VS Code window
   for `web-app` fills the rest of the screen, the cursor typing in it.
2. **3-7 s. A session needs you.** The `api-server` session asks for permission to write a
   file. Its card turns orange and blinks. Hold on it for a full second.
3. **7-10 s. One click.** The cursor moves to the blinking card and clicks.
4. **10-14 s. Landing on the tab.** The `api-server` VS Code window comes to the front, the
   right Claude Code tab is active with the permission prompt on screen, and the blink on
   the deck stops.
5. **14-18 s. Back to work.** Approve the prompt; the card turns blue. End on the calm deck
   for half a second so the loop restart is not abrupt.

Optional, as a separate short clip and not in the main GIF: the Windows notification and
taskbar badge appearing while the deck is covered, then withdrawing once the session is
answered.

## Staging environment

**TabTower cannot run a second, separate instance on the same Windows account, and has no
config path override** (checked in the code):

- `Program.cs` takes a single-instance mutex, `TabTower_SingletonMutex`; a second UI launch
  only activates the running one and exits.
- `Services/ConfigStore.cs` fixes the config folder to `%APPDATA%\TabTower`
  (`config.json`, `toggles\`, `logs\`), with no command-line switch or environment
  variable to move it.
- `Services/PipeServer.cs` fixes the pipe to `\\.\pipe\tabtower`, and the hook installer
  writes to `%USERPROFILE%\.claude\settings.json`. So the hooks of the everyday Claude Code
  setup would also report real sessions to a staged deck on the same account.

A second Windows user on the same PC does not solve it either: named pipes are machine-wide,
so the two decks would compete for one pipe name.

**Recommended: Windows Sandbox** (Windows 11 Pro; the optional feature has to be turned on
once, with admin rights, then a reboot). It is a separate, disposable Windows with its own
`%APPDATA%`, its own `.claude` folder and its own pipe namespace, so the everyday TabTower,
its config and its sessions are never touched, and closing the sandbox deletes everything.
A Hyper-V VM works the same way if Sandbox is unavailable. Do one dry run first to confirm
the live window thumbnails render inside the sandbox's remote display.

Inside the sandbox:

1. Install VS Code and the Claude Code extension, and sign in to Claude Code (a manual
   step). Use a throwaway sign-in or an API key with a small spending limit if possible.
2. Install TabTower from the release zip with `install.ps1`, exactly as a new user would.
   That also proves the install path on a clean machine.
3. Create four sample folders, each a git repo with a few placeholder files and a branch:
   `C:\demo\api-server` (`feat/rate-limit`), `C:\demo\web-app` (`main`),
   `C:\demo\docs` (`main`), `C:\demo\data-jobs` (`fix/retry`). No real code.
4. Open each in its own VS Code window and add each as a workspace (`tabtower add C:\demo\api-server`, and so on).
   Give the cards distinct colours with `tabtower set <id> --color <c>`.
5. Start one real Claude Code session per card with a neutral prompt ("Add a rate limiter
   to server.js", "Fix the search box", "Update the guide", "Retry failed imports"). For
   scene 2, the `api-server` prompt asks Claude to create a new file, so it stops on a
   genuine permission request. Set VS Code to a dark theme and a large font (16 px or more)
   so text stays readable after scaling.
6. Turn on the tasks panel only for the tasks screenshot, with a sample JSON whose tasks use
   invented ids and titles.

Use real sessions for every card in the shot. Cards made only with `tabtower session start`
have no VS Code tab behind them, and the reconcile sweep closes cards whose tab is gone,
so they may vanish mid-take.

Fallback if no sandbox or VM is possible: quit TabTower, rename `%APPDATA%\TabTower` to a
backup, record, then quit and rename it back. This does touch the everyday setup (and every
real session would appear on the deck), so it is the last resort, not the plan.

## Capture tool and settings

- **ScreenToGif** (free, Windows): record a fixed region around the sandbox window at 15 fps,
  then trim, delete idle frames and export. Use its encoder with "detect unchanged pixels" to
  keep the file small. Also export an MP4: it is far smaller, the landing page can use it,
  and GitHub plays an MP4 that is dropped into the README through the web editor.
- Display scale 100% inside the sandbox, sandbox window about 1600x900, so a 1280-wide
  export is a light downscale.
- Mouse: enable click highlighting in ScreenToGif so the click in scene 3 reads.
- Turn off Windows notifications from anything else, hide the desktop icons and use a plain
  wallpaper inside the sandbox.

## Phone scene

The phone page accepts a request addressed to `localhost` from the PC itself (see
`docs/phone-access.md`, "How it is protected"), so it can be captured without a phone or a
tailnet:

1. In the sandbox, tick Phone access in TabTower's settings.
2. Open `http://127.0.0.1:7055/` in Edge, open the developer tools and switch to device mode
   at 390x844.
3. Screenshot the session list with the same four sample workspaces, one session waiting.
   For a moving clip, record opening a new session from the page and closing one.

A real phone clip (pairing with the four-character code, then the list) is a nice extra,
but it needs Tailscale inside the sandbox joined to a tailnet, and the phone's own screen
recorder. If recorded, crop out the status bar and any notification.

## Before publishing any capture

- Step through every frame: no real name, path, project, email, session id or tailnet
  machine name. The sandbox user name appears in paths; rename the sandbox user or keep
  paths out of frame.
- Check the title bar reads TabTower, not the former name.
- Keep the GIF under 5 MB and every screenshot under 1 MB so the pages load fast.
