namespace TabTower.Services;

/// <summary>Where a session a person opened runs, by its Claude Code entrypoint, when that place is
/// NOT VS Code: a terminal (<c>claude</c> in a console, entrypoint "cli") or the Claude Desktop
/// app's Code tab ("claude-desktop"). The one definition the orphan sweep, the startup recovery
/// and the tab claim read (0.11.68), so the deck cannot ask "outside VS Code?" three ways.
///
/// Such a session has no VS Code tab by construction, so the deck must never (1) close its card
/// because no tab answers to it, (2) skip it at startup because it is not a VS Code session, or
/// (3) hand it a VS Code tab's label. Measured: a Desktop session's card closed as orphaned 15 min
/// after its turn ended while the session lived four more hours (12-11-2026), and the
/// same session stood in the tab-elimination count like an IDE session.
///
/// Narrow on purpose: an unknown entrypoint keeps today's behaviour, and a headless run (print
/// mode, sdk-*, `--bg`) is <see cref="HeadlessRun"/>'s question, asked separately by each caller.
/// Free of WPF so test harnesses can compile it.</summary>
public static class SessionHost
{
    public const string Terminal = "cli";
    public const string Desktop = "claude-desktop";

    public static bool IsOutsideIde(string? entrypoint)
        => string.Equals(entrypoint, Terminal, StringComparison.Ordinal) ||
           string.Equals(entrypoint, Desktop, StringComparison.Ordinal);

    /// <summary>Does the orphan sweep spare this session's card? Only a person's own session
    /// outside VS Code, and only while Claude Code's live-session registry shows its process
    /// running (<paramref name="processLive"/>: SessionRegistry.Probe reads Live; asked last and
    /// only then, because it reads the registry). Unknown or Gone closes as before, as does every
    /// VS Code session and every headless run.</summary>
    public static bool KeptByLiveProcess(string? entrypoint, bool headless, Func<bool> processLive)
        => IsOutsideIde(entrypoint) && !headless && processLive();
}
