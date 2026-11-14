using TabTower.Services;

// Self-test for closing an unassociated tab by label (Services/TabLabelClose.cs, 0.11.59).
// Every case but one is a refusal: the rule only ever lets a close through when the tab is provably
// nobody's. `selftest`: exit 0 = all passed.
if (args.Length != 1 || args[0] != "selftest")
{
    Console.Error.WriteLine("usage: TabLabelHarness selftest");
    return 2;
}

int failed = 0;
void Check(bool ok, string what)
{
    Console.WriteLine((ok ? "PASS  " : "FAIL  ") + what);
    if (!ok) failed++;
}

const string Sq = "\U0001F7EA";
string dead = Sq + " 12:40", live = Sq + " 13:11";
var window = new[] { "Claude Code", dead, live, "session 3" };
(bool Send, string Why) D(IReadOnlyList<string> tabs, string label, string holder = "", int elim = 0,
                          bool conn = true, bool sup = true, string ver = "0.7.3")
    => TabLabelClose.Decide(tabs, label, holder, elim, conn, sup, ver);

// LabelMatches: the deck's own rule
Check(TabLabelClose.LabelMatches(dead, dead), "equal labels match");
Check(TabLabelClose.LabelMatches("ABC", "abc"), "case is ignored");
Check(TabLabelClose.LabelMatches(Sq + " 1…", dead), "a truncated tab label matches the title it prefixes");
Check(!TabLabelClose.LabelMatches(dead, live), "two titles a few hours apart do not match");
Check(!TabLabelClose.LabelMatches("…", dead), "a bare ellipsis matches nothing");

// The one case that sends: the 30-10 shape, the dead session's tab beside its successor
var ok = D(window, dead);
Check(ok.Send && ok.Why == "", "dead session's tab, unique, nobody answers, no elimination: SEND");

// Refusals
Check(!D(window, "").Send, "empty label: refuse");
Check(!D(window, "   ").Send, "blank label: refuse");
Check(!D(window, dead, conn: false).Send, "no connector: refuse");
Check(!D(window, dead, sup: false, ver: "0.6.11").Send, "extension cannot close tabs: refuse");
Check(D(window, dead, sup: false, ver: "").Why.Contains("pre-0.6.12"), "no version reported: the answer says pre-0.6.12");
Check(!D(window, Sq + " 09:00").Send, "no tab carries the label: refuse");
Check(D(window, Sq + " 09:00").Why.StartsWith("no Claude tab"), "no tab: the answer says so");
Check(!D(new[] { dead, dead, live }, dead).Send, "two tabs share the label: refuse");
Check(!D(new[] { Sq + " 1…", dead }, dead).Send, "a truncated tab that also answers counts as a second tab: refuse");
Check(!D(new[] { Sq + " 1…", live }, Sq + " 1…").Send, "asked for a truncated label two titles may share: two answer, refuse");
var held = D(window, live, holder: "live-session-1");
Check(!held.Send && held.Why.Contains("live-session-1"), "a session answers to the label (the live successor): refuse, naming it");
Check(!D(window, dead, elim: 1).Send, "a live session holds a tab by elimination: refuse");
Check(!D(window, dead, holder: "x", elim: 2, conn: false).Send, "several reasons at once: still refused");
Check(!D(Array.Empty<string>(), dead).Send, "window reports no Claude tabs: refuse");
Check(D(window, "claude code").Send, "label compares case-insensitively, like the deck (one tab answers)");

Console.WriteLine(failed == 0 ? "ALL PASSED" : $"{failed} FAILED");
return failed == 0 ? 0 : 1;
