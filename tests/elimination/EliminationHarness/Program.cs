using TabTower.Services;

// Self-test for scoping the elimination claim to the window (Services/EliminationScope.cs).
// The rule only ever REMOVES a claim, so every case that keeps one is something the deck does
// not know (no stamp, no mapping, an unidentified window). `selftest`: exit 0 = all passed.
if (args.Length != 1 || args[0] != "selftest")
{
    Console.Error.WriteLine("usage: EliminationHarness selftest");
    return 2;
}

int failed = 0;
void Check(bool ok, string what)
{
    Console.WriteLine((ok ? "PASS  " : "FAIL  ") + what);
    if (!ok) failed++;
}

HashSet<string> G(params string[] g) => new(g, StringComparer.OrdinalIgnoreCase);
Func<string, IReadOnlyCollection<string>?> Map(Dictionary<string, HashSet<string>> m)
    => l => m.TryGetValue(l, out var g) ? g : null;
string GroupOf(S s) => s.Group;

// MayClaim
Check(EliminationScope.MayClaim("orange", G("orange")), "own window shows the tab: may claim");
Check(!EliminationScope.MayClaim("orange", G("purple")), "only another window shows the tab: may not claim");
Check(EliminationScope.MayClaim("Orange", G("orange")), "group ids compare case-insensitively");
Check(EliminationScope.MayClaim("orange", G("purple", "orange")), "two windows show it, one is ours: may claim");
Check(EliminationScope.MayClaim("orange", G("purple", "")), "one reporting window unidentified: may claim (unknown keeps the old rule)");
Check(EliminationScope.MayClaim("", G("purple")), "session with no stamp: may claim (unknown keeps the old rule)");
Check(EliminationScope.MayClaim("orange", null), "label with no mapping: may claim");
Check(EliminationScope.MayClaim("orange", G()), "label mapped to no window: may claim");

// The incident of 30-10-2026 20:09: one orange session, one tab shown only by purple.
var orange = new S("o1", "orange");
var incident = new Dictionary<string, HashSet<string>> { ["🟪"] = G("purple") };
var (ss, tt) = EliminationScope.Scope(new[] { "🟪" }, new[] { orange }, GroupOf, Map(incident));
Check(ss.Count == 0 && tt.Count == 0, "30-10 incident: orange session and purple-only tab both scoped out, nothing to pair");
Check(EliminationScope.Pair(new[] { orange }, new[] { "🟪" }, GroupOf, Map(incident), 1).Count == 0,
      "30-10 incident: Pair hands the purple tab to nobody in orange");

// Same shape, tab in our own window: unchanged single pair.
var own = new Dictionary<string, HashSet<string>> { ["🟧"] = G("orange") };
(ss, tt) = EliminationScope.Scope(new[] { "🟧" }, new[] { orange }, GroupOf, Map(own));
Check(ss.Count == 1 && tt.Count == 1, "tab in the session's own window: the single pair survives");
var p1 = EliminationScope.Pair(ss, tt, GroupOf, Map(own), 1);
Check(p1.Count == 1 && p1[0].Tab == "🟧" && p1[0].Session == orange, "own-window pair is handed out");

// Mixed card: two windows, each with one tabless session and one unowned tab.
var purple = new S("p1", "purple");
var mixed = new Dictionary<string, HashSet<string>> { ["A"] = G("purple"), ["B"] = G("orange") };
(ss, tt) = EliminationScope.Scope(new[] { "A", "B" }, new[] { orange, purple }, GroupOf, Map(mixed));
Check(ss.Count == 2 && tt.Count == 2, "mixed card: both sessions and both tabs stay in the count");
var pm = EliminationScope.Pair(ss, tt, GroupOf, Map(mixed), 2);
Check(pm.Count == 2 && pm.Single(p => p.Session == orange).Tab == "B" && pm.Single(p => p.Session == purple).Tab == "A",
      "mixed card: each session takes the tab of its own window, never the first in the list");

