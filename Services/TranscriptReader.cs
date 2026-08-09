using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SessionDeck.Services;

/// <summary>Titles derived from a Claude Code transcript (.jsonl).</summary>
/// <param name="TabTitle">The exact label VSCode shows on the session's tab: the last
/// "custom-title" entry (/rename) when present, else the last "ai-title" entry.
/// Primary display title and the session↔tab correlation key.</param>
/// <param name="AutoTitle">Heuristic session title: last summary entry, else the first
/// real user prompt. Secondary display title.</param>
/// <param name="Pending">Set when the transcript's last assistant turn issued a tool call
/// that has no tool_result yet. Hook-independent: the VSCode extension doesn't fire
/// Notification/PostToolUse at all, so this is the only trustworthy "waiting" signal
/// there (issue 2026-07-20).</param>
/// <param name="LabelCandidates">Every string VSCode might be showing as this session's
/// tab label, newest first. The tab label is the ONLY handle the extension gives us on a
/// tab (there is no session id in the VSCode tab API), so correlation is string matching —
/// and matching a single title is too brittle: a session whose transcript has no
/// "ai-title" at all gets labelled from a user prompt instead, which no single title field
/// reproduces (issue 2026-07-20, second report). Matching against the whole candidate set
/// covers every labelling rule Claude Code uses without having to know which one applied.</param>
/// <param name="Entrypoint">Where this session runs: "claude-vscode" for the VSCode
/// extension, "cli"/"sdk-cli" for a terminal. Every message line carries it. Only a
/// VSCode-hosted session may be closed when its VSCode window exits — a terminal session
/// in the same folder outlives the window (decision 13 keeps the engine generic).</param>
/// <param name="LastMessageAtUtc">Timestamp of the newest line that has one — i.e. the
/// last real conversation event. NOT the file's mtime: Claude Code appends a timestampless
/// "last-prompt" record when a tab opens or closes, which bumps the mtime with no
/// conversation behind it and read as a sign of life to the orphan sweep (issue
/// 2026-08-09 — a session dead for three days kept its card).</param>
public sealed record TranscriptInfo(
    string? TabTitle,
    string? AutoTitle,
    PendingCall? Pending = null,
    IReadOnlyList<string>? LabelCandidates = null,
    string? Entrypoint = null,
    DateTime? LastMessageAtUtc = null);

/// <summary>A tool call with no tool_result yet — either Claude is blocked on the user,
/// or the tool is simply still running. <see cref="IsAsk"/> separates the two.</summary>
/// <param name="ToolName">The tool Claude called.</param>
/// <param name="Detail">Card text describing what Claude is waiting for.</param>
/// <param name="StartedAtUtc">When the call was issued, per the transcript timestamp.
/// Used to age a permission dialog past the confidence threshold.</param>
/// <param name="IsAsk">True for AskUserQuestion/ExitPlanMode — an unanswered call is
/// definitive proof Claude is blocked, no waiting period needed. False for every other
/// tool, where "no result yet" is indistinguishable from "still executing".</param>
public sealed record PendingCall(string ToolName, string Detail, DateTime StartedAtUtc, bool IsAsk);

/// <summary>
/// Single-pass transcript scanner. Best-effort: any parse failure yields nulls and the
/// card keeps its "session xxxxxxxx" title.
/// </summary>
public static class TranscriptReader
{
    private const int MaxTitleLength = 80;

    /// <summary>How many trailing lines are kept for the pending-question scan. An
    /// unanswered tool call is always in the last assistant turn, so a bounded tail is
    /// both sufficient and cheap on multi-MB transcripts.</summary>
    private const int TailLines = 300;

    /// <summary>How many recent prompts are kept as possible tab labels. The tab shows one
    /// of them; more history only raises the odds of colliding with another session.</summary>
    private const int MaxLabelCandidates = 8;

    /// <summary>Tools whose unanswered call means "Claude is blocked on the user".</summary>
    private static readonly string[] AskTools = { "AskUserQuestion", "ExitPlanMode" };

