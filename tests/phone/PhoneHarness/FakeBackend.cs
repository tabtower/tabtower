using TabTower.Services.Phone;

namespace PhoneHarness;

/// <summary>An in-memory deck: a few cards, sessions in every status, a split folder with two
/// session groups, and a recently-closed list. New, close and reopen behave like the real
/// engine from the page's point of view, with a short delay for the "session started" event.</summary>
public sealed class FakeBackend : IPhoneBackend
{
    private sealed class Card
    {
        public required string Key, Title, Path, Color;
        public List<PhoneSession> Sessions = new();
        public List<PhoneTarget> Targets = new();
    }

    private readonly object _gate = new();
    private readonly List<Card> _cards = new();
    private readonly List<PhoneClosedSession> _closed = new();
    private readonly Dictionary<string, string> _links = new();
    public TimeSpan StartDelay { get; set; } = TimeSpan.FromMilliseconds(1200);

    /// <summary>A live session the page does not list (a headless run), so it must not be
    /// closable from the phone either.</summary>
    public string HiddenSessionId { get; } = NewId();

    /// <summary>A spare id with no message and no live process behind it (what a window load
    /// leaves): not listed, and not closable.</summary>
    public string SpareSessionId { get; } = NewId();

    public FakeBackend(IReadOnlyList<string>? titles = null)
    {
        var t = new Queue<string>(titles ?? Array.Empty<string>());
        string T(string fallback) => t.Count > 0 ? t.Dequeue() : fallback;
        long ago(int minutes) => DateTimeOffset.UtcNow.AddMinutes(-minutes).ToUnixTimeMilliseconds();

        var app = new Card { Key = "ws:1", Title = "demo-app", Path = @"C:\dev\demo-app", Color = "#2F6FDE" };
        app.Sessions.Add(Live(T("Refactor the request parser"), "working", false, "", ago(1), link: true));
        app.Sessions.Add(Live(T("Add the login form and its validation"), "waiting", false, "", ago(4), link: true));
        app.Sessions.Add(Live(T("Write the release notes"), "done", true, "", ago(38), link: false));
        app.Targets.Add(new PhoneTarget("", "", "ready"));

        var work = new Card { Key = "ws:2:work", Title = "Work", Path = @"C:\dev\notes", Color = "#B99CF5" };
        work.Sessions.Add(Live(T("Plan the next sprint"), "idle", false, "Work", ago(90), link: true));
        work.Targets.Add(new PhoneTarget("work", "Work", "ready"));
        var personal = new Card { Key = "ws:2:personal", Title = "Personal", Path = @"C:\dev\notes", Color = "#5FD48A" };
        personal.Sessions.Add(Live(T("Sort the photo archive"), "error", false, "Personal", ago(300), link: false));
        personal.Targets.Add(new PhoneTarget("personal", "Personal", "launch"));

        var site = new Card { Key = "ws:3", Title = "website", Path = @"C:\dev\website", Color = "#4A4A4A" };
        site.Targets.Add(new PhoneTarget("", "", "ready"));

        _cards.AddRange(new[] { app, work, personal, site });
        _closed.Add(new PhoneClosedSession(NewId(), T("Fix the flaky upload test"), "", "demo-app", ago(20)));
        _closed.Add(new PhoneClosedSession(NewId(), T("Draft the onboarding email"), "Work", "Work", ago(140)));
        _closed.Add(new PhoneClosedSession(NewId(), T("Migrate the settings page"), "", "website", ago(1500)));
    }

    private PhoneSession Live(string title, string status, bool ack, string group, long at, bool link)
    {
        var s = new PhoneSession(NewId(), title, status, ack, group, at);
        if (link) _links[s.Id] = "https://claude.ai/code/session_" + System.Guid.NewGuid().ToString("N");
        return s;
    }

    private static string NewId() => System.Guid.NewGuid().ToString();

    public string? LinkFor(string id)
    {
        lock (_gate) return _links.TryGetValue(id, out var l) ? l : null;
    }

    public Task<IReadOnlyList<PhoneCard>> ListCardsAsync()
    {
        lock (_gate)
            return Task.FromResult<IReadOnlyList<PhoneCard>>(_cards
                .Where(c => c.Sessions.Count > 0 || c.Targets.Any(t => t.State == "ready"))
                .Select(c => new PhoneCard(c.Key, c.Title, c.Path, c.Color, c.Sessions.ToList(), c.Targets.ToList()))
                .ToList());
    }

    public Task<IReadOnlyList<PhoneClosedSession>> ListClosedAsync()
    {
        lock (_gate) return Task.FromResult<IReadOnlyList<PhoneClosedSession>>(_closed.OrderByDescending(c => c.At).ToList());
    }

    public async Task<PhoneNewResult> NewSessionAsync(string cardKey, string group, CancellationToken ct)
    {
        Card? card;
        lock (_gate) card = _cards.FirstOrDefault(c => c.Key == cardKey);
        if (card == null) return new PhoneNewResult(false, "not_found");
        var target = card.Targets.FirstOrDefault(t => t.Group == group);
        if (target == null) return new PhoneNewResult(false, "no_window");
        if (target.State == "launch") return new PhoneNewResult(true, Launching: true);
        await Task.Delay(StartDelay, ct);
        // Opened from the phone, no message yet: listed at once and marked new, as the deck does.
        var s = Live("New session", "idle", false, target.Label, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), link: true) with { New = true };
        lock (_gate) card.Sessions.Add(s);
        return new PhoneNewResult(true, SessionId: s.Id);
    }

    public Task<PhoneOpResult> CloseSessionAsync(string sessionId)
    {
        lock (_gate)
        {
            // The same rule the deck applies: only a listed session may be closed.
            if (sessionId == HiddenSessionId && !PhoneRules.IsListed(closed: false, phantom: false, headless: true, replaced: false, evidentlyReal: true))
                return Task.FromResult(new PhoneOpResult(false, "not_found"));
            if (sessionId == SpareSessionId && !PhoneRules.IsListed(closed: false, phantom: true, headless: false, replaced: false, evidentlyReal: false))
                return Task.FromResult(new PhoneOpResult(false, "not_found"));
            foreach (var c in _cards)
            {
                var s = c.Sessions.FirstOrDefault(x => x.Id == sessionId);
                if (s == null) continue;
                c.Sessions.Remove(s);
                _links.Remove(s.Id);
                // As the deck does (EndSession, NeverMaterialized): a session with no message yet
                // has no conversation to resume, so it is dropped, not kept for "Recently closed".
                // This fake used to keep it, which is how Reopen passed here and failed live.
                if (s.New) return Task.FromResult(new PhoneOpResult(true, Removed: true));
                _closed.Add(new PhoneClosedSession(s.Id, s.Title, s.Group, c.Title, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
                return Task.FromResult(new PhoneOpResult(true));
            }
        }
        return Task.FromResult(new PhoneOpResult(false, "not_found"));
    }

    public Task<PhoneOpResult> ReopenSessionAsync(string sessionId)
    {
        lock (_gate)
        {
            var closed = _closed.FirstOrDefault(c => c.Id == sessionId);
            if (closed == null) return Task.FromResult(new PhoneOpResult(false, "not_found"));
            _closed.Remove(closed);
            var card = _cards.FirstOrDefault(c => c.Title == closed.Workspace) ?? _cards[0];
            var s = new PhoneSession(closed.Id, closed.Title, "idle", false, closed.Group, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            card.Sessions.Add(s);
            _links[s.Id] = "https://claude.ai/code/session_" + System.Guid.NewGuid().ToString("N");
            return Task.FromResult(new PhoneOpResult(true));
        }
    }
}
