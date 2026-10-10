using System.Text.RegularExpressions;

namespace TabTower.Services;

/// <summary>Strips the blocks Claude Code prepends to a prompt before the user's own words, so a
/// card shows what was typed rather than the plumbing in front of it.
///
/// The case that forced it: with the browser extension connected, every prompt arrives as a
/// <c>&lt;browser_instruction&gt;</c> block of about 4,000 characters followed by the real text.
/// The hook trims a prompt to 400 characters and the card to 300, so the card's second line was
/// that block's first sentence on every session, and the transcript title skipped every such
/// prompt as markup.
///
/// The rule, kept narrow on purpose:
/// - Only LEADING blocks are removed, one after another, each a complete
///   <c>&lt;name ...&gt;...&lt;/name&gt;</c> pair. Anything after the first non-block text stays.
/// - The name must be an identifier with at least one <c>_</c> or <c>-</c> in it
///   (<c>browser_instruction</c>, <c>system-reminder</c>, <c>ide_selection</c>,
///   <c>command-name</c>, <c>pasted_content</c>). Every wrapper the harness writes has one and no
///   HTML element does, so a prompt that opens with <c>&lt;div&gt;...&lt;/div&gt;</c> is left
///   whole.
/// - <c>cross-session-message</c> is never stripped: it is an envelope whose CONTENT is the
///   prompt, and the existing unwrap code reads what is inside it.
/// - An open tag with no matching close (a truncated block, or a prompt that merely starts with
///   <c>&lt;div&gt;</c> or <c>&lt;</c>) stops the strip there; nothing is removed past it.
/// - If nothing is left once the blocks are gone, the original text is returned unchanged, so a
///   prompt that is ONLY a block behaves exactly as it did before.
///
/// <c>hooks/tabtower-hook.ps1</c> carries the same rule (Remove-InjectedPrefix), because the hook
/// trims before the app ever sees the text.</summary>
public static class InjectedPrefix
{
    private static readonly Regex OpenTag = new(
        @"^\s*<([A-Za-z][A-Za-z0-9]*(?:[_-][A-Za-z0-9]+)+)(?:\s[^<>]*)?>",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Envelopes whose content is the message itself (see the class remarks).</summary>
    private static bool IsEnvelope(string name) =>
        string.Equals(name, "cross-session-message", StringComparison.Ordinal);

    /// <summary>The text after any complete leading injected blocks, trimmed; the input itself
    /// when there are none or nothing remains after them.</summary>
    public static string Strip(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        string rest = text;
        bool stripped = false;
        while (OpenTag.Match(rest) is { Success: true } m && !IsEnvelope(m.Groups[1].Value))
        {
            string close = "</" + m.Groups[1].Value + ">";
            int end = rest.IndexOf(close, m.Length, StringComparison.Ordinal);
            if (end < 0) break;
            rest = rest[(end + close.Length)..];
            stripped = true;
        }
        if (!stripped) return text;
        rest = rest.Trim();
        return rest.Length > 0 ? rest : text;
    }

    /// <summary>True when the text opens with an injected-style tag whose close is missing: what
    /// a block looks like once something upstream has trimmed it (a hook from before this rule
    /// cut every browser-extension prompt to the first 400 characters of its block).</summary>
    public static bool StartsWithUnclosedBlock(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        var m = OpenTag.Match(text);
        return m.Success && !IsEnvelope(m.Groups[1].Value) &&
               text.IndexOf("</" + m.Groups[1].Value + ">", m.Length, StringComparison.Ordinal) < 0;
    }
}
