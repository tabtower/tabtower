namespace SessionDeck.Models;

/// <summary>
/// The external tasks file: a read-only JSON document produced by an external
/// tool (e.g. a task tracker's export script). SessionDeck only displays it — the producer owns
/// the content, the order and the status→color semantics. Unknown JSON keys are ignored
/// (forward-compat); only id+name are required per task.
/// </summary>
public class TasksDocument
{
    public int Version { get; set; }
    public string? Generated { get; set; }           // ISO timestamp, display only
    /// <summary>status → color (name or #RRGGBB). Data, not config: the producer owns it.</summary>
    public Dictionary<string, string> StatusColors { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Template for a new session opened FROM a task, with &lt;id&gt;/&lt;name&gt;
    /// placeholders. Missing = new sessions start empty.</summary>
    public string? NewSessionPrompt { get; set; }
    /// <summary>Optional second template, used when a launch asks for the fast variant (the
    /// word "fast" after a number in the Run box). Same placeholders. Applies to every task
    /// that has no phrase of its own; a task can also carry its own sessionPromptFast. With
    /// neither, the task has no fast variant and opens normally.</summary>
    public string? NewSessionPromptFast { get; set; }
    public List<TaskEntry> Tasks { get; set; } = new();
    /// <summary>Optional navigation map for the tree the tasks belong to. The panel shows one
    /// level at a time, so the task list alone cannot say what else exists; a producer that
    /// knows the whole tree may describe it here and the deck draws it as a grid. Missing =
    /// no grid, which is what a file written before this existed should do.</summary>
    public NavIndex? NavIndex { get; set; }
    /// <summary>Optional: every task the producer knows how to start, not just the level on
    /// screen. It is what lets the toolbar's Run box take a task NUMBER from anywhere and open
    /// a session for it. Same record shape as a task; only id, name and workspace are read.</summary>
    public List<TaskEntry> LaunchIndex { get; set; } = new();
}

/// <summary>The two-column navigation grid: the top level, and each top-level item's direct
/// children. `Active*` say where the currently displayed level sits in that map.</summary>
public class NavIndex
{
    public string? ActiveRoot { get; set; }
    public string? ActiveChild { get; set; }
    /// <summary>Optional square above the top-level column: the way back out of a tree without
    /// climbing it level by level.</summary>
    public NavEntry? Home { get; set; }
    public List<NavEntry> Roots { get; set; } = new();
}

public class NavEntry
{
    /// <summary>What the square prints — short by necessity (a 28px box). The producer
    /// decides how to shorten; inside a column a shared prefix is understood.</summary>
    public string? Label { get; set; }
    /// <summary>The full number, for the tooltip and for matching a card on screen.</summary>
    public string? Number { get; set; }
    public string? Name { get; set; }
    /// <summary>Coloured through the document's statusColors, same as a card.</summary>
    public string? Status { get; set; }
    /// <summary>Owns children of its own — drawn as a filled square rather than a hollow one.</summary>
    public bool IsParent { get; set; }
    /// <summary>Where a click goes, opened exactly like a task's url.</summary>
    public string? Url { get; set; }
    public List<NavEntry> Children { get; set; } = new();
}

public class TaskEntry
{
    public string? Id { get; set; }                  // required
    public string? Name { get; set; }                // required
    public string? Description { get; set; }
    /// <summary>One line that says what this card IS, drawn above the description in a
    /// brighter weight so it reads before the body does. Optional, and deliberately a field
    /// rather than "the first line of the description": only the producer knows whether a
    /// card's opening line is a subject or just the first of several equal facts, and a deck
    /// that promoted every first line would shout on every card in every view.</summary>
    public string? Lead { get; set; }
    public string? Status { get; set; }              // free string, colored via StatusColors
    public bool Pinned { get; set; }
    public string? Workspace { get; set; }           // full folder path — matched to a card by path
    public List<string> Sessions { get; set; } = new();
    public string? Url { get; set; }                 // opened via ShellExecute (e.g. obsidian://)
    /// <summary>The launch phrase for a session opened from THIS task, overriding the
    /// document's newSessionPrompt. Same &lt;id&gt;/&lt;name&gt; placeholders. It exists because
    /// one document can hold cards that are not the same KIND of thing: a card that stands for
    /// a package of work needs a different opening phrase from a card that stands for a single
    /// item, and opening the package with the item phrase would start the wrong kind of session.
    /// The document template stays right for every card that does not carry one.</summary>
    public string? SessionPrompt { get; set; }
    /// <summary>This task's own fast variant of its launch phrase, same placeholders. Present =
    /// the task can be opened fast even where the document has no fast template.</summary>
    public string? SessionPromptFast { get; set; }
    /// <summary>Label for the url button. The deck cannot know what a url MEANS — an
    /// external document, or (as this deck's producer uses it) a drill into the card's own
    /// sub-tasks — and calling all of them "Open task" made a navigation card read as a unit
    /// of work. The producer knows, so it may say. Missing = the generic label.</summary>
    public string? UrlLabel { get; set; }
}
