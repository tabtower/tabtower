namespace TabTower.Services;

/// <summary>Is a session's own Claude Code process running right now, as far as Claude Code's
/// live-session registry can tell?</summary>
public enum ProcessState
{
    /// <summary>The registry names this session and its process is the one that registered it.</summary>
    Live,
    /// <summary>The registry is in use on this machine and holds no running process for this session.</summary>
    Gone,
    /// <summary>The registry cannot answer: missing, unreadable, or holding no live process at all
    /// (a Claude Code build that does not write it).</summary>
    Unknown,
}

public enum CloseTabAction
{
    /// <summary>Refused before anything was sent; the reason says why.</summary>
    Refuse,
    /// <summary>Nothing to do and nothing sent: the session has no tab that can be positively
    /// told apart as its own. Logged, never an error.</summary>
    NoOp,
    /// <summary>Ask the connector to close the tab by session id (reveal, then close what the
    /// reveal put in front).</summary>
    ById,
}

/// <summary>The decision behind <c>session close-tab</c> and <c>session end --close-tab</c>, apart
/// from the window that acts on it so it can be tested without the app (tests/phone/PhoneHarness
/// links this file).
///
/// Why liveness decides it (0.11.35). Closing by id means revealing the session through Claude
/// Code's own id-to-panel registry and closing the tab the reveal put in front. That is a
/// positive match only while the session has a panel, i.e. while its process is alive. For a
/// session whose process died without the deck hearing of it, the reveal resumes it off its
/// transcript or does nothing at all, and what is in front afterwards is some other tab. On
/// 17-10-2026 a burst of such requests closed a live tab that no request had named. So a
/// session that the registry shows as gone is a no-op with a log line: no reveal, no tab
/// touched. When the registry cannot answer (an older Claude Code), the by-id path stands as
/// before; the connector serialises its commands (0.7.1), which removes the other half of what
/// went wrong that day.</summary>
public static class CloseTabRoute
{
    /// <summary>May the connector close this session's tab IN PLACE, without revealing it first
    /// (0.11.51, connector 0.7.3)? Only when exactly one of the window's tabs answers to the session
    /// and no other LIVE session answers to that tab's label: then the label alone names the tab.
    /// Anything else (a label two tabs share, or a fork or a resume that answers to the same
    /// opening prompt) keeps the reveal, which asks Claude Code's own id-to-panel registry. The
    /// connector checks the same thing again on its side, against its own tabs, and falls back to
    /// the reveal when it disagrees. A CLOSED session is left out on purpose: its leftover tab, if
    /// any, is a second tab carrying the label, and the connector's count of one catches it.</summary>
    public static bool ClosesInPlace(IReadOnlyList<string> windowTabLabels, Func<string, bool> isItsTab,
                                     Func<string, bool> anotherLiveSessionAnswers)
    {
        var its = windowTabLabels.Where(isItsTab).ToList();
        return its.Count == 1 && !anotherLiveSessionAnswers(its[0]);
    }

    public static (CloseTabAction Action, string Why) Decide(
        bool replaced, bool closed, ProcessState process, string processWhy,
        bool hasConnector, bool supportsById, string connectorVersion)
    {
        if (replaced)
            return (CloseTabAction.Refuse, "this session was replaced by a successor and its process was ended; the deck closes its tab on its own, by label, because revealing a dead session revives it");
        if (closed)
            return (CloseTabAction.Refuse, "this session has already ended: revealing it would resume it off its transcript, so its tab is left for you to close. Pass --close-tab to `session end` instead, which asks while the session is still alive");
        if (process == ProcessState.Gone)
            return (CloseTabAction.NoOp, $"this session's process is not running ({processWhy}): revealing it would resume it, and no tab can be told apart as its own, so no tab was touched. If one is still open, close it by hand");
        if (!hasConnector)
            return (CloseTabAction.Refuse, "no VSCode connector for this workspace");
        if (!supportsById)
            return (CloseTabAction.Refuse, $"the VSCode window's TabTower extension ({(connectorVersion.Length > 0 ? connectorVersion : "pre-0.6.12")}) cannot close a tab by session id: reload that window to update it");
        return (CloseTabAction.ById, "");
    }
}

/// <summary>The pure half of reading Claude Code's live-session registry (<c>~/.claude/sessions/*.json</c>,
/// one file per running process, deleted on a clean exit); <c>SessionRegistry.Probe</c> does the
/// reading. Undocumented by Claude Code, so every answer it cannot prove is
/// <see cref="ProcessState.Unknown"/>, never <see cref="ProcessState.Gone"/>.</summary>
public static class RegistryState
{
    /// <summary>One registry entry, reduced to what the classification needs.</summary>
    public readonly record struct Entry(string? SessionId, bool ProcessRunning);

    /// <summary>Pure: the state of <paramref name="sessionId"/> given the entries read.
    /// Gone needs proof that the registry is maintained here at all, which is any OTHER entry whose
    /// process is running; a registry with no live entry says nothing about this session.</summary>
    public static (ProcessState State, string Why) Classify(string sessionId, bool readable, IReadOnlyList<Entry> entries)
    {
        if (!readable) return (ProcessState.Unknown, "session registry not readable");
        bool anyLive = false, ownEntry = false;
        foreach (var e in entries)
        {
            if (e.ProcessRunning) anyLive = true;
            if (!string.Equals(e.SessionId, sessionId, StringComparison.Ordinal)) continue;
            if (e.ProcessRunning) return (ProcessState.Live, "");
            ownEntry = true;
        }
        if (ownEntry) return (ProcessState.Gone, "its registered process has exited");
        if (anyLive) return (ProcessState.Gone, "no running Claude Code process is registered for it");
        return (ProcessState.Unknown, "the session registry holds no running process at all");
    }
}
