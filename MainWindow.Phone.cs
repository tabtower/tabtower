using System.IO;
using System.Net.Sockets;
using System.Windows;
using TabTower.Models;
using TabTower.Services;
using TabTower.Services.Phone;
using TabTower.ViewModels;

namespace TabTower;

// The phone page (docs/phone-access.md): a partial class like the tasks panel, so the whole
// feature sits in one file and is inert while it is switched off.
//
// The HTTP layer (Services/Phone) knows nothing about view-models; this file is the bridge. Every
// backend call hops onto the UI thread, exactly like the pipe handler, and calls the same engine
// methods the CLI calls: NewSessionInVscode, CloseSessionTab + EndSession, OpenSessionInVscode.
public partial class MainWindow
{
    private PhoneAccessConfig _phoneConfig = new();
    private PhonePairing _phonePairing = new();
    private PhoneServer? _phoneServer;
    private readonly ClaudeSessionLinks _sessionLinks = new();
    private readonly TailscaleResolver _tailscale = new(msg => LogService.Info("phone", msg));
    private readonly Dictionary<string, PairingPromptWindow> _pairingPrompts = new(StringComparer.Ordinal);
    /// <summary>Sessions opened from the phone page. Listed from their first second, before any
    /// message has made them more than a phantom. UI thread only.</summary>
    private readonly HashSet<string> _phoneStartedSessions = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A session id the deck had never seen just started (SessionStart for a new id).
    /// Raised on the UI thread. The phone page uses it to hand back the id of the session it
    /// asked for, instead of polling the session list and guessing.</summary>
    public event Action<WorkspaceViewModel, SessionViewModel>? SessionCreated;

    private const int ClosedListLimit = 15;

    // ---- config ----

    private void LoadPhoneConfig(AppConfig config)
    {
        _phoneConfig = config.PhoneAccess ?? new PhoneAccessConfig();
        if (_phoneConfig.Port is < 1024 or > 65535) _phoneConfig.Port = PhoneAccessConfig.DefaultPort;
        _phonePairing = new PhonePairing(_phoneConfig.Devices);
        _phonePairing.Requested += p => Dispatcher.BeginInvoke(() => ShowPairingPrompt(p));
        _phonePairing.ApprovedChanged += () => Dispatcher.BeginInvoke(QueueSave);
    }

    private PhoneAccessConfig BuildPhoneConfig() => new()
    {
        Enabled = _phoneConfig.Enabled,
        Port = _phoneConfig.Port,
        Devices = _phonePairing.Approved.ToList(),
    };

    // ---- server lifecycle ----

    /// <summary>Start or stop the server to match the setting. Safe to call repeatedly.</summary>
    private void ApplyPhoneAccess()
    {
        bool running = _phoneServer != null;
        if (_phoneConfig.Enabled && running && _phoneServer!.Port == _phoneConfig.Port) return;
        StopPhoneServer();
        if (!_phoneConfig.Enabled) return;

        var server = new PhoneServer(new PhoneDeckBackend(this), _tailscale, _phonePairing,
            _sessionLinks.LinkFor, _sessionLinks.Invalidate, msg => LogService.Info("phone", msg),
            GetType().Assembly.GetName().Version?.ToString(3) ?? "?", typeof(MainWindow).Assembly);
        try
        {
            server.Start(_phoneConfig.Port);
            _phoneServer = server;
            SetStatus($"Phone access on: 127.0.0.1:{_phoneConfig.Port}");
        }
        catch (SocketException ex)
        {
            server.Dispose();
            LogService.Info("phone", $"could not listen on 127.0.0.1:{_phoneConfig.Port}: {ex.Message}");
            SetStatus($"Phone access could not start: port {_phoneConfig.Port} is in use. Pick another port in ⚙ → Phone access settings.");
        }
    }

    private void StopPhoneServer()
    {
        if (_phoneServer == null) return;
        _phoneServer.Dispose();
        _phoneServer = null;
        LogService.Info("phone", "stopped");
    }

    // ---- menu ----

