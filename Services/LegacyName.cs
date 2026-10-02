namespace TabTower.Services;

/// <summary>
/// The names this app shipped under before it became TabTower. Every place that still has to
/// recognise an installation from that time reads them from here and nowhere else: the config
/// folder that is copied over on first start, the auto-start value that is moved, the hook
/// registrations that are replaced rather than duplicated, and the single-instance lock that
/// keeps an old build and a new one from running side by side.
/// </summary>
public static class LegacyName
{
    /// <summary>%APPDATA% folder, Run value and product name.</summary>
    public const string Product = "SessionDeck";                         // public-gate: allow

    /// <summary>Single-instance mutex of the old builds.</summary>
    public const string Mutex = "SessionDeck_SingletonMutex";            // public-gate: allow

    /// <summary>File name of the old hook script, as it appears in settings.json commands.</summary>
    public const string HookScript = "sessiondeck-hook.ps1";             // public-gate: allow
}
