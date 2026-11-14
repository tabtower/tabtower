// Which tab a by-id close may close IN PLACE (0.7.3), apart from VS Code so `npm test` checks it.

/// Does a tab label belong to one of a session's titles? VS Code truncates long labels with a
/// trailing '…', and a truncated label matches any title it prefixes.
export function labelMatches(label: string, titles: string[]): boolean {
    for (const t of titles) {
        if (label === t) {
            return true;
        }
        if (label.endsWith('…') && label.length > 1 && t.startsWith(label.slice(0, -1))) {
            return true;
        }
    }
    return false;
}

export interface TabLike { label: string; claude: boolean }

/// The one Claude tab, across every editor group of this window, that carries one of the
/// session's labels, when TabTower vouched (Unique) that no other live session answers to it.
/// Undefined means "reveal first, as before": no vouching, no labels, no such tab, or several.
export function inPlaceTarget<T extends TabLike>(tabs: T[], labels: string[], unique: boolean): T | undefined {
    if (!unique || labels.length === 0) {
        return undefined;
    }
    const hits = tabs.filter((t) => t.claude && labelMatches(t.label, labels));
    return hits.length === 1 ? hits[0] : undefined;
}
