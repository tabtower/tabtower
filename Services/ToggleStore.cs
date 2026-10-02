using System.IO;

namespace TabTower.Services;

/// <summary>
/// Flag files for custom toggles (feature 2026-07-19): one file per toggle id under
/// %APPDATA%\TabTower\toggles, containing "1" (on) or "0" (off). Any external process
/// can read these files directly — they work even when TabTower isn't running,
/// and the last state persists across restarts. Best-effort: IO failures are swallowed.
/// </summary>
public static class ToggleStore
{
    public static readonly string Dir = Path.Combine(ConfigStore.ConfigDir, "toggles");

    /// <summary>Toggle ids double as file names — strip anything unsafe.</summary>
    public static string Sanitize(string id)
        => string.Concat(id.Where(c => !Path.GetInvalidFileNameChars().Contains(c)));

    public static bool Read(string id, bool fallback)
    {
        try
        {
            string path = Path.Combine(Dir, Sanitize(id));
            if (File.Exists(path))
                return File.ReadAllText(path).Trim() != "0";
        }
        catch { }
        return fallback;
    }

    public static void Write(string id, bool value)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(Path.Combine(Dir, Sanitize(id)), value ? "1" : "0");
        }
        catch { }
    }
}
