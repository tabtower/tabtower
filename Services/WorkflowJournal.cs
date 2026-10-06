using System.IO;
using System.Text.RegularExpressions;

namespace TabTower.Services;

/// <summary>How many agents of one Workflow run are alive right now, read from the run's own
/// <c>journal.jsonl</c> (in its transcript dir, which the Workflow tool's answer names).
///
/// Nothing else carries the number. The Stop hook lists a running workflow as ONE
/// <c>background_tasks</c> entry with no agent count, its agents are not <c>subagent</c>
/// entries, and they never wake the session, so no hook fires while they come and go. The
/// journal is written by the runner itself: one <c>started</c> line per agent as it comes up,
/// then a <c>result</c> or <c>failed</c> line for the same <c>key</c> when it ends.
///
/// Counted per KEY, by the last line seen for it, never as started minus finished: a retry
/// starts the same key again after a <c>failed</c> (measured 04-10-2026: 581 started, 518
/// failed, 56 results in one run), and a straight subtraction drifts. A run that was killed
/// leaves its last agents <c>started</c> forever (13 started, no result, in another), which is
/// why this is only ever asked about a run the Stop hook still lists as running.</summary>
public static class WorkflowJournal
{
    private const string FileName = "journal.jsonl";

    // type and key are the first two fields of every line the runner writes, so the head of
    // the line answers without parsing a result that can run to tens of kilobytes.
    private static readonly Regex Head = new(
        @"^\{""type"":""(started|result|failed)"",""key"":""([^""]+)""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private sealed record Cached(long Length, DateTime WriteUtc, int Live);
    private static readonly Dictionary<string, Cached> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Live agents in the run whose transcript dir is <paramref name="runDir"/>, or null
    /// when the journal cannot be read. Re-read only when the file changed, so a long run that
    /// is polled every tick costs one stat per tick between agents.</summary>
    public static int? CountLive(string runDir)
    {
        try
        {
            string path = Path.Combine(runDir, FileName);
            var info = new FileInfo(path);
            if (!info.Exists) return null;
            lock (Cache)
                if (Cache.TryGetValue(path, out var hit) &&
                    hit.Length == info.Length && hit.WriteUtc == info.LastWriteTimeUtc)
                    return hit.Live;

            var last = new Dictionary<string, bool>(StringComparer.Ordinal);   // key → still running
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream))
                while (reader.ReadLine() is { } line)
                {
                    var m = Head.Match(line.Length > 300 ? line[..300] : line);
                    if (m.Success) last[m.Groups[2].Value] = m.Groups[1].Value == "started";
                }
            int live = last.Values.Count(running => running);
            lock (Cache) Cache[path] = new Cached(info.Length, info.LastWriteTimeUtc, live);
            return live;
        }
        catch
        {
            return null;
        }
    }
}