// Two orange sessions, one orange tab and one purple tab: one claim, in the given order.
var orange2 = new S("o2", "orange");
var lop = new Dictionary<string, HashSet<string>> { ["A"] = G("purple"), ["B"] = G("orange") };
(ss, tt) = EliminationScope.Scope(new[] { "A", "B" }, new[] { orange, orange2 }, GroupOf, Map(lop));
Check(ss.Count == 2 && tt.Count == 1 && tt[0] == "B", "purple-only tab drops out; both orange sessions stay");
var po = EliminationScope.Pair(ss, tt, GroupOf, Map(lop), Math.Min(ss.Count, tt.Count));
Check(po.Count == 1 && po[0].Session == orange && po[0].Tab == "B", "the first in order takes the one orange tab");

// Duplicate labels are capacity.
var dup = new Dictionary<string, HashSet<string>> { ["X"] = G("orange") };
(ss, tt) = EliminationScope.Scope(new[] { "X", "X" }, new[] { orange, orange2 }, GroupOf, Map(dup));
Check(tt.Count == 2, "two tabs with one label stay two tabs");
Check(EliminationScope.Pair(ss, tt, GroupOf, Map(dup), 2).Count == 2, "and both are handed out");

// Nothing known: exactly the old behaviour.
var none = new Dictionary<string, HashSet<string>>();
var unstamped = new S("u", "");
(ss, tt) = EliminationScope.Scope(new[] { "A" }, new[] { unstamped, orange }, GroupOf, Map(none));
Check(ss.Count == 2 && tt.Count == 1, "card with no window mapping: nothing scoped out");

// max is respected.
Check(EliminationScope.Pair(new[] { orange, orange2 }, new[] { "X", "X" }, GroupOf, Map(dup), 1).Count == 1,
      "Pair stops at max");

// MayHoldTab (0.11.60): a headless run never owns a tab, however it says so.
Check(EliminationScope.MayHoldTab(false, "claude-vscode", null), "IDE session: may hold a tab");
Check(EliminationScope.MayHoldTab(false, null, null), "entrypoint not known yet: may hold a tab (unknown keeps the old rule)");
Check(!EliminationScope.MayHoldTab(false, "cli", null), "terminal session: may not hold a tab (0.11.68, SessionHost)");
Check(!EliminationScope.MayHoldTab(true, "claude-vscode", null), "`claude -p` that inherited claude-vscode (print mode proven): may not");
Check(!EliminationScope.MayHoldTab(false, "sdk-cli", null), "sdk-cli run, print mode never sent by the hook: may not");
Check(!EliminationScope.MayHoldTab(false, "SDK-ts", null), "sdk-* entrypoint compares case-insensitively: may not");

// The incident of 02-11-2026 18:45:16: in one window, a background SDK run (entrypoint sdk-cli,
// print mode false, titled) was the only tabless correlatable session when a freshly opened tab
// was renamed; the session inside that tab had no title yet. The single pair handed the tab's
// label to the background run, which then carried it as its card title.
const string newTab = "work on the parser";
var sdkRun = new H("bg-run", "o", false, "sdk-cli", null);
var incident0211 = new Dictionary<string, HashSet<string>> { [newTab] = G("o") };
var tabless = new[] { sdkRun }.Where(h => EliminationScope.MayHoldTab(h.PrintMode, h.Entrypoint, h.Kind)).ToList();
var (hs, ht) = EliminationScope.Scope(new[] { newTab }, tabless, h => h.Group, Map(incident0211));
Check(EliminationScope.Pair(hs, ht, h => h.Group, Map(incident0211), 1).Count == 0,
      "02-11 incident: the sdk-cli run is handed no tab, so it never carries the new tab's label");
var inTab = new H("ide-session", "o", false, "claude-vscode", null);
tabless = new[] { sdkRun, inTab }.Where(h => EliminationScope.MayHoldTab(h.PrintMode, h.Entrypoint, h.Kind)).ToList();
(hs, ht) = EliminationScope.Scope(new[] { newTab }, tabless, h => h.Group, Map(incident0211));
var p0211 = EliminationScope.Pair(hs, ht, h => h.Group, Map(incident0211), 1);
Check(p0211.Count == 1 && p0211[0].Session == inTab, "02-11 incident: with both in the count, the IDE session is the one paired");

