namespace TabTower.Services;

/// <summary>One file of Claude Code's live-session registry (<c>~/.claude/sessions/&lt;pid&gt;.json</c>),
/// reduced to what deciding a card needs. <see cref="SessionRegistry.ReadRecords"/> fills it; a test
/// builds it by hand.</summary>
/// <param name="ProcessVerified">The pid is running AND its start time equals the recorded
/// <paramref name="ProcStart"/>. False for an exited process and for a pid Windows handed to a
/// stranger.</param>
/// <param name="IdeBinary">The running process is the VS Code extension's own claude binary. The
/// entrypoint alone cannot say so: it is inherited, so a <c>claude -p</c> run launched from inside
/// an IDE session reports <c>claude-vscode</c> too (hooks/tabtower-hook.ps1, Test-PrintModeRun).</param>
/// <param name="Transcript">The session's transcript, when one exists on disk; null otherwise.</param>
public sealed record RegistryRecord(
    string? SessionId, int? Pid, string? ProcStart, string? Cwd, string? Entrypoint, string? Kind,
    bool ProcessVerified, bool IdeBinary, string? Transcript);

/// <summary>What the deck already holds for a session id.</summary>
public readonly record struct KnownSession(bool Closed, string? EndReason);

public enum RecoveryAction
{
    /// <summary>Nothing to do; the reason says why.</summary>
    Skip,
    /// <summary>A live session the deck has no card for: create it.</summary>
    Create,
    /// <summary>A live session whose card the deck closed on a guess: open it again.</summary>
    Revive,
}

/// <summary>Cards for sessions the deck never heard start.
///
/// The hooks are the deck's only news of a session, and a hook that fires while the deck is not
/// running is lost. That is the normal morning: VS Code restores its windows and their sessions
/// at sign-in, the deck starts a minute later, and every restored session stays invisible until it
/// happens to fire another hook, which an idle session never does. Closing and reopening the deck
/// loses every session opened in between the same way.
///
/// So at startup the deck reads Claude Code's own live-session registry and rebuilds from it. The
/// rule is precision over coverage: a card created here is one the deck will later reveal, focus
/// and close by id, and a dead session revealed by id is RESUMED into some tab, which is how a
/// close aimed at a dead session once closed a live one. A record therefore earns a card only when
/// its identity is proven: a parseable session id, a running pid whose start time matches the one
/// recorded (a recycled pid belongs to a stranger), an interactive session of the VS Code extension
/// running the extension's own binary, or one of a terminal or Claude Desktop (0.11.68), and, for a
/// new card, a transcript on disk.</summary>
public static class RegistryRecovery
{
    /// <summary>Closes the deck made on its own evidence (the orphan, stale and exit sweeps), which a
    /// live process proves wrong. A close a hook reported, and a replaced session, stay closed here:
    /// the next hook from that session decides those, by the rule in SetSessionStatus.</summary>
    public static readonly IReadOnlySet<string> GuessedCloses =
        new HashSet<string>(StringComparer.Ordinal) { "orphaned", "stale", "exited" };

    public const string IdeEntrypoint = "claude-vscode";

    /// <summary>Pure: the action for one record, given what the deck holds for its id.</summary>
    public static (RecoveryAction Action, string Why) Decide(RegistryRecord r, KnownSession? known)
    {
        if (r.SessionId is not { Length: > 0 } id || !Guid.TryParse(id, out _))
            return (RecoveryAction.Skip, "no valid session id");
        if (r.Pid is not > 0)
            return (RecoveryAction.Skip, "no pid");
        if (r.ProcStart is not { Length: > 0 })
            return (RecoveryAction.Skip, "no process start time recorded, so the pid cannot be proven to be this session's");
        if (!r.ProcessVerified)
            return (RecoveryAction.Skip, "its process is not running (exited, or the pid now belongs to another process)");
        if (!string.Equals(r.Kind, "interactive", StringComparison.Ordinal))
            return (RecoveryAction.Skip, $"not an interactive session (kind={r.Kind ?? "?"})");
        // A terminal or Claude Desktop session is a person's own session too (SessionHost, 0.11.68);
        // the hooks give it a card while the deck runs, so a deck restart must not lose it. The
        // binary test is the IDE's own proof and has no counterpart there: a `--bg` run is refused
        // by its kind above, an sdk-* run by its entrypoint here.
        bool outsideIde = SessionHost.IsOutsideIde(r.Entrypoint);
        if (!outsideIde && !string.Equals(r.Entrypoint, IdeEntrypoint, StringComparison.Ordinal))
            return (RecoveryAction.Skip, $"not a VS Code, terminal or Claude Desktop session (entrypoint={r.Entrypoint ?? "?"})");
        if (!outsideIde && !r.IdeBinary)
            return (RecoveryAction.Skip, "its process is not the VS Code extension's binary (a headless run started from inside one)");

        if (known is { } k)
        {
            if (!k.Closed) return (RecoveryAction.Skip, "already on the deck");
            if (k.EndReason is { } reason && GuessedCloses.Contains(reason))
                return (RecoveryAction.Revive, $"closed as {reason}, but its process is running");
            return (RecoveryAction.Skip, $"closed ({k.EndReason ?? "?"}), which only a hook from it may undo");
        }

        if (r.Cwd is not { Length: > 0 })
            return (RecoveryAction.Skip, "no working folder recorded");
        if (r.Transcript is not { Length: > 0 })
            return (RecoveryAction.Skip, "no transcript on disk yet (an id VS Code minted and never used)");
        return (RecoveryAction.Create, "running, and the deck has no card for it");
    }

    /// <summary>Pure: one decision per record, the same session id acted on once at most (two
    /// processes writing one session is a fork; one card holds it).</summary>
    public static IReadOnlyList<(RegistryRecord Record, RecoveryAction Action, string Why)> Plan(
        IEnumerable<RegistryRecord> records, Func<string, KnownSession?> lookup)
    {
        var result = new List<(RegistryRecord, RecoveryAction, string)>();
        var acted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in records)
        {
            KnownSession? known = r.SessionId is { Length: > 0 } id ? lookup(id) : null;
            var (action, why) = Decide(r, known);
            if (action != RecoveryAction.Skip && !acted.Add(r.SessionId!))
                (action, why) = (RecoveryAction.Skip, "another live process of the same session was already taken");
            result.Add((r, action, why));
        }
        return result;
    }
}