    private void PhoneAccessMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _phoneConfig.Enabled = PhoneAccessMenuItem.IsChecked;
        ApplyPhoneAccess();
        if (!_phoneConfig.Enabled) SetStatus("Phone access off");
        QueueSave();
    }

    private void PhoneAccessSettingsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new PhoneAccessDialog(_phoneConfig.Enabled, _phoneConfig.Port, _phonePairing, _tailscale,
                                           () => _phoneServer != null) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            _phoneConfig.Enabled = dialog.EnabledValue;
            _phoneConfig.Port = dialog.PortValue;
            PhoneAccessMenuItem.IsChecked = _phoneConfig.Enabled;
            ApplyPhoneAccess();
            if (!_phoneConfig.Enabled) SetStatus("Phone access off");
        }
        QueueSave();   // a revoke inside the dialog is saved even on Cancel
    }

    // ---- pairing ----

    /// <summary>A device asked to be let in. One prompt per device (and at most
    /// PhonePairing.MaxPending at once). The prompt approves only with the code typed in from
    /// the device's own screen, and closing it any other way denies; it closes itself when the
    /// request expires. The code is never logged: the log is readable by anything on this PC.</summary>
    private void ShowPairingPrompt(PendingPairing pending)
    {
        string id = pending.Device.StableId;
        if (_pairingPrompts.ContainsKey(id)) return;
        LogService.Info("phone", $"pairing requested by \"{pending.Device.Name}\" " +
                                 $"(host \"{pending.Device.HostName}\", {pending.Device.Os}, node {id})");
        var prompt = new PairingPromptWindow(pending, _phonePairing);
        _pairingPrompts[id] = prompt;
        prompt.Closed += (_, _) =>
        {
            _pairingPrompts.Remove(id);
            switch (prompt.Outcome)
            {
                case PairingPromptWindow.Result.Approved:
                    LogService.Info("phone", $"approved \"{pending.Device.Name}\" (node {id})");
                    SetStatus($"Phone access: \"{pending.Device.Name}\" approved");
                    break;
                case PairingPromptWindow.Result.Denied:
                    LogService.Info("phone", $"denied \"{pending.Device.Name}\" (node {id})");
                    SetStatus($"Phone access: \"{pending.Device.Name}\" declined");
                    break;
            }
        };
        prompt.Show();
    }

    // ---- the deck, as the phone page sees it ----

    /// <summary>The cards to show: every card with an open session, plus every card with a
    /// window a new session can be opened in. A split workspace contributes its group cards,
    /// as on the deck.</summary>
    private List<PhoneCard> BuildPhoneCards(IReadOnlySet<string> live)
    {
        var cards = new List<PhoneCard>();
        var windowOpen = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var ws in Vm.Workspaces)
        {
            if (ws.IsSplit)
                foreach (var card in ws.GroupCards) AddPhoneCard(cards, card, ws, windowOpen, live);
            else AddPhoneCard(cards, ws, ws, windowOpen, live);
        }
        return cards;
    }

    private void AddPhoneCard(List<PhoneCard> cards, WorkspaceViewModel view, WorkspaceViewModel owner,
                              Dictionary<string, bool> windowOpen, IReadOnlySet<string> live)
    {
        var sessions = view.Sessions
            .Where(s => IsListedOnPhone(s, live))
            .Select(s => new PhoneSession(s.SessionId, s.DisplayTitle, SessionStatusNames.ToName(s.Status),
                                          s.Acknowledged, s.GroupLabel, UnixMs(s.LastEventAt ?? s.StartedAt))
                         { New = s.Phantom })
            .ToList();
        var targets = PhoneTargets(view, owner, windowOpen);
        if (sessions.Count == 0 && !targets.Any(t => t.State == "ready")) return;
        cards.Add(new PhoneCard(CardKey(view, owner), view.DisplayTitle, owner.Path, view.EffectiveColor, sessions, targets));
    }

    /// <summary>The sessions the page shows, and the only ones it may close. <paramref name="live"/>:
    /// the ids a running Claude Code process registered (ClaudeSessionLinks.LiveSessionIds).</summary>
    private bool IsListedOnPhone(SessionViewModel s, IReadOnlySet<string> live)
        => PhoneRules.IsListed(s.Closed, s.Phantom, s.IsHeadless, s.Status == SessionStatus.Replaced,
                               evidentlyReal: live.Contains(s.SessionId) || _phoneStartedSessions.Contains(s.SessionId));

    private static string CardKey(WorkspaceViewModel view, WorkspaceViewModel owner)
        => view.IsGroupCard ? $"ws:{owner.Id}:{view.GroupId}" : $"ws:{owner.Id}";

    /// <summary>Where a new session can go from this card. A group card offers its own group;
    /// an ordinary card offers each of its groups, or, with none configured, the folder's own
    /// window when one is connected. "launch" means the deck will start (or wait for) that
    /// group's window and open the session there once it connects: never another window.</summary>
    private List<PhoneTarget> PhoneTargets(WorkspaceViewModel view, WorkspaceViewModel owner,
                                           Dictionary<string, bool> windowOpen)
    {
        var groups = view.IsGroupCard
            ? _sessionGroups.Where(g => string.Equals(g.Id, view.GroupId, StringComparison.OrdinalIgnoreCase)).ToList()
            : GroupsFor(owner);
        var targets = new List<PhoneTarget>();
        if (groups.Count == 0)
        {
            if (ConnectorsFor(owner).Count > 0) targets.Add(new PhoneTarget("", "", "ready"));
            return targets;
        }
        string norm = WorkspaceMetadata.NormalizePath(owner.Path);
        foreach (var g in groups)
        {
            string label = g.Name.Length > 0 ? g.Name : g.Id;
            if (ConnectorInGroup(owner, g) != null) { targets.Add(new PhoneTarget(g.Id, label, "ready")); continue; }
            if (!windowOpen.TryGetValue(g.Id, out bool open)) windowOpen[g.Id] = open = GroupWindowIsOpen(g);
            bool pinnedHere = g.WorkspacePath.Length > 0 && WorkspaceMetadata.NormalizePath(g.WorkspacePath) == norm;
            // Open but not connected: only worth waiting for when that window holds THIS folder.
            // Not running: offered when the group has a launcher the deck can run.
            if (open ? pinnedHere : g.Launcher.Length > 0 && File.Exists(g.Launcher))
                targets.Add(new PhoneTarget(g.Id, label, "launch"));
        }
        return targets;
    }

    private List<PhoneClosedSession> BuildPhoneClosed()
    {
        var result = new List<PhoneClosedSession>();
        var candidates = Vm.Workspaces
            .SelectMany(w => w.Sessions.Select(s => (w, s)))
            .Where(x => x.s.Closed && !x.s.Phantom && !x.s.IsHeadless && !x.s.Historical &&
                        // A replaced session has a successor doing the same work; bringing it back
                        // would put two sessions on one job.
                        x.s.Status != SessionStatus.Replaced && x.s.EndReason != "replaced")
            .OrderByDescending(x => x.s.EndedAt ?? x.s.LastEventAt ?? x.s.StartedAt);
        foreach (var (w, s) in candidates)
        {
            // A resume reads the transcript; without it Claude Code would open a blank conversation.
            if (s.TranscriptPath is not { Length: > 0 } t || !File.Exists(t)) continue;
            result.Add(new PhoneClosedSession(s.SessionId, s.DisplayTitle, s.GroupLabel, w.DisplayTitle,
                                              UnixMs(s.EndedAt ?? s.LastEventAt ?? s.StartedAt)));
            if (result.Count >= ClosedListLimit) break;
        }
        return result;
    }

    private static long UnixMs(DateTime local) => new DateTimeOffset(local).ToUnixTimeMilliseconds();

    /// <summary>Raised from StartSession for a session id the deck did not know.</summary>
    private void NotifySessionCreated(WorkspaceViewModel ws, SessionViewModel session)
        => SessionCreated?.Invoke(ws, session);

    /// <summary>The engine behind the phone page. Every call runs on the UI thread.</summary>
    private sealed class PhoneDeckBackend : IPhoneBackend
    {
        private readonly MainWindow _w;
        public PhoneDeckBackend(MainWindow window) => _w = window;

        public async Task<IReadOnlyList<PhoneCard>> ListCardsAsync()
        {
            // The registry is read here, off the UI thread; only the view-models are read there.
            var live = await Task.Run(_w._sessionLinks.LiveSessionIds);
            return await _w.Dispatcher.InvokeAsync(() => _w.BuildPhoneCards(live));
        }

        public async Task<IReadOnlyList<PhoneClosedSession>> ListClosedAsync()
            => await _w.Dispatcher.InvokeAsync(_w.BuildPhoneClosed);

        public async Task<PhoneNewResult> NewSessionAsync(string cardKey, string group, CancellationToken ct)
        {
            var started = new TaskCompletionSource<PhoneNewResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            Action<WorkspaceViewModel, SessionViewModel>? watch = null;

            var immediate = await _w.Dispatcher.InvokeAsync<PhoneNewResult?>(() =>
            {
                var parts = cardKey.Split(':');
                if (parts.Length < 2 || !int.TryParse(parts[1], out int id) || _w.Vm.FindById(id) is not { } owner)
                    return new PhoneNewResult(false, "not_found");
                var view = parts.Length > 2
                    ? owner.GroupCards.FirstOrDefault(c => string.Equals(c.GroupId, parts[2], StringComparison.OrdinalIgnoreCase))
                    : owner;
                if (view == null) return new PhoneNewResult(false, "not_found");
                // Only what the page was offered may be asked for: the same rules, re-checked now.
                var target = _w.PhoneTargets(view, owner, new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase))
                    .FirstOrDefault(t => string.Equals(t.Group, group, StringComparison.OrdinalIgnoreCase));
                if (target == null) return new PhoneNewResult(false, "no_window");
                SessionGroupConfig? g = group.Length > 0 ? _w.GroupById(group) : null;
                if (group.Length > 0 && g == null) return new PhoneNewResult(false, "no_window");

                // The first NEW session in this folder from here on is ours. With a group, it must
                // be in that group (or carry no group stamp at all, for a setup without the hook
                // reporting one): another window on the same folder may start sessions too.
                watch = (ws, s) =>
                {
                    if (!ReferenceEquals(ws, owner) || s.IsHeadless) return;
                    if (g != null && s.GroupId.Length > 0 && !string.Equals(s.GroupId, g.Id, StringComparison.OrdinalIgnoreCase)) return;
                    // Listed from now on, though it has no message (so no transcript) yet.
                    if (started.TrySetResult(new PhoneNewResult(true, SessionId: s.SessionId)))
                        _w._phoneStartedSessions.Add(s.SessionId);
                };
                _w.SessionCreated += watch;
                var (ok, msg) = _w.NewSessionInVscode(owner, null, g, focus: false);
                if (ok) return null;
                _w.SessionCreated -= watch;
                // Parked for a window that is starting: a success the engine reports as "not sent".
                if (g != null && (msg.EndsWith(": launching", StringComparison.Ordinal) ||
                                  msg.EndsWith(": window up, connector not", StringComparison.Ordinal)))
                    return new PhoneNewResult(true, Launching: true);
                return new PhoneNewResult(false, "failed", msg);
            });
            if (immediate != null) return immediate;

            try
            {
                using var reg = ct.Register(() => started.TrySetResult(new PhoneNewResult(false, "timeout")));
                return await started.Task;
            }
            finally
            {
                await _w.Dispatcher.InvokeAsync(() => _w.SessionCreated -= watch);
            }
        }

        public async Task<PhoneOpResult> CloseSessionAsync(string sessionId)
        {
            var live = await Task.Run(_w._sessionLinks.LiveSessionIds);
            return await _w.Dispatcher.InvokeAsync(() =>
            {
                // Only a session the page offers: any other id (ended, headless, never materialized,
                // replaced) is refused as if it did not exist.
                if (_w.Vm.FindSession(sessionId) is not { } found || !_w.IsListedOnPhone(found.Item2, live))
                    return new PhoneOpResult(false, "not_found");
                // Tab first, while the session record still exists (same order as the CLI's
                // `session end --close-tab`). A tab that could not be closed is reported, not fatal.
                var (tabClosed, why) = _w.CloseSessionTab(sessionId);
                var (msg, ok) = _w.EndSession(sessionId, HookInfo.Empty);
                // A session with no message yet is dropped, not kept (EndSession, NeverMaterialized):
                // there is no conversation to resume, so it never reaches "Recently closed". Say so,
                // or the page reads as if Reopen were broken (live report 09-10-2026 16:20).
                bool removed = ok && _w.Vm.FindSession(sessionId) is null;
                return ok ? new PhoneOpResult(true, Detail: tabClosed ? null : why, Removed: removed)
                          : new PhoneOpResult(false, "failed", msg);
            });
        }

        public async Task<PhoneOpResult> ReopenSessionAsync(string sessionId)
            => await _w.Dispatcher.InvokeAsync(() =>
            {
                if (!_w.BuildPhoneClosed().Any(c => c.Id == sessionId) || _w.Vm.FindSession(sessionId) is not { } found)
                    return new PhoneOpResult(false, "not_found");
                var (ws, session) = found;
                // Same order as `session open`: point the card at the session's own window first.
                _w.PointCardAtSessionWindow(ws, session);
                _w.FocusWorkspace(ws);
                // In a Claude Code panel, as New opens one, never a `claude --resume` terminal: the
                // terminal route exists for a session whose window died, and here the user closed it
                // on purpose in a window that is still up (live report 09-10-2026 17:37: the reopen
                // came back as a terminal in the purple window, with no tab on the card).
                var (sent, msg) = _w.OpenSessionInVscode(ws, session, allowTerminal: false);
                return sent ? new PhoneOpResult(true) : new PhoneOpResult(false, "failed", msg);
            });
    }
}
