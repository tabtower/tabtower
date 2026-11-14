namespace TabTower.Services.Phone;

// The phone page's view of the deck. Plain records, serialized as camelCase JSON, so the page
// never depends on a view-model and the HTTP layer can be tested with a fake backend.

/// <summary>One session row. <c>At</c> is the last activity, Unix milliseconds.</summary>
public sealed record PhoneSession(string Id, string Title, string Status, bool Ack, string Group, long At)
{
    /// <summary>https://claude.ai/code/... when Claude Code published one for this session.</summary>
    public string? Link { get; init; }

    /// <summary>Started, but no message sent yet (no transcript). Shown so a session just opened
    /// from the phone is there to tap at once.</summary>
    public bool New { get; init; }

    /// <summary>Pinned to the top of its window on the deck. The page lists it first, as the
    /// deck does, and marks it.</summary>
    public bool Pinned { get; init; }

    /// <summary>What the deck card shows in place of <c>Status</c>, null when it shows the status
    /// itself: "board_open" (questions wait on its question board), "board_empty", "runs",
    /// "watch", "job" (the turn ended, but the session waits for a machine, not the user),
    /// "peer" (the turn answered another session's message, in a window with a question board) or
    /// "forked". <c>Count</c> goes with it (questions, runs, monitors, jobs).</summary>
    public string? Shown { get; init; }

    public int Count { get; init; }
}

/// <summary>Where a new session can be opened from a card. <c>Group</c> is "" for a card with
/// no session groups. <c>State</c>: "ready" (its window is connected) or "launch" (the deck
/// can start or is waiting for that window, and the session opens once it connects).</summary>
public sealed record PhoneTarget(string Group, string Label, string State)
{
    /// <summary>0.11.66: the window's colour (#RRGGBB or "").</summary>
    public string Color { get; init; } = "";
}

/// <summary>One deck card, as the deck groups them: a workspace, or one group card of a
/// workspace split by session group.</summary>
public sealed record PhoneCard(string Key, string Title, string Path, string Color,
                               IReadOnlyList<PhoneSession> Sessions, IReadOnlyList<PhoneTarget> Targets)
{
}

public sealed record PhoneClosedSession(string Id, string Title, string Group, string Workspace, long At);

/// <summary>Outcome of a new-session request. <c>Error</c> is a short machine code the page
/// translates; <c>Detail</c> is the deck's own (English) explanation, shown as is.
/// <c>Prompt</c> (task sessions only): the opening phrase the session was given. VS Code puts it
/// in the input box without sending it, so the page shows it for the user to send from the app.</summary>
public sealed record PhoneNewResult(bool Ok, string? Error = null, string? Detail = null,
                                    bool Launching = false, string? SessionId = null, string? Prompt = null)
{
    /// <summary>0.11.66: for group "auto", the label of the window the pick chose.</summary>
    public string? Picked { get; init; }
}

/// <summary>Outcome of a close or reopen. <c>Removed</c> (close only): the session had no message
/// yet, so the deck dropped it instead of keeping it, and it will not be in "Recently closed".</summary>
public sealed record PhoneOpResult(bool Ok, string? Error = null, string? Detail = null, bool Removed = false);

/// <summary>One card of the tasks page, as the phone sees it. <c>CanStart</c>: a session can be
/// opened from it (it names a folder). <c>Nav</c>: its link runs on the PC, which is how the
/// producer's drill-in links move the page to another level. <c>Link</c>: its link is a web
/// address, opened on the phone itself rather than on the PC.</summary>
public sealed record PhoneTask(string Id, string Name, string Lead, string Description, string Status,
                               string Color, bool Pinned, bool CanStart, bool HasFast, bool Nav,
                               string NavLabel, string? Link,
                               IReadOnlyList<PhoneChip>? Chips = null, PhoneHover? Hover = null);

/// <summary>A task card's chip (0.11.52): its label and colours, and what tapping it shows, the
/// panel the PC shows when the pointer rests on it. Colours are #RRGGBB, or "" for the default.</summary>
public sealed record PhoneChip(string Label, string Background, string Foreground, PhoneHover? Hover);

/// <summary>The detail panel behind a card or a chip, as the PC draws it on hover: a title,
/// groups of rows, a footer. <c>Rtl</c> is one direction for the whole panel, as on the PC.</summary>
public sealed record PhoneHover(string Title, IReadOnlyList<PhoneHoverSection> Sections, string Footer, bool Rtl);

public sealed record PhoneHoverSection(string Heading, string Color, IReadOnlyList<PhoneHoverRow> Rows, string More);

public sealed record PhoneHoverRow(string Text, string Number, IReadOnlyList<PhoneHoverDetail> Details);

public sealed record PhoneHoverDetail(string Label, string Value);

/// <summary>One square of the tasks page's top-level navigation column (or its home square).</summary>
public sealed record PhoneNav(string Number, string Label, string Name, string Status, string Color,
                              bool Active, string Detail);