// The session kind (0.11.64): a `claude --bg` run reports entrypoint "cli" and no print mode, so
// only the transcript's sessionKind ("bg") tells it from a terminal session.
Check(!EliminationScope.MayHoldTab(false, "cli", "bg"), "`claude --bg` run (entrypoint cli, sessionKind bg): may not hold a tab");
Check(!EliminationScope.MayHoldTab(false, "cli", "BG"), "sessionKind compares case-insensitively: may not");
Check(!EliminationScope.MayHoldTab(false, null, "bg"), "kind bg with the entrypoint not known yet: may not");
Check(!HeadlessRun.Is(false, "cli", null) && !EliminationScope.MayHoldTab(false, "cli", null),
      "terminal session, no kind: not headless, and still may not hold a tab (its host, 0.11.68)");
Check(EliminationScope.MayHoldTab(false, "claude-vscode", null), "IDE session, no kind: may hold a tab");
Check(EliminationScope.MayHoldTab(false, "claude-vscode", "interactive"), "any other kind than bg: may hold a tab (unknown keeps the old rule)");
Check(EliminationScope.MayHoldTab(false, "claude-vscode", ""), "empty kind: may hold a tab");
Check(HeadlessRun.Is(false, "cli", "bg"), "HeadlessRun.Is reads the kind: a bg run is headless");
Check(!HeadlessRun.Is(false, "cli", null), "HeadlessRun.Is: a terminal session is not");
Check(HeadlessRun.Is(true, "claude-vscode", null) && HeadlessRun.Is(false, "sdk-cli", null),
      "HeadlessRun.Is: print mode and sdk-* still decide as before");

// The incident of 03-11-2026 16:24:54: in one window, a `claude --bg` run (entrypoint cli, titled)
// was the only tabless correlatable session while a tab nobody had matched was on screen, and the
// single pair gave it that tab's label ("Claude Code"), which it kept as its card title. The bg run
// had no tab at all.
const string orphanTab = "Claude Code";
var bgRun = new H("bgrun1", "o", false, "cli", "bg");
var incident0311 = new Dictionary<string, HashSet<string>> { [orphanTab] = G("o") };
tabless = new[] { bgRun }.Where(h => EliminationScope.MayHoldTab(h.PrintMode, h.Entrypoint, h.Kind)).ToList();
(hs, ht) = EliminationScope.Scope(new[] { orphanTab }, tabless, h => h.Group, Map(incident0311));
Check(EliminationScope.Pair(hs, ht, h => h.Group, Map(incident0311), 1).Count == 0,
      "03-11 incident: the bg run is handed no tab, so it never carries the tab's label");
// Without the kind the same run is now refused by its entrypoint too (0.11.68): before, it was paired.
var bgRunNoKind = bgRun with { Kind = null };
tabless = new[] { bgRunNoKind }.Where(h => EliminationScope.MayHoldTab(h.PrintMode, h.Entrypoint, h.Kind)).ToList();
(hs, ht) = EliminationScope.Scope(new[] { orphanTab }, tabless, h => h.Group, Map(incident0311));
Check(EliminationScope.Pair(hs, ht, h => h.Group, Map(incident0311), 1).Count == 0,
      "03-11 control: the same run without the kind is not paired either (entrypoint cli, 0.11.68)");
var terminalRun = new H("t1", "o", false, "cli", null);
tabless = new[] { bgRun, inTab, terminalRun }.Where(h => EliminationScope.MayHoldTab(h.PrintMode, h.Entrypoint, h.Kind)).ToList();
(hs, ht) = EliminationScope.Scope(new[] { orphanTab }, tabless, h => h.Group, Map(incident0311));
Check(hs.Count == 1 && hs[0] == inTab, "03-11: with an IDE session and a terminal one beside it, only the IDE session stays in the count");

