using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TabTower.Services;

/// <summary>One card badge, as an external producer described it.</summary>
/// <param name="Text">What the pill says. May carry <c>{until:ISO-8601}</c> tokens, drawn as a
/// live countdown ("2d", "3h", "28m", "now"), and <c>{at:ISO-8601}</c> tokens, drawn as a local
/// day and time.</param>
/// <param name="Tooltip">The pill's tooltip, same tokens.</param>
/// <param name="Level">0-100. Colours the pill: calm below 60, warm from 60, hot from 85.</param>
public sealed record BadgeReading(string Text, string Tooltip, int Level);

/// <summary>
/// Optional per-card badges, read off a JSON file another tool writes (config.json
/// <c>BadgesFile</c>). The deck knows nothing about what a badge means; the producer owns the
/// words, the colour level and how often the file is refreshed.
///
/// <code>
/// {
///   "updatedAt": "2026-10-02T10:00:00Z",
///   "staleAfterMinutes": 30,
///   "badges": [
///     { "group": "",     "text": "build green", "level": 0 },
///     { "group": "work", "text": "42% · {until:2026-10-04T08:00:00Z}",
///       "tooltip": "resets {at:2026-10-04T08:00:00Z}", "level": 42 }
///   ]
/// }
/// </code>
///
/// <c>group</c> is a session group id; "" is every card that belongs to no group.
///
/// A number in a card header is one nobody verifies, so it is shown only when it is known to
/// be current: an unreadable file shows nothing, a file older than its own
/// <c>staleAfterMinutes</c> (360 when absent) shows nothing, and a malformed entry is skipped
/// while the others go on showing.
/// </summary>
public static class BadgeReader
{
    private static string _path = "";
    private static DateTime _lastWriteUtc = DateTime.MinValue;
    private static DateTime _expiresUtc = DateTime.MinValue;
    private static Dictionary<string, BadgeReading> _byGroup = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Re-read if the file moved since last time. Cheap enough for the 10s metadata
    /// tick: a stat, and a parse only when the producer has actually been round.</summary>
    public static void Refresh(string? path)
    {
        try
        {
            path = path?.Trim() ?? "";
            if (path != _path) { _path = path; _lastWriteUtc = DateTime.MinValue; }
            if (path.Length == 0) { _byGroup = new(StringComparer.OrdinalIgnoreCase); return; }
            var info = new FileInfo(path);
            if (!info.Exists) { _byGroup = new(StringComparer.OrdinalIgnoreCase); return; }
            if (info.LastWriteTimeUtc == _lastWriteUtc) return;
            _lastWriteUtc = info.LastWriteTimeUtc;
            // ReadAllText rather than a byte-level parse: a PowerShell producer writes UTF-8
            // WITH a BOM, and JsonDocument.Parse chokes on one.
            _byGroup = Parse(File.ReadAllText(path), out _expiresUtc);
        }
        catch
        {
            // A badge is never worth an exception on the UI thread.
            _byGroup = new(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static Dictionary<string, BadgeReading> Parse(string json, out DateTime expiresUtc)
    {
        var result = new Dictionary<string, BadgeReading>(StringComparer.OrdinalIgnoreCase);
        expiresUtc = DateTime.MinValue;
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return result;

        // The whole file expires together: updatedAt is when the producer last spoke, and
        // staleAfterMinutes is its own statement of how long that stays true.
        if (!root.TryGetProperty("updatedAt", out var updatedAt)
            || updatedAt.ValueKind != JsonValueKind.String
            || !DateTime.TryParse(updatedAt.GetString(), out var updated))
            return result;
        int staleAfter = root.TryGetProperty("staleAfterMinutes", out var s)
                         && s.TryGetInt32(out int minutes) ? minutes : 360;
        expiresUtc = updated.ToUniversalTime() + TimeSpan.FromMinutes(staleAfter);

        if (!root.TryGetProperty("badges", out var badges) || badges.ValueKind != JsonValueKind.Array)
            return result;
        foreach (var b in badges.EnumerateArray())
        {
            if (b.ValueKind != JsonValueKind.Object) continue;
            string group = Str(b, "group");
            string text = Str(b, "text");
            if (text.Length == 0) continue;
            int level = b.TryGetProperty("level", out var l) && l.TryGetInt32(out int n) ? Math.Clamp(n, 0, 100) : 0;
            result[group] = new BadgeReading(text, Str(b, "tooltip"), level);
        }
        return result;
    }

    private static string Str(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    /// <summary>The badge for one group ("" = no group), or null when there is nothing
    /// current to show.</summary>
    public static BadgeReading? For(string groupId)
    {
        if (DateTime.UtcNow > _expiresUtc) return null;
        return _byGroup.TryGetValue(groupId, out var reading) ? reading : null;
    }

    private static readonly Regex Token = new(@"\{(until|at):([^}]+)\}", RegexOptions.Compiled);

    /// <summary>Expand the time tokens against the clock NOW, so a countdown stays live between
    /// two writes of the file.</summary>
    public static string Render(string template) => Token.Replace(template, m =>
    {
        if (!DateTime.TryParse(m.Groups[2].Value, out var stamp)) return m.Value;
        return m.Groups[1].Value == "until" ? Remaining(stamp) : stamp.ToLocalTime().ToString("ddd HH:mm");
    });

    /// <summary>"2d" / "3h" / "28m" - one unit, because a pill is read in passing.</summary>
    private static string Remaining(DateTime at)
    {
        var left = at.ToUniversalTime() - DateTime.UtcNow;
        if (left <= TimeSpan.Zero) return "now";
        if (left.TotalDays >= 1) return $"{(int)left.TotalDays}d";
        if (left.TotalHours >= 1) return $"{(int)left.TotalHours}h";
        return $"{Math.Max(1, (int)left.TotalMinutes)}m";
    }
}