    public static TranscriptInfo ReadInfo(string path)
    {
        try
        {
            string? customTitle = null, aiTitle = null, summary = null, firstUserText = null;
            var prompts = new List<string>();
            var tail = new Queue<string>(TailLines);
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            while (reader.ReadLine() is { } line)
            {
                if (line.Length == 0) continue;
                if (tail.Count == TailLines) tail.Dequeue();
                tail.Enqueue(line);
                if (line.Contains("\"custom-title\""))
                {
                    // /rename. An empty value (rename cleared) falls back to the ai-title.
                    string? t = TryGetString(line, "custom-title", "customTitle");
                    if (t != null) customTitle = t.Length > 0 ? t : null;
                }
                else if (line.Contains("\"ai-title\""))
                    aiTitle = TryGetString(line, "ai-title", "aiTitle") ?? aiTitle;
                else if (line.Contains("\"last-prompt\""))
                {
                    // What VSCode falls back to for the tab label when the session never
                    // got an ai-title. Kept in order; only the tail is used.
                    if (Shorten(TryGetString(line, "last-prompt", "lastPrompt")) is { } p)
                    {
                        prompts.Remove(p);
                        prompts.Add(p);
                        if (prompts.Count > MaxLabelCandidates) prompts.RemoveAt(0);
                    }
                }
                else if (line.Contains("\"summary\""))
                    summary = TryGetString(line, "summary", "summary") ?? summary;
                else if (firstUserText == null && line.Contains("\"user\""))
                    firstUserText = TryReadUserText(line);
            }
            string? tabTitle = Shorten(customTitle ?? aiTitle);
            string? autoTitle = Shorten(summary ?? firstUserText);
            // Newest first: a tab is far more likely to carry a recent prompt than an old one.
            var candidates = new List<string>();
            foreach (var c in new[] { Shorten(customTitle), Shorten(aiTitle) })
                if (c != null && !candidates.Contains(c)) candidates.Add(c);
            // Prompts label only titleless sessions — VSCode always prefers the title when
            // one exists. Prompt candidates on a titled session produce false matches: a
            // forked session shares its prompt history with its origin, so the shared
            // prompts made both cards answer to the fork's tab label and the ambiguity
            // guard blocked auto-acknowledge for both (T-0313 follow-up).
            if (tabTitle == null)
            {
                for (int i = prompts.Count - 1; i >= 0; i--)
                    if (!candidates.Contains(prompts[i])) candidates.Add(prompts[i]);
                if (autoTitle != null && !candidates.Contains(autoTitle)) candidates.Add(autoTitle);
            }

            var (entrypoint, lastMessageAt) = ReadHostAndLastMessage(tail);
            return new TranscriptInfo(tabTitle, autoTitle, FindPendingCall(tail), candidates,
                                      entrypoint, lastMessageAt);
        }
        catch
        {
            return new TranscriptInfo(null, null);
        }
    }

