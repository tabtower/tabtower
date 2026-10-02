using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using TabTower.Models;

namespace TabTower.Services;

/// <summary>
/// Persistence with permanent auto-save: every change is queued and flushed
/// after a ~1s debounce; writes are atomic (temp file + rename). No "save" button exists.
/// </summary>
public sealed class ConfigStore
{
    public static readonly string ConfigDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TabTower");
    public static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly Func<AppConfig> _snapshot;
    private readonly DispatcherTimer _debounce;

    public ConfigStore(Func<AppConfig> snapshot)
    {
        _snapshot = snapshot;
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _debounce.Tick += (_, _) => { _debounce.Stop(); SaveNow(); };
    }

    public static AppConfig Load()
    {
        CopyFormerNameConfig();
        MigrateLegacyConfig();
        try
        {
            if (File.Exists(ConfigPath))
                return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigPath)) ?? new AppConfig();
        }
        catch
        {
            // Corrupt config — start fresh rather than crash on startup.
        }
        return new AppConfig();
    }

    /// <summary>One-time carry-over from the folder of the app's former name
    /// (<see cref="LegacyName.Product"/>): on the first start, while this app has no config of
    /// its own yet, everything there except the logs is COPIED here - config.json, the toggle
    /// files, any state beside them. Copied rather than moved, so the old folder stays intact
    /// and going back to an old build loses nothing.</summary>
    private static void CopyFormerNameConfig()
    {
        try
        {
            string legacyDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), LegacyName.Product);
            if (File.Exists(ConfigPath) || !File.Exists(Path.Combine(legacyDir, "config.json")))
                return;
            foreach (string src in Directory.EnumerateFiles(legacyDir, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(legacyDir, src);
                if (relative.StartsWith("logs" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    continue;
                string dst = Path.Combine(ConfigDir, relative);
                if (File.Exists(dst)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                File.Copy(src, dst);
            }
        }
        catch
        {
            // Best-effort, like the migration below: a fresh config is an acceptable fallback.
        }
    }

    /// <summary>One-time migration from the pre-rename WinGrid config location (decision 19).</summary>
    private static void MigrateLegacyConfig()
    {
        try
        {
            string legacyDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinGrid");
            string legacyPath = Path.Combine(legacyDir, "config.json");
            if (File.Exists(ConfigPath) || !File.Exists(legacyPath))
                return;
            Directory.CreateDirectory(ConfigDir);
            File.Move(legacyPath, ConfigPath);
            if (Directory.GetFileSystemEntries(legacyDir).Length == 0)
                Directory.Delete(legacyDir);
        }
        catch
        {
            // Migration is best-effort; a fresh config is an acceptable fallback.
        }
    }

    public void QueueSave()
    {
        _debounce.Stop();
        _debounce.Start();
    }

    public void SaveNow()
    {
        _debounce.Stop();
        try
        {
            Directory.CreateDirectory(ConfigDir);
            string tmp = ConfigPath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_snapshot(), JsonOptions));
            File.Move(tmp, ConfigPath, overwrite: true);
        }
        catch
        {
            // Never let a failed save take down the app.
        }
    }
}
