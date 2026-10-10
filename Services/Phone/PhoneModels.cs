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
}

/// <summary>Where a new session can be opened from a card. <c>Group</c> is "" for a card with
/// no session groups. <c>State</c>: "ready" (its window is connected) or "launch" (the deck
/// can start or is waiting for that window, and the session opens once it connects).</summary>
public sealed record PhoneTarget(string Group, string Label, string State);

/// <summary>One deck card, as the deck groups them: a workspace, or one group card of a
/// workspace split by session group.</summary>
public sealed record PhoneCard(string Key, string Title, string Path, string Color,
                               IReadOnlyList<PhoneSession> Sessions, IReadOnlyList<PhoneTarget> Targets);

public sealed record PhoneClosedSession(string Id, string Title, string Group, string Workspace, long At);

/// <summary>Outcome of a new-session request. <c>Error</c> is a short machine code the page
/// translates; <c>Detail</c> is the deck's own (English) explanation, shown as is.</summary>
public sealed record PhoneNewResult(bool Ok, string? Error = null, string? Detail = null,
                                    bool Launching = false, string? SessionId = null);

/// <summary>Outcome of a close or reopen. <c>Removed</c> (close only): the session had no message
/// yet, so the deck dropped it instead of keeping it, and it will not be in "Recently closed".</summary>
public sealed record PhoneOpResult(bool Ok, string? Error = null, string? Detail = null, bool Removed = false);

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
