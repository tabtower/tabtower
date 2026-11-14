# Phone access

TabTower can serve a small page for your phone that shows your Claude Code sessions the way
the deck groups them, and lets you act on them from anywhere on your tailnet:

- see every open session with its status (working, needs you, your turn, idle, error) and
  how long ago it was last active, worded and coloured as the deck card shows it: a question
  board says how many questions wait for you (or that none do), a session waiting for a run or
  a monitor of its own says so instead of "your turn", so does a turn that only answered another
  session in a window with a question board, and pinned sessions come first, marked 📌,

- open a new session in one of your VS Code windows (it asks first, because it starts work
  on your PC),
- close a session together with its VS Code tab (it asks first),
- reopen a recently closed session with its whole conversation (no question: closing it
  again undoes it),
- jump straight into a session in the Claude app, when Claude Code has published a Remote
  Control link for it,
- when a tasks file is configured, work the tasks page from the phone too (see
  [The Tasks tab](#the-tasks-tab)).

The page follows the phone's language: English by default, and any other language TabTower
has a string table for (Hebrew is included, shown right to left).

It is **off by default**. Nothing listens until you turn it on.

## Requirements

- **Tailscale** on the PC and on the phone, both logged in as the **same Tailscale user**.
  The free personal plan is enough.
- Optional, for the "Open in the Claude app" links: Claude Code's **Remote Control** turned
  on for your sessions. Without it the page works the same, the app links are just not shown.

## Turn it on

1. In TabTower, open ⚙ and tick **Phone access**. The page now listens on
   `127.0.0.1:7055` on this PC only. (⚙ → **Phone access settings...** changes the port.)
2. Make it reachable from your tailnet. Run this once in a terminal on the PC; Tailscale
   remembers it across restarts:

   ```
   tailscale serve --bg http://127.0.0.1:7055
   ```

   If this PC already serves something else on the default HTTPS port, pick another one,
   for example `tailscale serve --bg --https=8443 http://127.0.0.1:7055`, and add `:8443`
   to the address below.
3. On the phone, open `https://<pc-name>.<tailnet>.ts.net/`. The settings dialog shows the
   exact address for this PC.

**Never use `tailscale funnel` for this.** The page refuses traffic that comes from the
public internet, but a funnel would still publish your PC's address.

To stop serving it later: `tailscale serve --https=443 off` (or the port you chose), and
untick **Phone access**.

## Pairing a device

The first time a phone (or any other device) opens the page, it shows **Waiting for approval**
and a four-character code. At the same moment TabTower on the PC shows a prompt with the
device's Tailscale node name, the name the device gives itself, its OS and its node ID. The
prompt does **not** show the code.

- To **approve**, type the code from the phone's screen into the prompt. A wrong code is
  refused and the request stays open, so you can check the phone and type it again.
- **Deny**, or closing the prompt in any other way, refuses it; that device cannot ask again
  for 10 minutes.
- An unanswered request expires after 10 minutes.
- At most two requests wait at a time. A third device is told the PC is busy and to try
  again later, without a new prompt appearing.

Why typing: a device's names are chosen by whoever controls that device, so another machine
on your tailnet could call itself "iPhone" and ask at the same moment as your phone. Only
the device in your hand shows the code that matches its own request.

The prompt never takes the keyboard focus and has no default button, so a stray Enter while
you type elsewhere cannot answer it. Requests waiting for an answer are also listed in
⚙ → Phone access settings, where they can be denied.

The phone page opens by itself a few seconds after you approve. Approval lasts until you
revoke it.

## Revoking a device

⚙ → **Phone access settings...** lists every approved device with when it was approved and
last used. **Revoke** removes it at once; its next visit starts a new pairing.

If a phone is lost, revoke it here, and also remove it from your tailnet in the Tailscale
admin console.

## Choosing the window

When a card can open a session in more than one VS Code window (a folder with several session
groups), its **+ Session** button opens a sheet with one button per window: the card's own
window first, then the others. A window that is not running says so; picking it starts it, and
the session opens there once it connects (about a minute). The same sheet is used by a task's
**▶ Session**.

## The Tasks tab

When TabTower has a tasks file (⚙ → Tasks file), the page gets a second tab, **Tasks**, that
shows what the tasks page on the PC shows:

- the cards of the level on screen, pinned ones first, with their status colours,
- the top-level navigation squares (and the home square, when the file has one) as a row of
  buttons; the square of the level on screen is highlighted,
- **▶ Session** on every card that names a folder: it asks which VS Code window to use (one
  button per session group, the same window a click on the PC with that key held would use,
  plain click first) and, when the task has one, offers its fast variant,
- the card's own link button: a drill-in link (a script, a file in the tasks file's own
  folder) is followed **on the PC**, and a web address opens on the phone,
- a **Run** box that opens a session for a task by its number, from anywhere in the file's
  launch index, like the toolbar's Run box.

The phone keeps no view of its own. Following a link runs it on the PC exactly as a click there
would, the producer rewrites the tasks file, and the page shows the new level once TabTower has
reloaded it. The tasks page on the PC moves with it.

What the phone can and cannot ask for:

- A session opened from a task always starts with the task's launch phrase from the tasks file
  (`sessionPrompt` / `newSessionPrompt`, or the fast variant). The phone sends the task's
  number, the window and the fast flag, never any text of its own.
- The phrase is typed into the new tab's input box on the PC and **not sent**, exactly as on the
  PC, where you review it and press Enter. From the phone the page shows the phrase and offers
  **Copy it and open in the app**: the phrase goes to the clipboard and the session opens in the
  Claude app, where you paste and send it.
- A link is followed only when the card or square that carries it is on the current level of
  the file. The phone names the card; the link itself is read from the file on the PC, so the
  page cannot make the PC open anything the file did not already offer.
- A link is followed only when it is a file or folder inside the tasks file's own folder, or
  below it. Anything else is refused and the page says so (see
  [What an approved phone can make the PC do](#what-an-approved-phone-can-make-the-pc-do)).

## How it is protected

Every request goes through these checks before anything else happens:

1. **Funnel traffic is refused.** Tailscale marks a request that came through a funnel, and
   such a request is rejected outright.
2. **The Host header must be `localhost`, `127.0.0.1`, `[::1]`, or exactly this PC's own
   Tailscale name.** This stops a web page that points its own domain at `127.0.0.1` (DNS
   rebinding), or a request addressed to some other machine, from talking to the port.
3. **The device is identified by Tailscale, not by anything it sends.** `tailscale serve`
   sets `X-Forwarded-For` to the caller's tailnet address, overwriting whatever the caller
   sent. TabTower resolves that address with `tailscale whois` to a device, and then:
   - the device must belong to the **same Tailscale user** as the PC (a tagged device, or a
     device shared in from another account, is refused),
   - and the device's Tailscale **node ID** must be on your approved list. Each device has
     its own node ID, bound to its own WireGuard key, so a second machine logged in as you
     still has to be approved on its own, with the code from its own screen.

   A request **without** `X-Forwarded-For` did not come through `tailscale serve` in its
   normal (HTTP) mode. It is accepted only when it is addressed to `localhost` /
   `127.0.0.1` / `[::1]`, which is what a browser on the PC itself sends. Addressed to the
   PC's Tailscale name without that header, it came through some other forwarder
   (`tailscale serve --tcp` or `--tls-terminated-tcp`, an SSH or editor port forward, WSL)
   and is refused.
4. **API calls need a custom header** (`X-TabTower-Phone`). A browser will not send a custom
   header to another site without asking it first, and this server never says yes, so a
   different website open on the phone cannot drive the page with the phone's identity.

Nothing secret is stored on the phone: there is no password, token or cookie to steal. The
identity is the device itself, as Tailscale already authenticates it.

The page only ever listens on `127.0.0.1`. It is a plain socket on the loopback address,
which needs no administrator rights and is not reachable from your network. It handles at
most 32 connections at a time and answers any more with "busy".

Only sessions the page lists can be closed from it: an id for a session it does not show
(one that already ended, a headless run, a replaced session) is refused.

### What an approved phone can make the PC do

With a tasks file configured, an approved device can do two things on the PC that go beyond
looking at sessions. Weigh them before you approve a device:

- **Start sessions.** It can open a new Claude Code session for any task in the file's launch
  index, in any of your session-group windows, and that can start a VS Code instance that is not
  running. The session always starts with the task's own launch phrase from the file, and the
  phrase is typed into the new tab, not sent: nothing runs until you, or the phone through the
  Claude app, send it.
- **Run the file's links on the PC.** Following a card's link runs that link on the PC as a
  double-click there would (Windows' shell "open"), but only when the link is a file or folder
  that exists inside the tasks file's own folder, or a folder below it. The link must also
  already be in the tasks file, on a card or square of the level on screen, and the phone can
  only name the card, never send a link of its own.

How TabTower decides, for a link that is not an `http://` or `https://` address:

1. It must be a full local path with a drive letter, such as `D:\work\tasks\next.vbs`.
   A network share (`\\server\share\...`), a device path (`\\?\...`, `\\.\...`), a relative
   path, an alternate data stream or any `something:` link (`file:`, `ms-settings:`, a custom
   app link) is refused.
2. The path is made canonical first (Windows' `GetFullPath`), so a `..` that climbs out of the
   folder is caught after it is resolved, not before.
3. The result must be the tasks file's folder or sit below it. The comparison ignores case and
   needs a folder separator after the folder name, so a sibling folder such as `tasks-old`
   does not count as inside `tasks`.
4. The file or folder must exist.

A refused link answers `not_allowed` to the phone, which shows that the link opens on the PC
only, and the deck log gets one line naming the card and the reason. A click on the PC still
opens any link, as before. If your tasks file lives on a network share, no link runs from the
phone.

This does not cover a script placed in the tasks folder itself: it runs from the phone like
any other file there. Treat write access to that folder like write access to the tasks file,
and if your file is written by a tool that takes text from elsewhere (issue titles, web pages,
other people), make sure that tool never turns such text into a file in that folder, or leave
Phone access off.

### Caveats

- **Local requests are trusted.** A request addressed to `localhost` with no
  `X-Forwarded-For` is treated as the PC's owner. On a PC shared by several Windows accounts,
  another account can connect to `127.0.0.1:<port>` directly and use the page as you, and so
  can any local tool that forwards traffic to it while rewriting the Host to `localhost`. If
  that matters on your PC, leave Phone access off.
- **Reinstalling Tailscale on the phone, or logging it in again, creates a new device.** It
  has to be paired again; revoke the old entry.
- **A stolen phone that is unlocked and still on your tailnet is still approved** until you
  revoke it or remove it from the tailnet.
- **The app links depend on an undocumented Claude Code file.** TabTower reads
  `~/.claude/sessions/*.json` (or the folder under `CLAUDE_CONFIG_DIR`) for the Remote Control
  session id. If a future Claude Code changes that file, the page keeps working and simply
  stops showing app links.
- **A brand-new session that has not been sent a message yet is not listed**, as on the deck.
  The page hands you its app link right after it opens.

## Checking it without a phone

`tests/phone.tests.ps1` builds a test harness that runs the same server code against a fake
deck, without touching a running TabTower, and checks the rules above over real sockets.
For a request from another tailnet device, run the harness in `serve` mode, add a
temporary `tailscale serve --bg --https=<spare port> http://127.0.0.1:<harness port>`, and
request the page from the other device: it must answer `pairing` with a code until the
device is approved through the harness's control port with that code (a wrong code must be
refused), and a forged `X-Forwarded-For` must not change which device is identified. A
second temporary `tailscale serve --bg --tls-terminated-tcp=<spare port> tcp://127.0.0.1:<harness
port>` must answer `not_forwarded`. Remove the temporary serves afterwards.
