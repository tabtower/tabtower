using System.Text.RegularExpressions;

namespace TabTower.Services;

/// <summary>The one wording for "Claude is blocked on a permission dialog".
///
/// Three sources report the same dialog, and until 0.11.17 each said it its own way, so the
/// line under a waiting card depended on which of them had spoken last:
/// - the <c>PermissionRequest</c> hook, the moment the dialog opens (it sent the tool and its
///   argument alone, <c>Bash: npm test</c>);
/// - the transcript scanner, up to ten seconds later (<c>Waiting for permission: Bash</c>);
/// - Claude Code's own <c>Notification</c> message, where it fires
///   (<c>Claude needs your permission to use Bash</c>).
///
/// All three now open with the same words. The stable line is <see cref="Text"/>,
/// <c>Waiting for permission: Bash</c>. The hook, which is the only source that knows the
/// argument, puts it after the tool: <c>Waiting for permission: Bash: npm test</c>. That longer
/// line shows for as long as the hook's line always did, until the scanner confirms the call
/// and writes the stable one. <c>hooks/tabtower-hook.ps1</c> builds the hook's line.</summary>
public static class PermissionWait
{
    private const string Lead = "Waiting for permission";

    /// <summary>Claude Code's sentence for a permission dialog, as its Notification hook sends
    /// it. Anchored at both ends: only that exact sentence is rewritten.</summary>
    private static readonly Regex ClaudeCodeSentence = new(
        @"^Claude needs your permission to use (.+?)\.?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>The card line for a dialog on <paramref name="tool"/>.</summary>
    public static string Text(string? tool) =>
        string.IsNullOrWhiteSpace(tool) ? Lead : $"{Lead}: {tool.Trim()}";

    /// <summary>A hook detail in the deck's wording when it is Claude Code's sentence for a
    /// permission dialog. Any other text (a prompt, another notification) comes back unchanged.</summary>
    public static string Normalize(string detail)
    {
        var m = ClaudeCodeSentence.Match(detail.Trim());
        return m.Success ? Text(m.Groups[1].Value) : detail;
    }

    /// <summary>A hook detail as the card shows it. <paramref name="bound"/> is the caller's
    /// one-line clean-up and length limit for hook text.
    ///
    /// The hook's own permission line is the lead followed by the tool and its argument. Only
    /// that subject goes through <paramref name="bound"/>, exactly as it did when it stood
    /// alone, so the words in front of it do not take room away from the command.</summary>
    public static string ForCard(string detail, Func<string, string> bound)
    {
        const string prefix = Lead + ": ";
        string text = detail.TrimStart();
        if (text.StartsWith(prefix, StringComparison.Ordinal))
            return prefix + bound(text[prefix.Length..]);
        return Normalize(bound(detail));
    }
}
