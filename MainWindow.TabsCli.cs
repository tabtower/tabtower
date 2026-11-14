using TabTower.Models;
using TabTower.Services;
using TabTower.ViewModels;

namespace TabTower;

/// <summary>`tabtower tabs` and `tabtower tabs close` (0.11.59): the Claude tabs of each
/// session group's window as its connector last reported them, each with the session card that answers
/// to it, and a by-label close for a tab that no card answers to. The rule for the close is
/// Services/TabLabelClose.cs; this file only gathers what it decides on, and sends.
///
/// The case it is for: an extension or VS Code update ends a session's process, the deck closes its card
/// ("its tab left VSCode", 60 s), and VS Code restores the tab when the window reloads. The tab then has
/// no card, so nothing reachable by session id can close it.</summary>
public partial class MainWindow
{
    public sealed record TabRow(string Label, string HeldBy);
    public sealed record WindowTabsRow(SessionGroupConfig Group, VscodeConnection Conn, List<TabRow> Tabs, int Eliminated);

    /// <summary>The window of each group (or of the one named), with its Claude tabs and who holds each.</summary>
    public List<WindowTabsRow> WindowTabs(string? groupId)
    {
        var result = new List<WindowTabsRow>();
        foreach (var group in _sessionGroups.Where(g => g.Id.Length > 0 &&
                     (groupId == null || string.Equals(g.Id, groupId, StringComparison.OrdinalIgnoreCase))))
        {
            var ws = group.WorkspacePath.Length > 0 ? Vm.FindByPath(group.WorkspacePath) : null;
            if (ws == null) continue;
            ReapplyTabCorrelation(ws);
            foreach (var conn in ConnectorsInGroup(ws, group))
            {
                var tabs = conn.Tabs.Select(t => new TabRow(t.Label, AnsweringSession(t.Label))).ToList();
                result.Add(new WindowTabsRow(group, conn, tabs, EliminatedIn(ws, group, conn)));
            }
        }
        return result;
    }

    /// <summary>The first session card, on ANY card, that answers to this label and may still own a tab:
    /// open, replaced, or closed on the deck while Claude Code's registry shows its process running.
    /// "" when none does.</summary>
    private string AnsweringSession(string label)
    {
        foreach (var w in Vm.Workspaces)
            foreach (var s in w.Sessions.Where(s => !s.Phantom))
            {
                if (MatchTabLabel(label, s) == null) continue;
                if (!s.Closed || s.Status == SessionStatus.Replaced) return s.SessionId;
                if (SessionRegistry.Probe(s.SessionId).State == ProcessState.Live) return s.SessionId;
            }
        return "";
    }

    /// <summary>Live sessions that may hold one of this window's tabs by elimination: they have a tab
    /// (OpenAsTab), and none of this window's labels answers to them. A session stamped in another
    /// group is not counted; one with no stamp is counted unless another window of the card shows a
    /// tab of its own.</summary>
    private int EliminatedIn(WorkspaceViewModel ws, SessionGroupConfig group, VscodeConnection conn)
    {
        var all = ConnectorsFor(ws);
        int n = 0;
        foreach (var s in ws.Sessions.Where(s => !s.Closed && !s.Phantom && s.OpenAsTab))
        {
            if (s.GroupId.Length > 0 && !string.Equals(s.GroupId, group.Id, StringComparison.OrdinalIgnoreCase)) continue;
            if (conn.Tabs.Any(t => MatchTabLabel(t.Label, s) != null)) continue;
            if (s.GroupId.Length == 0 && all.Any(c => !ReferenceEquals(c, conn) && c.Tabs.Any(t => MatchTabLabel(t.Label, s) != null))) continue;
            n++;
        }
        return n;
    }

    /// <summary>`tabtower tabs close --group &lt;id&gt; --label "..."`. The answer says only that the ask
    /// was DELIVERED; the connector closes the tab only if it, too, finds exactly one Claude tab with the
    /// label, and says so in its own Output channel.</summary>
    public (bool, string) CloseUnassociatedTab(string groupId, string label)
    {
        if (GroupById(groupId) is not { } group) return (false, $"unknown group '{groupId}' (see: tabtower groups)");
        var rows = WindowTabs(group.Id);
        if (rows.Count > 1)
            return (false, $"{rows.Count} windows of group {group.Id} are connected: cannot tell which one shows the tab; not closing");
        var row = rows.FirstOrDefault();
        var labels = row?.Tabs.Select(t => t.Label).ToList() ?? new List<string>();
        string holder = AnsweringSession(label);
        var (send, why) = TabLabelClose.Decide(labels, label, holder, row?.Eliminated ?? 0,
                                               row != null, row?.Conn.SupportsCloseSession ?? false,
                                               row?.Conn.Version ?? "");
        if (!send || row == null)
        {
            LogService.Info("close", $"unassociated tab \"{label}\" in {group.Id}: NOT sent, no tab touched: {why}");
            return (false, why);
        }
        // No ById: the connector's by-label path, which never reveals (see TabLabelClose).
        if (!row.Conn.TrySend(new { Cmd = "closeSession", SessionId = "unassociated-tab", Labels = new[] { label } }))
        {
            _connectors.Remove(row.Conn);
            return (false, "connector connection lost");
        }
        LogService.Info("close", $"unassociated tab \"{label}\" closeSession by label → pid={row.Conn.Pid} " +
                                 $"group={group.Id} (ext {ExtVersionText(row.Conn)}); no card answers to it");
        return (true, $"asked the {group.Id} window to close the unassociated tab \"{label}\"");
    }
}
