namespace TabTower.Services;

/// <summary>The rule that retires a folderless duplicate card (0.11.44): a card only a drag-in
/// could have made, never used, with nothing on it and nothing the user set, whose window is
/// reported by the connector of a card that HAS the folder. It deletes a card from the deck, so
/// every condition must hold; MainWindow.RetirePathlessDuplicates does the removal and the log.
///
/// Free of WPF so a test harness can compile it (tests/pathless-duplicates.tests.ps1).</summary>
public static class PathlessDuplicate
{
    /// <summary>What the rule reads off one card.</summary>
    public readonly record struct Facts(
        string Path, string Name, int SessionCount, bool Hidden, DateTime? LastUsedAt, int UseCount,
        string? CustomTitle, string? CustomColor, string Description);

    /// <summary>No folder, a name, no sessions, not hidden, never used, and nothing the user set.</summary>
    public static bool IsSuspect(in Facts f)
        => f.Path.Length == 0 && f.Name.Length > 0 && f.SessionCount == 0 && !f.Hidden &&
           f.LastUsedAt == null && f.UseCount == 0 &&
           f.CustomTitle == null && f.CustomColor == null && f.Description.Length == 0;

    /// <summary>Which cards retire, each with the window whose title it was cut from and the card
    /// that owns that window. Nothing without a suspect or a connector; the windows are read only
    /// then. <paramref name="owner"/> is null for a window no foldered card's connector reports.
    /// Removing a suspect never changes an owner (an owner has a folder), so the caller may remove
    /// after planning.</summary>
    public static List<(TCard Card, TWindow Window, TOwner Owner)> Plan<TCard, TWindow, TOwner>(
        IEnumerable<TCard> cards, Func<TCard, Facts> facts, bool anyConnector,
        Func<IReadOnlyList<TWindow>> windows, Func<TWindow, string> title, Func<TWindow, TOwner?> owner)
        where TOwner : class
    {
        var retire = new List<(TCard, TWindow, TOwner)>();
        var suspects = cards.Where(c => IsSuspect(facts(c))).ToList();
        if (suspects.Count == 0 || !anyConnector) return retire;
        var live = windows();
        foreach (var card in suspects)
        {
            string name = facts(card).Name;
            foreach (var w in live)
            {
                if (!TitleIsCutFrom(name, title(w))) continue;
                if (owner(w) is not { } o) continue;
                retire.Add((card, w, o));
                break;
            }
        }
        return retire;
    }

    /// <summary>Was this card's name cut from this window title by the drag-in (WorkspaceNameFromTitle)?</summary>
    public static bool TitleIsCutFrom(string name, string title)
        => title == name || title.StartsWith(name + " - ", StringComparison.Ordinal) ||
           WorkspaceNameFromTitle(title) == name;

    /// <summary>"file - {workspace} - Visual Studio Code" → workspace segment.</summary>
    public static string WorkspaceNameFromTitle(string title)
    {
        var parts = title.Split(" - ");
        int vsIdx = Array.FindIndex(parts, p => p.StartsWith("Visual Studio Code"));
        if (vsIdx > 0) return parts[vsIdx - 1].Trim();
        return parts.Length >= 2 ? parts[^2].Trim() : "";
    }
}
