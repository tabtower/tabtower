using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TabTower.Services.Phone;

/// <summary>
/// The "open in the Claude app" link of a live session, when Claude Code published one.
///
/// Every running Claude Code process writes <c>&lt;config dir&gt;/sessions/&lt;pid&gt;.json</c>, and with
/// Remote Control on, that file carries <c>bridgeSessionId</c>: the id the Claude app opens at
/// https://claude.ai/code/&lt;id&gt;. The file is undocumented, so it is read defensively: any
/// missing field, malformed file or dead process simply means no link, and the page then shows
/// no app button for that session. Only the .json files are read.
/// </summary>
public sealed class ClaudeSessionLinks
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(3);
    private static readonly Regex SafeId = new("^[A-Za-z0-9_-]{6,128}$", RegexOptions.Compiled);

    private readonly string _dir;
    private readonly object _gate = new();
    private Dictionary<string, string> _cache = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _live = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _at = DateTime.MinValue;

    public ClaudeSessionLinks(string? sessionsDir = null) => _dir = sessionsDir ?? DefaultDir();

    /// <summary>Claude Code's config folder: CLAUDE_CONFIG_DIR when set, else ~/.claude.</summary>
    public static string DefaultDir()
    {
        string? configured = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        string root = !string.IsNullOrWhiteSpace(configured)
            ? configured
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
        return Path.Combine(root, "sessions");
    }

    /// <summary>The app link for a session id, or null.</summary>
    public string? LinkFor(string sessionId)
    {
        var map = Read();
        return map.TryGetValue(sessionId, out var bridge) ? "https://claude.ai/code/" + bridge : null;
    }

    /// <summary>Sessions a running Claude Code process has registered, link or not. A session
    /// with no message yet has no transcript, so the deck cannot tell it from a spare id nobody
    /// is using; a live process registered for it is the evidence that it is real.</summary>
    public IReadOnlySet<string> LiveSessionIds()
    {
        Read();
        lock (_gate) return _live;
    }

    /// <summary>Read again on the next call, ignoring the short cache. For a caller polling for
    /// a session that has only just started.</summary>
    public void Invalidate()
    {
        lock (_gate) _at = DateTime.MinValue;
    }

    private Dictionary<string, string> Read()
    {
        lock (_gate)
        {
            if (DateTime.UtcNow - _at < CacheTtl) return _cache;
        }
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var live = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (Directory.Exists(_dir))
            {
                foreach (var file in Directory.EnumerateFiles(_dir, "*.json"))
                {
                    try
                    {
                        var info = new FileInfo(file);
                        if (info.Length > 256 * 1024) continue;
                        using var doc = JsonDocument.Parse(File.ReadAllText(file));
                        var root = doc.RootElement;
                        if (root.ValueKind != JsonValueKind.Object) continue;
                        string sid = Str(root, "sessionId");
                        string bridge = Str(root, "bridgeSessionId");
                        if (sid.Length == 0) continue;
                        if (!root.TryGetProperty("pid", out var pidEl) || !pidEl.TryGetInt32(out int pid) || !IsAlive(pid))
                            continue;
                        live.Add(sid);
                        if (SafeId.IsMatch(bridge)) map[sid] = bridge;
                    }
                    catch
                    {
                        // A file mid-write, or not what we expect: skipped this round.
                    }
                }
            }
        }
        catch
        {
            // The folder is unreadable: no links, which the page handles.
        }
        lock (_gate)
        {
            _cache = map;
            _live = live;
            _at = DateTime.UtcNow;
        }
        return map;
    }

    private static bool IsAlive(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return !p.HasExited;
        }
        catch (ArgumentException) { return false; }
        catch { return true; }   // exists but not inspectable: assume alive
    }

    private static string Str(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}
