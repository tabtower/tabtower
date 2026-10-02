using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TabTower.Services;

/// <summary>
/// Reads workspace metadata straight from the folder (decisions 17-18):
/// git branch from .git/HEAD, card color from .vscode/settings.json (Peacock).
/// All reads are best-effort — a workspace without git or Peacock is normal.
/// </summary>
public static class WorkspaceMetadata
{
    public static string NormalizePath(string path)
    {
        try { return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)).ToLowerInvariant(); }
        catch { return path.ToLowerInvariant(); }
    }

    public static string NameFromPath(string path)
    {
        try { return Path.GetFileName(Path.TrimEndingDirectorySeparator(path)); }
        catch { return path; }
    }

    /// <summary>VSCode window title contains " - {workspace} - Visual Studio Code"
    /// (or starts with "{workspace} - " when no editor is open).</summary>
    public static string BuildTitlePattern(string workspaceName)
        => $"(^|- ){Regex.Escape(workspaceName)} - Visual Studio Code";

    public static bool IsVsCodeProcess(string processName)
        => processName.StartsWith("Code", StringComparison.OrdinalIgnoreCase);

    /// <summary>Current branch from .git/HEAD (no git.exe involved): branch name for
    /// "ref: refs/heads/x", short SHA for detached HEAD, "" when not a git repo.</summary>
    public static string ReadBranch(string workspacePath)
    {
        try
        {
            if (workspacePath.Length == 0) return "";
            string gitDir = Path.Combine(workspacePath, ".git");
            if (File.Exists(gitDir))
            {
                // Worktree/submodule: .git is a file with "gitdir: <path>".
                string line = File.ReadAllText(gitDir).Trim();
                if (!line.StartsWith("gitdir:")) return "";
                gitDir = Path.GetFullPath(Path.Combine(workspacePath, line["gitdir:".Length..].Trim()));
            }
            string headPath = Path.Combine(gitDir, "HEAD");
            if (!File.Exists(headPath)) return "";
            string head = File.ReadAllText(headPath).Trim();
            if (head.StartsWith("ref: refs/heads/")) return head["ref: refs/heads/".Length..];
            return head.Length >= 7 ? head[..7] : head;   // detached HEAD
        }
        catch
        {
            return "";
        }
    }

    private static readonly JsonDocumentOptions Jsonc = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Card color from .vscode/settings.json: "peacock.color", else
    /// "workbench.colorCustomizations"."titleBar.activeBackground" (decision 18).</summary>
    public static string? ReadPeacockColor(string workspacePath)
    {
        try
        {
            if (workspacePath.Length == 0) return null;
            string settingsPath = Path.Combine(workspacePath, ".vscode", "settings.json");
            if (!File.Exists(settingsPath)) return null;

            using var doc = JsonDocument.Parse(File.ReadAllText(settingsPath), Jsonc);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            if (root.TryGetProperty("peacock.color", out var peacock) &&
                peacock.ValueKind == JsonValueKind.String)
                return peacock.GetString();

            if (root.TryGetProperty("workbench.colorCustomizations", out var custom) &&
                custom.ValueKind == JsonValueKind.Object &&
                custom.TryGetProperty("titleBar.activeBackground", out var titleBar) &&
                titleBar.ValueKind == JsonValueKind.String)
                return titleBar.GetString();

            return null;
        }
        catch
        {
            return null;
        }
    }
}