// Outside VS Code (0.11.68, Services/SessionHost.cs): a terminal or Claude Desktop session has no IDE tab.
Check(!EliminationScope.MayHoldTab(false, "claude-desktop", null), "Claude Desktop session: may not hold a tab");
Check(!EliminationScope.MayHoldTab(false, "claude-desktop", "interactive"), "Claude Desktop session, kind interactive: may not");
Check(SessionHost.IsOutsideIde("cli") && SessionHost.IsOutsideIde("claude-desktop"), "SessionHost: cli and claude-desktop run outside VS Code");
Check(!SessionHost.IsOutsideIde("claude-vscode") && !SessionHost.IsOutsideIde(null) && !SessionHost.IsOutsideIde("")
      && !SessionHost.IsOutsideIde("sdk-cli") && !SessionHost.IsOutsideIde("CLI"),
      "SessionHost: VS Code, unknown, sdk-* and a differently spelled value are not (unknown keeps the old rule)");

// The 12-11-2026 shape: a Desktop session stood in its window's elimination count. With a
// spare "Claude Code" tab there and an IDE session beside it, only the IDE session may be paired.
var desktop = new H("desktop1", "o", false, "claude-desktop", null);
tabless = new[] { desktop }.Where(h => EliminationScope.MayHoldTab(h.PrintMode, h.Entrypoint, h.Kind)).ToList();
(hs, ht) = EliminationScope.Scope(new[] { orphanTab }, tabless, h => h.Group, Map(incident0311));
Check(EliminationScope.Pair(hs, ht, h => h.Group, Map(incident0311), 1).Count == 0,
      "12-11 shape: a Desktop session alone is handed no tab, so it never carries a VS Code tab's label");
tabless = new[] { desktop, inTab }.Where(h => EliminationScope.MayHoldTab(h.PrintMode, h.Entrypoint, h.Kind)).ToList();
(hs, ht) = EliminationScope.Scope(new[] { orphanTab }, tabless, h => h.Group, Map(incident0311));
var p1211 = EliminationScope.Pair(hs, ht, h => h.Group, Map(incident0311), 1);
Check(p1211.Count == 1 && p1211[0].Session == inTab, "12-11 shape: beside an IDE session, the IDE session is the one paired");

// D1 (0.11.68): the orphan sweep spares a card only for a person's own session outside VS Code whose
// process the registry shows running; the registry is not read for anything else.
int probes = 0;
Func<bool> Live = () => { probes++; return true; };
Func<bool> NotLive = () => { probes++; return false; };
Check(SessionHost.KeptByLiveProcess("cli", false, Live), "orphan sweep: terminal session, process running: card kept");
Check(SessionHost.KeptByLiveProcess("claude-desktop", false, Live), "orphan sweep: Desktop session, process running: card kept");
Check(!SessionHost.KeptByLiveProcess("claude-desktop", false, NotLive), "orphan sweep: Desktop session, process gone or unknown: closes as before");
probes = 0;
Check(!SessionHost.KeptByLiveProcess("claude-vscode", false, Live) && probes == 0,
      "orphan sweep: VS Code session: unchanged, and the registry is not read");
Check(!SessionHost.KeptByLiveProcess(null, false, Live) && probes == 0, "orphan sweep: entrypoint unknown: unchanged, registry not read");
Check(!SessionHost.KeptByLiveProcess("cli", true, Live) && probes == 0, "orphan sweep: `--bg` / print run (headless): unchanged, registry not read");
Check(!SessionHost.KeptByLiveProcess("sdk-cli", false, Live) && probes == 0, "orphan sweep: sdk-cli run: unchanged, registry not read");

Console.WriteLine(failed == 0 ? "ALL PASSED" : $"{failed} FAILED");
return failed == 0 ? 0 : 1;

record S(string Id, string Group);
record H(string Id, string Group, bool PrintMode, string? Entrypoint, string? Kind);
