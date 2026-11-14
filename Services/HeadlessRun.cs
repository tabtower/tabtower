namespace TabTower.Services;

/// <summary>Is a session a headless run (`claude -p` / the SDK) rather than one a user opened?
/// The one definition SessionViewModel.IsHeadless and EliminationScope.MayHoldTab both read
/// (0.11.60), so the deck cannot again ask "headless?" two different ways.
///
/// Three signals, any one enough. PrintMode is the hook's proof from the process's own command
/// line, and the hook only looks when the entrypoint claims the IDE (hooks/tabtower-hook.ps1,
/// Test-PrintModeRun: "sdk-* classifies itself"). So a run whose entrypoint is sdk-* NEVER gets
/// PrintMode, and a check of PrintMode alone misses every one of them: measured 02-11-2026, a
/// background SDK run (transcript entrypoint sdk-cli) took the label of an IDE tab by elimination.
///
/// The third is the session kind (0.11.64). A `claude --bg` run reports entrypoint "cli", the same
/// as a terminal session, so neither signal above sees it; what tells it apart is the
/// <c>sessionKind</c> its transcript lines carry ("bg"; an IDE session's lines carry none). Measured
/// 03-11-2026: a `--bg` run (entrypoint cli, sessionKind bg) took, by elimination, the label of an
/// IDE tab that was not its own, and its card then counted as a session of a window. The kind is
/// the narrow signal on purpose: the entrypoint "cli" alone would also catch a terminal session.
///
/// Deliberately a blacklist of the SDK entrypoints rather than a whitelist of the interactive
/// ones: an unknown entrypoint read as interactive is today's behaviour, while one read as
/// headless would silently swallow a session the user is waiting on. Free of WPF so a test
/// harness can compile it (tests/elimination-scope.tests.ps1).</summary>
public static class HeadlessRun
{
    public static bool Is(bool printMode, string? entrypoint, string? sessionKind)
        => printMode ||
           (entrypoint != null && entrypoint.StartsWith("sdk", StringComparison.OrdinalIgnoreCase)) ||
           string.Equals(sessionKind, BackgroundKind, StringComparison.OrdinalIgnoreCase);

    /// <summary>The <c>sessionKind</c> a `claude --bg` run writes on every transcript line.</summary>
    public const string BackgroundKind = "bg";
}
