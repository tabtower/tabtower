namespace TabTower.Services;

/// <summary>The decision behind <c>tabtower tabs close --group &lt;id&gt; --label "..."</c> (0.11.59): close a
/// Claude tab that NO session card answers to, by its label, without revealing anything. Kept apart
/// from the window so tests/tablabel/TabLabelHarness can link this file and test it without the app.
///
/// Why it exists. When an extension or VS Code update ends a session's process, the deck closes the
/// card about 60 s later (its tab left VS Code), and VS Code restores the tab when the window comes
/// back. That tab now belongs to no card: <c>session end --id</c> answers "unknown session id", and
/// every other close path is reached through a card. Measured 30-10-2026: a session that had handed
/// off to a successor left its dead tab beside the successor's until the user closed it by hand.
///
/// Why it is safe. A by-label close is the connector's oldest path (closeClaudeTab, used for a
/// `replaced` session): it closes only when exactly one Claude tab of the window carries the label,
/// and it never reveals, so it cannot resume a dead session or close what a reveal put in front.
/// The deck refuses before anything is sent unless every one of these holds:
///   - exactly one Claude tab of THIS window answers to the label (equal, or a truncated "…" prefix);
///   - no session card that is open, replaced, or closed while its process still runs answers to it;
///   - no live session of this window holds a tab by elimination (a headcount: which tab is whose is
///     unknown, so the tab asked for may be that session's);
///   - the window's connector is up and can close tabs (0.6.12+).
/// The caller decides WHICH label is stale (say, a script that knows which session should carry a
/// title); this rule only guarantees it is nobody's.</summary>
public static class TabLabelClose
{
    /// <summary>The deck's own tab-label rule (MainWindow.TabLabelMatches): equal ignoring case, or the
    /// tab shows a truncated label ending in '…' that the title starts with.</summary>
    public static bool LabelMatches(string tabLabel, string title)
    {
        if (string.Equals(tabLabel, title, StringComparison.OrdinalIgnoreCase)) return true;
        return tabLabel.EndsWith('…') && tabLabel.Length > 1 &&
               title.StartsWith(tabLabel[..^1], StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Send = true only when every condition above holds; Why names the first that does not.</summary>
    public static (bool Send, string Why) Decide(
        IReadOnlyList<string> windowTabLabels, string label, string answeringSession, int eliminated,
        bool hasConnector, bool supportsClose, string connectorVersion)
    {
        if (string.IsNullOrWhiteSpace(label))
            return (false, "no label given: pass --label \"<the tab's label>\"");
        if (!hasConnector)
            return (false, "this group's window has no connected TabTower extension right now");
        if (!supportsClose)
            return (false, $"the window's TabTower extension ({(connectorVersion.Length > 0 ? connectorVersion : "pre-0.6.12")}) cannot close tabs: reload that window to update it");
        int hits = windowTabLabels.Count(t => LabelMatches(t, label) || LabelMatches(label, t));
        if (hits == 0)
            return (false, $"no Claude tab of this window carries \"{label}\"");
        if (hits > 1)
            return (false, $"{hits} Claude tabs of this window answer to \"{label}\": a by-label close cannot tell them apart, so none is closed");
        if (answeringSession.Length > 0)
            return (false, $"session {answeringSession} answers to \"{label}\": the tab is not unassociated, so it is not closed here");
        if (eliminated > 0)
            return (false, $"{eliminated} live session(s) of this window hold a tab nobody can name, which may be this one; not closing");
        return (true, "");
    }
}
