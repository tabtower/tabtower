using System.IO;

namespace TabTower.Services;

/// <summary>
/// Minimal file logger (design 2026-07-22): one line per event, daily file under
/// %APPDATA%\TabTower\logs, no external dependencies.
///
/// Two levels. Info records state CHANGES and decisions — hook status updates, every
/// auto-acknowledge with its cause, connector lifecycle, unroutable syncs. Always on:
/// the bugs this exists for (issues 2+3) occur sporadically in production and are not
/// reproducible on demand, so a dev-only log would miss exactly the events that matter;
/// being event-driven the volume is negligible. Debug records the periodic INPUTS
/// (full sync snapshots, every 2s while VSCode is focused) — off by default, toggled at
/// runtime via `tabtower log --debug on|off`, persisted in config.
///
/// Retention: files older than <see cref="RetentionDays"/> are deleted at startup; a
/// runaway day stops at <see cref="MaxFileBytes"/> (one "truncated" marker, then silence
/// until the day rolls). Logging must never take the app down — every path swallows its
/// own exceptions.
/// </summary>
public static class LogService
{
    public static readonly string LogDir = Path.Combine(ConfigStore.ConfigDir, "logs");
    public const int RetentionDays = 14;
    public const long MaxFileBytes = 10 * 1024 * 1024;

    public static bool DebugEnabled { get; set; }

    private static readonly object Gate = new();
    private static string _day = "";
    private static string _path = "";
    private static long _written;
    private static bool _capped;

    public static void Info(string evt, string details) => Write("INFO ", evt, details);

    public static void Debug(string evt, string details)
    {
        if (DebugEnabled) Write("DEBUG", evt, details);
    }

    private static void Write(string level, string evt, string details)
    {
        try
        {
            lock (Gate)
            {
                var now = DateTime.Now;
                string day = now.ToString("yyyy-MM-dd");
                if (day != _day)
                {
                    _day = day;
                    _path = Path.Combine(LogDir, $"deck-{day}.log");
                    Directory.CreateDirectory(LogDir);
                    _written = File.Exists(_path) ? new FileInfo(_path).Length : 0;
                    _capped = false;
                }
                if (_capped) return;
                string line = $"{now:HH:mm:ss.fff}  {level}  {evt,-8}  {details}{Environment.NewLine}";
                if (_written + line.Length > MaxFileBytes)
                {
                    File.AppendAllText(_path,
                        $"{now:HH:mm:ss.fff}  INFO   log       truncated: daily cap reached{Environment.NewLine}");
                    _capped = true;
                    return;
                }
                File.AppendAllText(_path, line);
                _written += line.Length;
            }
        }
        catch { /* logging must never throw */ }
    }

    /// <summary>Delete this app's log files older than the retention window. Startup only.</summary>
    public static void CleanOldLogs()
    {
        try
        {
            if (!Directory.Exists(LogDir)) return;
            foreach (var file in Directory.GetFiles(LogDir, "deck-*.log"))
                if (DateTime.Now - File.GetLastWriteTime(file) > TimeSpan.FromDays(RetentionDays))
                    File.Delete(file);
        }
        catch { }
    }
}
