using TabTower.Services;

namespace RecoveryHarness;

// Two modes.
//
//   selftest       The rules of RegistryRecovery.Decide / Plan over hand-built records.
//                  Exit 0 = all passed.
//
//   plan --sessions-dir <dir> --projects-dir <dir> [--known <id>=open|closed:<reason>]...
//                  What the deck would do at startup over a REAL registry folder: reads it with the
//                  app's own SessionRegistry.ReadRecords (real pid and start-time checks, real
//                  process paths, real transcript lookup), plans with RegistryRecovery.Plan against
//                  the --known cards, and prints one line per record plus the resulting card list.
//
//   probe <id>     SessionRegistry.Probe over the registry CLAUDE_CONFIG_DIR points at: "Live", or
//                  the state and why. A card click never resumes a Live session in a terminal (0.11.62).
public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "selftest") return SelfTest();
        if (args.Length > 0 && args[0] == "plan") return PlanCommand(args);
        if (args.Length == 2 && args[0] == "probe")
        {
            var (state, why) = SessionRegistry.Probe(args[1]);
            Console.WriteLine(why.Length > 0 ? $"{state}: {why}" : $"{state}");
            return 0;
        }
        Console.Error.WriteLine("usage: RecoveryHarness selftest | plan --sessions-dir D --projects-dir P [--known id=open|closed:reason]... | probe <id>");
        return 2;
    }

    private static int PlanCommand(string[] args)
    {
        string? sessions = null, projects = null;
        var known = new Dictionary<string, KnownSession>(StringComparer.OrdinalIgnoreCase);
        for (int i = 1; i < args.Length - 1; i++)
        {
            switch (args[i])
            {
                case "--sessions-dir": sessions = args[++i]; break;
                case "--projects-dir": projects = args[++i]; break;
                case "--known":
                    var (id, state) = (args[++i].Split('=', 2)[0], args[i].Split('=', 2)[1]);
                    known[id] = state == "open" ? new KnownSession(false, null)
                        : new KnownSession(true, state.StartsWith("closed:") ? state["closed:".Length..] : null);
                    break;
            }
        }
        if (sessions == null || projects == null) { Console.Error.WriteLine("plan needs --sessions-dir and --projects-dir"); return 2; }

        var records = SessionRegistry.ReadRecords(sessions, projects);
        var plan = RegistryRecovery.Plan(records, id => known.TryGetValue(id, out var k) ? k : null);
        foreach (var (r, action, why) in plan.OrderBy(p => p.Record.SessionId, StringComparer.Ordinal))
            Console.WriteLine($"{action.ToString().ToUpperInvariant(),-6} session={r.SessionId} pid={r.Pid} " +
                              $"verified={r.ProcessVerified} ide={r.IdeBinary} transcript={(r.Transcript != null ? "yes" : "no")}: {why}");
        Console.WriteLine("CARDS:");
        foreach (var id in known.Where(k => !k.Value.Closed).Select(k => k.Key)
                     .Concat(plan.Where(p => p.Action != RecoveryAction.Skip).Select(p => p.Record.SessionId!))
                     .OrderBy(x => x, StringComparer.Ordinal))
            Console.WriteLine($"  {id}");
        return 0;
    }

    private static int failed;

    private static void Check(bool ok, string what)
    {
        Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {what}");
        if (!ok) failed++;
    }

    private static int SelfTest()
    {
        const string id = "aaaaaaaa-0000-4000-8000-000000000001";
        const string id2 = "bbbbbbbb-0000-4000-8000-000000000002";
        RegistryRecord Live(string sid = id) => new(sid, 4242, "134368230427856414", @"C:\work\repo",
            "claude-vscode", "interactive", ProcessVerified: true, IdeBinary: true, Transcript: @"C:\t\" + sid + ".jsonl");
        RecoveryAction A(RegistryRecord r, KnownSession? k = null) => RegistryRecovery.Decide(r, k).Action;

        Check(A(Live()) == RecoveryAction.Create, "live VS Code session the deck does not know: card created");
        Check(A(Live() with { ProcessVerified = false }) == RecoveryAction.Skip, "process gone or pid reused: never created");
        Check(A(Live() with { ProcStart = null }) == RecoveryAction.Skip, "no recorded start time: identity unproven, skipped");
        Check(A(Live() with { ProcStart = "" }) == RecoveryAction.Skip, "empty start time: skipped");
        Check(A(Live() with { Pid = null }) == RecoveryAction.Skip, "no pid: skipped");
        Check(A(Live() with { SessionId = "not-a-guid" }) == RecoveryAction.Skip, "unparseable session id: skipped");
        Check(A(Live() with { SessionId = null }) == RecoveryAction.Skip, "no session id: skipped");
        Check(A(Live() with { Entrypoint = "sdk-cli" }) == RecoveryAction.Skip, "SDK / CLI entrypoint: skipped");
        Check(A(Live() with { Kind = "print" }) == RecoveryAction.Skip, "non-interactive kind: skipped");
        Check(A(Live() with { IdeBinary = false }) == RecoveryAction.Skip, "claude -p started from inside an IDE session: skipped");
        Check(A(Live() with { Transcript = null }) == RecoveryAction.Skip, "no transcript yet: skipped");
        Check(A(Live() with { Cwd = null }) == RecoveryAction.Skip, "no cwd: skipped");

        // Outside VS Code (0.11.68): a terminal or Claude Desktop session is recovered on the same
        // identity proof, without the IDE-binary test that has no counterpart there.
        RegistryRecord Outside(string entrypoint) => Live() with { Entrypoint = entrypoint, IdeBinary = false };
        Check(A(Outside("cli")) == RecoveryAction.Create, "live terminal session the deck does not know: card created");
        Check(A(Outside("claude-desktop")) == RecoveryAction.Create, "live Claude Desktop session the deck does not know: card created");
        Check(A(Outside("cli") with { Kind = "bg" }) == RecoveryAction.Skip, "`claude --bg` run (kind bg): skipped");
        Check(A(Outside("claude-desktop") with { ProcessVerified = false }) == RecoveryAction.Skip, "Desktop session whose process is gone: skipped");
        Check(A(Outside("cli") with { ProcStart = null }) == RecoveryAction.Skip, "terminal session with no recorded start time: skipped");
        Check(A(Outside("claude-desktop") with { Transcript = null }) == RecoveryAction.Skip, "Desktop process with no transcript (a pre-started spare): skipped");
        Check(A(Outside("cli"), new KnownSession(true, "orphaned")) == RecoveryAction.Revive, "terminal card closed as orphaned, process alive: revived");
        Check(A(Outside("claude-desktop"), new KnownSession(true, "other")) == RecoveryAction.Skip, "Desktop card a hook closed: left for its next hook");
        Check(A(Outside("CLI")) == RecoveryAction.Skip, "an entrypoint spelled differently: skipped (not guessed)");
        Check(A(Outside("sdk-ts")) == RecoveryAction.Skip, "sdk-* run outside the IDE binary: skipped");

        Check(A(Live(), new KnownSession(false, null)) == RecoveryAction.Skip, "already open on the deck: untouched");
        foreach (var reason in new[] { "orphaned", "stale", "exited" })
            Check(A(Live(), new KnownSession(true, reason)) == RecoveryAction.Revive, $"closed as {reason}, process alive: revived");
        foreach (var reason in new[] { "replaced", "logout", "prompt_input_exit", "clear", "other" })
            Check(A(Live(), new KnownSession(true, reason)) == RecoveryAction.Skip, $"closed by {reason}: left for its next hook");
        Check(A(Live(), new KnownSession(true, null)) == RecoveryAction.Skip, "closed with no reason: left closed");
        Check(A(Live() with { ProcessVerified = false }, new KnownSession(true, "orphaned")) == RecoveryAction.Skip,
              "orphan-closed card whose process is gone: stays closed (never resurrected)");
        Check(A(Live() with { Transcript = null }, new KnownSession(true, "orphaned")) == RecoveryAction.Revive,
              "revive does not need the transcript (the card already has one)");

        // Plan: a fork (two live processes, one session) is acted on once.
        var plan = RegistryRecovery.Plan(new[] { Live(), Live() with { Pid = 4343 }, Live(id2) }, _ => null);
        Check(plan.Count(p => p.Action == RecoveryAction.Create) == 2, "plan: two sessions, three records: two cards");
        Check(plan[1].Action == RecoveryAction.Skip, "plan: the second process of the same session is skipped");
        var mixed = RegistryRecovery.Plan(new[] { Live(), Live(id2) },
            sid => sid == id2 ? new KnownSession(true, "orphaned") : new KnownSession(false, null));
        Check(mixed[0].Action == RecoveryAction.Skip && mixed[1].Action == RecoveryAction.Revive, "plan: lookup decides per id");

        Console.WriteLine(failed == 0 ? "ALL PASSED" : $"{failed} FAILED");
        return failed == 0 ? 0 : 1;
    }
}
