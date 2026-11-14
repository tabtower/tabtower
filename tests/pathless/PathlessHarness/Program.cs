using TabTower.Services;
using Facts = TabTower.Services.PathlessDuplicate.Facts;

// Self-test for retiring a folderless duplicate card (Services/PathlessDuplicate.cs). The rule
// deletes a card from the deck, so every case below that keeps a card is one condition loosened
// on its own. `selftest`: exit 0 = all passed, 1 = a failure.
if (args.Length != 1 || args[0] != "selftest")
{
    Console.Error.WriteLine("usage: PathlessHarness selftest");
    return 2;
}

int failed = 0;
void Check(bool ok, string what)
{
    Console.WriteLine((ok ? "PASS  " : "FAIL  ") + what);
    if (!ok) failed++;
}

// A card only a drag-in could have made, and a window its name was cut from that a foldered
// card's connector reports. Each case changes one thing.
var dup = new Card("dup", new Facts(Path: "", Name: "demo-app", SessionCount: 0, Hidden: false, LastUsedAt: null,
                                    UseCount: 0, CustomTitle: null, CustomColor: null, Description: ""));
const string Title = "main.cs - demo-app - Visual Studio Code";
var owned = new Win(Title, "demo-app (with folder)");

List<(Card Card, Win Window, string Owner)> Plan(IEnumerable<Card> cards, IReadOnlyList<Win> windows,
                                                 bool anyConnector = true)
    => PathlessDuplicate.Plan(cards, c => c.Facts, anyConnector, () => windows, w => w.Title, w => w.Owner);
bool Retires(Card c, params Win[] windows) => Plan(new[] { c }, windows).Any(r => r.Card.Id == c.Id);

// The one case that retires.
var hit = Plan(new[] { dup }, new[] { owned });
Check(hit.Count == 1 && hit[0].Card.Id == "dup" && hit[0].Window == owned && hit[0].Owner == owned.Owner,
      "every condition holds, title cut from a window a foldered card owns: retired, with that window and owner");

// Each condition violated on its own keeps the card.
var one = new (string What, Facts Facts)[]
{
    ("has a folder", dup.Facts with { Path = @"C:\work\demo-app" }),
    ("has no name", dup.Facts with { Name = "" }),
    ("has a session", dup.Facts with { SessionCount = 1 }),
    ("is hidden", dup.Facts with { Hidden = true }),
    ("was used (LastUsedAt set)", dup.Facts with { LastUsedAt = new DateTime(2026, 1, 1) }),
    ("was used (UseCount 1)", dup.Facts with { UseCount = 1 }),
    ("has a custom title", dup.Facts with { CustomTitle = "My app" }),
    ("has a custom colour", dup.Facts with { CustomColor = "#3366CC" }),
    ("has a description", dup.Facts with { Description = "notes" }),
};
Check(PathlessDuplicate.IsSuspect(dup.Facts), "baseline card is a suspect");
foreach (var (what, facts) in one)
{
    Check(!PathlessDuplicate.IsSuspect(facts), $"card that {what}: not a suspect");
    Check(!Retires(dup with { Facts = facts }, owned), $"card that {what}: kept");
}

// The window side.
Check(!Retires(dup, owned with { Owner = null }), "window no foldered card's connector reports: kept");
Check(!Retires(dup), "no VS Code window at all: kept");
Check(!Retires(dup, new Win("main.cs - other-app - Visual Studio Code", "other-app")),
      "title the name was not cut from: kept");
bool read = false;
var none = PathlessDuplicate.Plan(new[] { dup }, c => c.Facts, false,
    () => { read = true; return new[] { owned }; }, w => w.Title, w => w.Owner);
Check(none.Count == 0, "no connector at all: kept");
Check(!read, "no connector at all: the windows are not even read");
read = false;
PathlessDuplicate.Plan(new[] { dup with { Facts = dup.Facts with { UseCount = 1 } } }, c => c.Facts, true,
    () => { read = true; return new[] { owned }; }, w => w.Title, w => w.Owner);
Check(!read, "no suspect: the windows are not even read");

// More than one card or window.
var twice = Plan(new[] { dup }, new[] { owned with { Owner = null }, owned, owned with { Owner = "second" } });
Check(twice.Count == 1 && twice[0].Owner == owned.Owner,
      "an unowned matching window is passed over; the card retires once, on the first owned one");
var used = dup with { Id = "used", Facts = dup.Facts with { UseCount = 3 } };
var mixed = Plan(new[] { used, dup }, new[] { owned });
Check(mixed.Count == 1 && mixed[0].Card.Id == "dup", "two cards on one window: only the unused one retires");

// TitleIsCutFrom / WorkspaceNameFromTitle, the match the drag-in made the name with.
Check(PathlessDuplicate.TitleIsCutFrom("demo-app", "demo-app"), "TitleIsCutFrom: the exact title");
Check(PathlessDuplicate.TitleIsCutFrom("demo-app", "demo-app - notes"), "TitleIsCutFrom: \"name - something\"");
Check(PathlessDuplicate.TitleIsCutFrom("demo-app", Title), "TitleIsCutFrom: \"file - name - Visual Studio Code\"");
Check(PathlessDuplicate.TitleIsCutFrom("demo-app", "main.cs - demo-app - Visual Studio Code - Insiders"),
      "TitleIsCutFrom: an Insiders title");
Check(!PathlessDuplicate.TitleIsCutFrom("demo-app", "demo-appfoo - Visual Studio Code"),
      "TitleIsCutFrom: \"namefoo - Visual Studio Code\" is a near miss");
Check(!PathlessDuplicate.TitleIsCutFrom("app", Title), "TitleIsCutFrom: a name that is only part of the segment");
Check(!PathlessDuplicate.TitleIsCutFrom("Demo-App", Title), "TitleIsCutFrom: case differs, no match");
Check(PathlessDuplicate.WorkspaceNameFromTitle(Title) == "demo-app", "WorkspaceNameFromTitle: segment before Visual Studio Code");
Check(PathlessDuplicate.WorkspaceNameFromTitle("demo-app - Visual Studio Code") == "demo-app",
      "WorkspaceNameFromTitle: no file part");
Check(PathlessDuplicate.WorkspaceNameFromTitle("demo-app") == "", "WorkspaceNameFromTitle: no separator, no name");

Console.WriteLine(failed == 0 ? "ALL PASSED" : $"{failed} FAILED");
return failed == 0 ? 0 : 1;

record Card(string Id, Facts Facts);
record Win(string Title, string? Owner);
