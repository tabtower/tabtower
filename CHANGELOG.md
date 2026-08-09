# Changelog

Every version of SessionDeck, newest first.

Only the current release stays on the
[Releases page](https://github.com/eyalBPM/SessionDeck/releases) - older ones are deleted
so the page never accumulates self-contained zips nobody downloads. Their **tags survive**,
so any version below can be rebuilt from source with `git checkout v<version>` followed by
a publish. This file is the history the Releases page cannot hold; `release.ps1` prepends
to it automatically.

<!-- new releases are inserted directly below this line -->

## v0.9.9 - 2026-08-09

- chore: sync hook script version header to 0.9.9
- fix(sessions): close a workspace's sessions when its VSCode window exits (T-0362)

## v0.9.8 - 2026-08-09

- chore: sync hook script version header to 0.9.8
- fix(zone): remove the full-screen zone, cap custom at 90% (T-0364)

## v0.9.7 - 2026-08-09

- chore: sync hook script version header to 0.9.7
- feat(ui): dark native title bar matching the card header (T-0361)
- docs(claude): remove the hazak-uvaruch chapter (T-0337)
- docs(claude): add hazak-uvaruch task-finish chapter (T-0337)

## v0.9.6 - 2026-08-05

- chore: sync hook script version header to 0.9.6
- chore(release): one release on GitHub, permanent tags, CHANGELOG.md
- chore: prepare the repo and the Marketplace listing for promotion (T-0342)

## v0.9.5 - 2026-08-05

- chore: sync hook script version header to 0.9.5
- fix: sweep ghost sessions whose transcript was declared but never written

## v0.9.4 - 2026-08-04

- fix: sync the hook version header, and stop hardcoding a version in the README
- docs: sync the README status line to v0.9.4
- docs: say up front what SessionDeck expects you to be running
- docs: rebuild the README around real screenshots (T-0240)
- fix(sessions): remove ghost sessions that never carried a transcript path (v0.9.4)

## v0.9.3 - 2026-08-04

- chore(extension): sync package-lock to the 0.6.11 bump
- fix(extension): give the VSIX a repository field so vsce can package it (v0.9.3)

## v0.9.2 - 2026-08-04

- docs: replace the executed plan documents with CLAUDE.md and skills (v0.9.2)

## v0.9.1 - 2026-08-04

- docs(i18n): translate the docs to English, stage 2 of T-0239 (v0.9.1)

## v0.9.0 - 2026-08-04

- feat(i18n): move the whole UI to English, stage 1 of T-0239 (v0.9.0)

## v0.8.1 - 2026-08-04

- fix(hooks): scope the hook-confirmed dialog to its own pending call
- docs(hooks): describe the scan-mark bound and the subagent limitation
- fix(hooks): bound the permission-dialog hold to one scan cycle
- fix(hooks): hold a PermissionRequest wait until the scanner corroborates it

## v0.8.0 - 2026-08-04

- feat(hooks): PermissionRequest + StopFailure + Elicitation (7 -> 11 events)

## v0.7.9 - 2026-08-03

- feat(connector): SessionDeck logo in the extension icon and the Output log

## v0.7.8 - 2026-08-02

- chore: sync hook script version header to 0.7.8
- v0.7.8: bump for the docs release

## v0.7.7 - 2026-08-02

- docs: repo is public - correct the stale visibility facts
- fix(release): vsce stderr warning aborted the release under EAP=Stop
- v0.7.7: titled sessions don't match prompt-based tab labels (T-0313)

## v0.7.6 - 2026-08-02

- v0.7.6: empty string instead of null for non-nullable Detail (T-0313)

## v0.7.5 - 2026-08-02

- v0.7.5: fix fork-phantom waiting/working card states (T-0313)

## v0.7.4 - 2026-07-28

- v0.7.4: pressed look for card toggle buttons (T-0236)

## v0.7.3 - 2026-07-27

- chore: sync hook script version header to 0.7.3
- v0.7.3: slim dark scrollbars + tasks-page header polish (T-0234)

## v0.7.2 - 2026-07-27

- v0.7.2: rounded corners on every button (T-0233)

## v0.7.1 - 2026-07-27

- v0.7.1: tasks button left of pin + header overflow fixes (T-0233)

## v0.7.0 - 2026-07-27

- fix: order-insensitive pending-call scan in TranscriptReader
- v0.7.0: read-only tasks panel fed by an external JSON file (T-0116)

## v0.6.43 - 2026-07-27

- chore: sync hook script version header to 0.6.43
- v0.6.43: apply Stage on bind when the pinned workspace had no open window

## v0.6.42 - 2026-07-26

- v0.6.42: orphan-session reconciliation - close sessions whose host died without SessionEnd

## v0.6.41 - 2026-07-26

- v0.6.41: release.ps1 - skip local tag delete when the tag is already gone

## v0.6.40 - 2026-07-26

- chore: sync hook script version header to 0.6.40
- v0.6.40: remove empty VSCode warmup sessions from workspace history

## v0.6.39 - 2026-07-22

- v0.6.39: hide DWM thumbnail with a clean call when card scrolls out

## v0.6.38 - 2026-07-22

- v0.6.38: diagnostic logging (issues 2+3 groundwork)

## v0.6.37 - 2026-07-22

- v0.6.37: enforce a usable minimum width (window + zone) and smarter icon wrap

## v0.6.36 - 2026-07-21

- v0.6.36: rebind window to the right card on same-window Open Folder

## v0.6.35 - 2026-07-21

- v0.6.35: clip DWM thumbnails to the scroll viewport

## v0.6.34 - 2026-07-21

- chore: sync hook script version header to 0.6.34
- v0.6.34: release policy - one release per major.minor line

## v0.6.33 - 2026-07-21

- fix(release.ps1): publish into a clean output dir
- v0.6.33 packaging: release.ps1 - one-command GitHub release
- v0.6.33: zone mode stretches visible frame to fill the full zone

## v0.6.32 - 2026-07-21

- v0.6.32: zone quarters/custom modes + lock; UI style pass

## v0.6.29 - 2026-07-20

- v0.6.29: packaged release - install-hooks, quit, installer scripts

## v0.6.28 - 2026-07-20

- docs: retarget the packaging plan at v0.6.29
- docs: add the two silent-failure gaps the installer must fix
- v0.6.28: stop auto-acknowledge from silencing a blink after the user left the tab

## v0.6.27 - 2026-07-20

- docs: add packaging and distribution plan for v0.6.28
- docs: add MIT license, .gitattributes, accurate install steps
- docs: add README for the public GitHub repo
- v0.6.27: withdraw the Windows notification when its cause is gone

## v0.6.26 - 2026-07-20

- v0.6.26: escalate attention to Windows when the deck can be buried

## v0.6.25 - 2026-07-20

- v0.6.25: card title follows the VSCode tab label

## v0.6.24 - 2026-07-20

- v0.6.24: correlate tabs by a candidate set, not one title

## v0.6.23 - 2026-07-20

- v0.6.23: neutral flags redesign + app-wide RTL handling

## v0.6.19 - 2026-07-20

- v0.6.19: detect "waiting" from the transcript - VSCode UI fires no Notification hook

## v0.6.16 - 2026-07-20

- v0.6.16: add "copy path to clipboard" to workspace card menu

## v0.6.15 - 2026-07-20

- v0.6.15: fix auto-ack lost to tab-title drift + split-group false acks

## v0.6.14 - 2026-07-19

- v0.6.14: status-bar summary dots, toggle agent-prompt copy button, app icon

## v0.6.13 - 2026-07-19

- v0.6.13: custom toolbar toggles - flag files for external hooks, CLI, GUI editor

## v0.6.12 - 2026-07-19

- v0.6.12: settings toggle for collapsing VSCode panels on session open + RTL settings menu

## v0.6.11 - 2026-07-19

- v0.6.11: pin toggle (always on top) + pressed-state UI for pin and search toggles

## v0.6.10 - 2026-07-19

- v0.6.10: release DWM thumbnail when card is collapsed (search filter / hide)

## v0.6.9 - 2026-07-19

- v0.6.9: search/filter row - workspaces+sessions by fields, optional transcript content search

## v0.6.8 - 2026-07-19

- v0.6.8: session card slimmed - secondary title removed, tooltip trimmed to detail+times, tooltip RTL

## v0.6.7 - 2026-07-19

- v0.6.7: RTL for the no-window placeholder text

## v0.6.6 - 2026-07-19

- v0.6.6: Stage "full" performs a real maximize

## v0.6.5 - 2026-07-19

- v0.6.5: + New Session button on workspace cards

## v0.6.4 - 2026-07-19

- v0.6.4: workspace card actions consolidated into one menu + close-window action

## v0.6.3 - 2026-07-19

- v0.6.3: phantom sessions hidden + auto-closed (stale)

## v0.6.2 - 2026-07-19

- v0.6.2: tab switch bumps session to top (extreme activity sort)

## v0.6.1 - 2026-07-19

- v0.6.1: /rename support, truncated-label matching, question-form hooks, bottom status bar

## v0.6.0 - 2026-07-19

- v0.6.0: tab titles as primary, reliable session-tab correlation, auto-acknowledge, history, RTL

## v0.5.0 - 2026-07-19

- Stage D: VSCode extension connector, tab open/resume from deck, transcript titles (v0.5.0)

## v0.4.0 - 2026-07-19

- Stage C: Workspace/Session Cards, hooks integration, session engine (v0.4.0)

## v0.3.0 - 2026-07-17

- Rename WinGrid -> SessionDeck (v0.3.0, decisions 19-20)
