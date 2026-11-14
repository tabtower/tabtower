namespace TabTower.Services;

/// <summary>Which tabless session may take which unowned tab when MainWindow's
/// ClaimTheLastTabByElimination hands tabs out by headcount (0.11.59).
///
/// The headcount was taken over the whole card, and a card is a FOLDER: three VS Code instances
/// on one folder are one card, so a tab open in one window counted as a candidate for a session
/// stamped in another. Measured 30-10-2026 20:09: a session stamped in window A had wrapped up and
/// lost its tab, and took, as "the single pair", a dormant tab that only window B was showing;
/// its card in A then carried the name of B's tab.
///
/// A tab can only belong to a session of a window that SHOWS it. So a session whose window is
/// known may take a tab only when that window is among those reporting it, or when one of those
/// windows could not be identified ("" here). Anything the deck does not know (a session with no
/// stamp, a label no window was mapped for) stays claimable exactly as before, because this rule
/// only ever REMOVES a claim and a claim is what keeps the orphan sweep off a live card.
///
/// Free of WPF so a test harness can compile it (tests/elimination-scope.tests.ps1).</summary>
public static class EliminationScope
{
    /// <summary>Can this session own a VS Code tab at all, by how it was launched? A headless run
    /// (`claude -p` / the SDK) has no tab by construction, so it may neither take an unowned tab by
    /// headcount nor be named as the closed session behind one.
    ///
    /// Both callers in MainWindow used to test PrintMode alone, and the hook never sends print
    /// mode for an sdk-* entrypoint, so an SDK run passed as tab-capable. Measured 02-11-2026
    /// 18:45:16: a background SDK run (entrypoint sdk-cli, stamped in the window it was launched
    /// from) was the only titled tabless session there when a freshly opened tab was renamed. It
    /// took that label as "the single pair" and kept it as its card title, while the session
    /// really inside the tab had no title yet (0.11.60).
    ///
    /// A `claude --bg` run is the same shape with another signal (0.11.64): entrypoint "cli", no
    /// print mode, but sessionKind "bg" in its transcript. Measured 03-11-2026 16:24:54, it took
    /// the label "Claude Code" of an IDE tab that was not its own, by elimination.
    ///
    /// A person's own session outside VS Code (a terminal, Claude Desktop) has no IDE tab either
    /// (0.11.68, <see cref="SessionHost"/>), and until now it stood in the elimination count like an
    /// IDE session: measured 12-11-2026 17:16:52, a Desktop session was listed as a tabless
    /// session waiting for an unowned tab of its window, so any spare "Claude Code" tab there would
    /// have become its label. An unknown entrypoint still may.</summary>
    public static bool MayHoldTab(bool printMode, string? entrypoint, string? sessionKind)
        => !HeadlessRun.Is(printMode, entrypoint, sessionKind) && !SessionHost.IsOutsideIde(entrypoint);

    /// <summary>May a session stamped in <paramref name="sessionGroup"/> (empty when unknown)
    /// take a tab shown by windows of <paramref name="tabGroups"/> (empty string for a window
    /// whose group could not be identified; null or empty when nothing is known)?</summary>
    public static bool MayClaim(string sessionGroup, IReadOnlyCollection<string>? tabGroups)
        => sessionGroup.Length == 0 || tabGroups == null || tabGroups.Count == 0 ||
           tabGroups.Contains("") ||
           tabGroups.Any(g => string.Equals(g, sessionGroup, StringComparison.OrdinalIgnoreCase));

    /// <summary>Narrow both sides of the headcount to what could possibly pair: a session with no
    /// tab it may take cannot own any of them, and a tab no remaining session may take cannot
    /// belong to any of them. Order is kept on both sides (the caller's ordering decides who goes
    /// first), and duplicates in <paramref name="tabs"/> stay, since two tabs with one label are
    /// two tabs.</summary>
    public static (List<TSession> Sessions, List<string> Tabs) Scope<TSession>(
        IReadOnlyList<string> tabs, IReadOnlyList<TSession> sessions, Func<TSession, string> group,
        Func<string, IReadOnlyCollection<string>?> tabGroups)
    {
        var keptSessions = sessions.Where(s => tabs.Any(t => MayClaim(group(s), tabGroups(t)))).ToList();
        var keptTabs = tabs.Where(t => keptSessions.Any(s => MayClaim(group(s), tabGroups(t)))).ToList();
        return (keptSessions, keptTabs);
    }

    /// <summary>Hand out at most <paramref name="max"/> tabs, sessions in the order given, each
    /// taking the first still-free tab it may claim. A session left with none is skipped rather
    /// than handed a tab from another window.</summary>
    public static List<(TSession Session, string Tab)> Pair<TSession>(
        IReadOnlyList<TSession> sessions, IReadOnlyList<string> tabs, Func<TSession, string> group,
        Func<string, IReadOnlyCollection<string>?> tabGroups, int max)
    {
        var free = tabs.ToList();
        var pairs = new List<(TSession, string)>();
        foreach (var s in sessions)
        {
            if (pairs.Count >= max) break;
            int i = free.FindIndex(t => MayClaim(group(s), tabGroups(t)));
            if (i < 0) continue;
            pairs.Add((s, free[i]));
            free.RemoveAt(i);
        }
        return pairs;
    }
}
