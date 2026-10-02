using System.IO;
using System.Text.Json;
using SessionDeck.Models;

namespace SessionDeck.Services;

/// <summary>Outcome of loading the tasks file. Exactly one of Document/FileError is set;
/// RecordWarnings lists tasks that were skipped (missing id/name) while the rest loaded.
/// Errors are SHOWN, not swallowed — a stale list masquerading as fresh is worse than a
/// visible error state.</summary>
public sealed record TasksLoadResult(TasksDocument? Document, string? FileError, IReadOnlyList<string> RecordWarnings)
{
    public static TasksLoadResult Error(string message) => new(null, message, Array.Empty<string>());
}

public static class TasksFileService
{
    public const int SupportedVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>Load + parse with a short retry on IOException: the producer may be
    /// mid-write (file locked / half-renamed). ~4 attempts over ~1.5s before the error
    /// is declared real. Blocking — call from a background thread.</summary>
    public static TasksLoadResult LoadWithRetry(string path)
    {
        const int attempts = 4;
        for (int i = 0; ; i++)
        {
            try
            {
                if (!File.Exists(path))
                    return TasksLoadResult.Error($"File not found: {path}");
                return Parse(File.ReadAllText(path));
            }
            catch (IOException ex)
            {
                if (i + 1 >= attempts)
                    return TasksLoadResult.Error($"File is locked for reading (even after {attempts} attempts): {ex.Message}");
                Thread.Sleep(400);
            }
            catch (UnauthorizedAccessException ex)
            {
                return TasksLoadResult.Error($"No read permission for the file: {ex.Message}");
            }
        }
    }

    public static TasksLoadResult Parse(string json)
    {
        TasksDocument? doc;
        try
        {
            doc = JsonSerializer.Deserialize<TasksDocument>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            return TasksLoadResult.Error($"Invalid JSON: {ex.Message}");
        }
        if (doc == null)
            return TasksLoadResult.Error("Empty JSON");
        if (doc.Version != SupportedVersion)
            return TasksLoadResult.Error($"Unsupported file version (version={doc.Version}; supported: {SupportedVersion})");

        // Record-level validation: skip broken entries, keep the rest, and say so.
        var warnings = new List<string>();
        var valid = new List<TaskEntry>();
        for (int i = 0; i < doc.Tasks.Count; i++)
        {
            var t = doc.Tasks[i];
            if (string.IsNullOrWhiteSpace(t.Id))
                warnings.Add($"Record {i + 1}: no id — skipped");
            else if (string.IsNullOrWhiteSpace(t.Name))
                warnings.Add($"{t.Id}: no name — skipped");
            else
                valid.Add(t);
        }
        doc.Tasks = valid;
        return new TasksLoadResult(doc, null, warnings);
    }
}