/// <summary>Where a session opened from a task can go: one VS Code instance (session group).
/// <c>Key</c> is the modifier that picks it on the PC ("", "ctrl", "alt"), which is what the
/// page sends back, so the phone reaches exactly the window the same click would. <c>State</c>:
/// "ready" (its window is connected), "launch" (the deck will start it, or is waiting for it)
/// or "off" (not running and the deck has no way to start it).</summary>
public sealed record PhoneLaunchOption(string Key, string Label, string Color, string State = "ready")
{
    /// <summary>0.11.66: the group this option opens in.</summary>
    public string Group { get; init; } = "";
}

/// <summary>The tasks page. <c>Enabled</c> false = no tasks file is configured on the PC, and
/// the page shows no tasks tab. <c>LoadedAt</c> moves on every reload of the file, which is how
/// the page knows a navigation has landed.</summary>
public sealed record PhoneTasksView(bool Enabled, string? Error, string Generated, long LoadedAt,
                                    IReadOnlyList<PhoneTask> Tasks, IReadOnlyList<PhoneNav> Nav,
                                    PhoneNav? Home, IReadOnlyList<PhoneLaunchOption> Options,
                                    int LaunchCount)
{
}

/// <summary>Rules the deck side and the test fake share, so they cannot drift apart.</summary>
public static class PhoneRules
{
    /// <summary>A session the page lists. Only these may be closed from the phone: a session the
    /// page does not show (ended, headless, replaced, or a spare id nobody is using) is not the
    /// phone's to end, whatever id a request names.
    ///
    /// <paramref name="phantom"/>: no transcript yet, i.e. no message sent. That alone does not
    /// hide a session: a brand-new one is exactly that. It is listed when it is
    /// <paramref name="evidentlyReal"/>: a live Claude Code process registered it, or it was
    /// opened from the phone. A phantom with neither is a spare id and stays hidden, as on the deck.</summary>
    public static bool IsListed(bool closed, bool phantom, bool headless, bool replaced, bool evidentlyReal)
        => !closed && !headless && !replaced && (!phantom || evidentlyReal);
}

/// <summary>What the phone server needs from the deck. Implemented by the main window (on its
/// UI thread) and by a fake in the test harness.</summary>
public interface IPhoneBackend
{
    Task<IReadOnlyList<PhoneCard>> ListCardsAsync();

    /// <summary>Open a new session from a card. Completes when the session has started and its
    /// id is known, when the request was parked for a window that is starting, or on failure.</summary>
    Task<PhoneNewResult> NewSessionAsync(string cardKey, string group, CancellationToken ct);

    /// <summary>End a live session and close its VS Code tab.</summary>
    Task<PhoneOpResult> CloseSessionAsync(string sessionId);

    Task<IReadOnlyList<PhoneClosedSession>> ListClosedAsync();

    /// <summary>Resume a recently closed session in its own window.</summary>
    Task<PhoneOpResult> ReopenSessionAsync(string sessionId);

    /// <summary>The tasks page as the PC shows it right now.</summary>
    Task<PhoneTasksView> ListTasksAsync();

    /// <summary>A task by its number, from the level on screen or the file's launch index (the
    /// PC's Run box). Null when the file knows no such task.</summary>
    Task<PhoneTask?> FindTaskAsync(string number);

    /// <summary>Open a new session from a task, with the task's own launch phrase, in the window
    /// <paramref name="option"/> picks. The phrase always comes from the tasks file, never from
    /// the phone.</summary>
    Task<PhoneNewResult> StartTaskAsync(string number, string option, bool fast, CancellationToken ct);

    /// <summary>Follow a navigation link on the PC: a task card's link (<paramref name="kind"/>
    /// "task"), a top-level square ("nav") or the home square ("home"). Only links the tasks file
    /// itself holds can be followed; the phone names the card, never the link.</summary>
    Task<PhoneOpResult> NavigateTasksAsync(string kind, string id);
}

/// <summary>A tailnet device, as Tailscale identifies it. <c>Name</c> is its tailnet node name,
/// <c>HostName</c> the name the device reports for itself; either can be chosen by whoever
/// controls the device, which is why approval needs the code the device shows.</summary>
public sealed record DeviceInfo(string StableId, string Name, string Os, string Login, bool Tagged, string HostName = "");

/// <summary>This machine's own Tailscale identity.</summary>
public sealed record SelfInfo(string Login, string StableId, string DnsName);

public interface IDeviceResolver
{
    /// <summary>The device behind a tailnet address, or null when Tailscale does not know it.</summary>
    Task<DeviceInfo?> WhoIsAsync(string ip);

    /// <summary>This machine's identity, or null when Tailscale is not running or not logged in.</summary>
    Task<SelfInfo?> SelfAsync();
}

/// <summary>A device the user approved. Persisted in the app config.</summary>
public sealed class PairedDevice
{
    public string StableId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Os { get; set; } = "";
    public DateTime ApprovedAt { get; set; }
    public DateTime? LastSeenAt { get; set; }
}
