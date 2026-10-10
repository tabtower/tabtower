## What this changes

<!-- What a user of TabTower will notice, in a sentence or two. Link the issue if there is one
("Fixes #123"). -->

## Why

## How I verified it

<!-- Which tests you ran, and what you checked by hand with real sessions. The test suites do
not cover card status or blinking, so a change there needs a manual check. -->

- [ ] `tests\install-hooks.tests.ps1` passes against the exe I built
- [ ] `tests\doctor.tests.ps1` passes against the exe I built
- [ ] Checked by hand:

## Checklist

- [ ] `<Version>` in `TabTower.csproj` is bumped (code changes only; checked against the current `main`)
- [ ] `vscode-extension/package.json` version is bumped, if the extension changed
- [ ] `hooks/README.md` and `Cli/HookInstaller.cs` still match, if hooks changed
- [ ] Commit subjects follow the `feat:` / `fix:` / `docs:` / `chore:` style