    /// <summary>Walk the tail newest-first for the two root-level fields the sweeps need:
    /// the host this session runs in, and when it last actually said something.
    ///
    /// Read from the tail rather than the whole file on purpose. It is bounded (the file is
    /// multi-MB and the main loop is deliberately substring-based, not JSON-parsed), it is
    /// exact — a "timestamp" substring inside message content can't be mistaken for the
    /// root property — and for a resumed session it reflects the host it is running in
    /// NOW rather than the one it was born in.</summary>
    private static (string? Entrypoint, DateTime? LastMessageAtUtc) ReadHostAndLastMessage(
        IEnumerable<string> tail)
    {
        string? entrypoint = null;
        DateTime? lastMessageAt = null;
        foreach (var line in tail.Reverse())
        {
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) continue;
                if (entrypoint == null &&
                    root.TryGetProperty("entrypoint", out var ep) && ep.ValueKind == JsonValueKind.String)
                    entrypoint = ep.GetString();
                if (lastMessageAt == null &&
                    root.TryGetProperty("timestamp", out var ts) && ts.ValueKind == JsonValueKind.String &&
                    DateTime.TryParse(ts.GetString(), null,
                        System.Globalization.DateTimeStyles.AdjustToUniversal |
                        System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed))
                    lastMessageAt = parsed;
            }
            catch { }
            if (entrypoint != null && lastMessageAt != null) break;
        }
        return (entrypoint, lastMessageAt);
    }

    /// <summary>A tool_use with no matching tool_result. For AskUserQuestion/ExitPlanMode
    /// that alone proves Claude is blocked; for any other tool the caller must age it past
    /// a threshold first, since a running tool looks identical. Sidechain (subagent) lines
    /// are ignored — only the main conversation can block the user. A later human prompt
    /// clears earlier pending calls: "Fork conversation" copies history but drops some
    /// tool_result lines (parallel-call siblings off the parentUuid chain, T-0313), so an
    /// orphaned tool_use mid-history would otherwise read as pending forever.</summary>
    private static PendingCall? FindPendingCall(IEnumerable<string> tail)
    {
        var pending = new Dictionary<string, PendingCall>();
        var order = new List<string>();
        // Transcript lines are NOT strictly ordered: a tool_result line can precede its
        // own tool_use line (seen in the wild 2026-07-27 — same-second flush). Matching
        // must therefore be order-insensitive, or the call reads as pending forever and
        // the card is stuck orange.
        var resolved = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in tail)
        {
            bool hasUse = line.Contains("\"tool_use\"");
            bool hasResult = line.Contains("\"tool_result\"");
            bool maybeUser = line.Contains("\"role\":\"user\"");
            if (!hasUse && !hasResult && !maybeUser) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (root.TryGetProperty("isSidechain", out var side) && side.ValueKind == JsonValueKind.True)
                    continue;
                if (!root.TryGetProperty("message", out var message) ||
                    !message.TryGetProperty("content", out var content))
                    continue;
                DateTime stamp = root.TryGetProperty("timestamp", out var ts) &&
                                 DateTime.TryParse(ts.GetString(), null,
                                     System.Globalization.DateTimeStyles.AdjustToUniversal |
                                     System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed)
                    ? parsed : DateTime.UtcNow;
                bool sawToolBlock = false;
                if (content.ValueKind == JsonValueKind.Array)
                    foreach (var block in content.EnumerateArray())
                    {
                        if (!block.TryGetProperty("type", out var bt)) continue;
                        string? kind = bt.GetString();
                        if (kind == "tool_use")
                        {
                            sawToolBlock = true;
                            string? name = block.TryGetProperty("name", out var n) ? n.GetString() : null;
                            string? id = block.TryGetProperty("id", out var i) ? i.GetString() : null;
                            if (name == null || id == null || resolved.Contains(id)) continue;
                            bool isAsk = AskTools.Contains(name);
                            string detail = isAsk ? AskDetail(name, block) : $"Waiting for permission: {name}";
                            pending[id] = new PendingCall(name, detail, stamp, isAsk);
                            order.Add(id);
                        }
                        else if (kind == "tool_result")
                        {
                            sawToolBlock = true;
                            if (block.TryGetProperty("tool_use_id", out var rid) &&
                                rid.GetString() is { } rId)
                            {
                                resolved.Add(rId);
                                pending.Remove(rId);
                            }
                        }
                    }
                // A tool-free user message means the conversation moved past every call
                // issued before it — those can't be blocking. Timestamp-guarded so the
                // same-second flush reorder above can't clear a genuinely pending call.
                if (!sawToolBlock &&
                    root.TryGetProperty("type", out var rt) && rt.GetString() == "user" &&
                    !(root.TryGetProperty("isMeta", out var meta) && meta.ValueKind == JsonValueKind.True))
                    foreach (var id in order)
                        if (pending.TryGetValue(id, out var pc) && pc.StartedAtUtc <= stamp)
                            pending.Remove(id);
            }
            catch { }
        }
        // Prefer a definitive question over a merely-unfinished tool, then most recent.
        for (int i = order.Count - 1; i >= 0; i--)
            if (pending.TryGetValue(order[i], out var call) && call.IsAsk)
                return call;
        for (int i = order.Count - 1; i >= 0; i--)
            if (pending.TryGetValue(order[i], out var call))
                return call;
        return null;
    }

    /// <summary>Card text for a pending question: the question itself when available.</summary>
    private static string AskDetail(string toolName, JsonElement block)
    {
        if (toolName == "ExitPlanMode") return "Waiting for plan approval";
        try
        {
            if (block.TryGetProperty("input", out var input) &&
                input.TryGetProperty("questions", out var questions) &&
                questions.ValueKind == JsonValueKind.Array &&
                questions.EnumerateArray().FirstOrDefault() is { ValueKind: JsonValueKind.Object } first &&
                first.TryGetProperty("question", out var q) &&
                Shorten(q.GetString()) is { } text)
                return text;
        }
        catch { }
        return "Waiting for an answer to a question";
    }

    /// <summary>Case-insensitive text search over the raw transcript lines (search
    /// feature 2026-07-19). Best-effort: unreadable file = no match.</summary>
    public static bool ContainsText(string path, string needle)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            while (reader.ReadLine() is { } line)
                if (line.Contains(needle, StringComparison.OrdinalIgnoreCase))
                    return true;
        }
        catch { }
        return false;
    }

    private static string? TryGetString(string line, string expectedType, string property)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.TryGetProperty("type", out var type) && type.GetString() == expectedType &&
                root.TryGetProperty(property, out var value))
                return value.GetString();
        }
        catch { }
        return null;
    }

    private static string? TryReadUserText(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var type) || type.GetString() != "user") return null;
            if (root.TryGetProperty("isMeta", out var meta) && meta.ValueKind == JsonValueKind.True) return null;
            if (!root.TryGetProperty("message", out var message) ||
                !message.TryGetProperty("content", out var content)) return null;

            string? text = content.ValueKind switch
            {
                JsonValueKind.String => content.GetString(),
                JsonValueKind.Array => content.EnumerateArray()
                    .Where(e => e.TryGetProperty("type", out var t) && t.GetString() == "text")
                    .Select(e => e.TryGetProperty("text", out var txt) ? txt.GetString() : null)
                    .FirstOrDefault(t => t != null),
                _ => null,
            };
            // Command wrappers (<command-name>, <system-reminder>, caveats) aren't real prompts.
            if (text == null || text.StartsWith('<') || text.StartsWith("Caveat:")) return null;
            return text;
        }
        catch
        {
            return null;
        }
    }

    private static string? Shorten(string? title)
    {
        if (title == null) return null;
        title = Regex.Replace(title, @"\s+", " ").Trim();
        if (title.Length == 0) return null;
        return title.Length <= MaxTitleLength ? title : title[..(MaxTitleLength - 1)] + "…";
    }
}
