using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using TabTower.Interop;

namespace TabTower.Cli;

/// <summary>
/// `tabtower doctor`, and the same check once on the very first start: is the setup that makes
/// the deck work in place? Without the hooks every card stays grey and nothing on screen says
/// why; without the connector a click cannot reach the session's tab. Read-only, and local like
/// install-hooks, because it has to answer before the app has ever run.
/// </summary>
public static class SetupCheck
{
    /// <summary>One line of the check. Status is "ok", "FIX" (something to do) or "skip" (could
    /// not be checked here).</summary>
    public sealed record Finding(string Status, string Text);

    public static string DefaultSettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json");

    public static string DefaultExtensionsDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".vscode", "extensions");

    public static List<Finding> Check(string settingsPath, string extensionsDir)
    {
        var findings = new List<Finding> { CheckHooks(settingsPath) };
        if (!Directory.Exists(extensionsDir))
        {
            // A portable or custom VS Code setup keeps its extensions elsewhere. Saying nothing
            // about them beats a warning that is wrong for that whole setup.
            findings.Add(new("skip", $"VS Code extensions: no folder at {extensionsDir}, so they were not checked"));
            return findings;
        }
        findings.Add(CheckExtension(extensionsDir, "tabtower.tabtower-connector-", "TabTower connector extension",
            "Without it a click cannot open the session's tab. Fix: run install.ps1 again, " +
            "or code --install-extension with the .vsix from the release"));
        findings.Add(CheckExtension(extensionsDir, "anthropic.claude-code-", "Claude Code extension",
            "TabTower follows Claude Code sessions in VS Code tabs. Fix: install it from the " +
            "VS Code Marketplace (anthropic.claude-code)"));
        return findings;
    }

    private static Finding CheckHooks(string settingsPath)
    {
        const string fix = " Fix: tabtower install-hooks";
        if (!File.Exists(settingsPath))
            return new("FIX", $"hooks: {settingsPath} does not exist, so no card will change colour.{fix}");

        JsonObject? root;
        try
        {
            string text = File.ReadAllText(settingsPath);
            root = string.IsNullOrWhiteSpace(text) ? new JsonObject() : JsonNode.Parse(text) as JsonObject;
        }
        catch (JsonException)
        {
            root = null;
        }
        if (root == null)
            return new("FIX", $"hooks: {settingsPath} is not valid JSON, so Claude Code reads no hooks from it. Fix that file by hand.");

        var (registered, expected, scripts, former) = HookInstaller.Inspect(root);
        if (former)
            return new("FIX", $"hooks: some still run the script of the app's former name ({Services.LegacyName.HookScript}).{fix}");
        if (registered == 0)
            return new("FIX", $"hooks: none registered in {settingsPath}, so no card will change colour.{fix}");
        if (registered < expected)
            return new("FIX", $"hooks: {registered} of {expected} registered in {settingsPath}.{fix}");
        string? missing = scripts.FirstOrDefault(s => !File.Exists(s));
        if (missing != null)
            return new("FIX", $"hooks: they run {missing}, which does not exist.{fix}");
        return new("ok", $"hooks: {registered} of {expected} registered in {settingsPath}");
    }

    private static Finding CheckExtension(string extensionsDir, string folderPrefix, string what, string whyAndFix)
    {
        string? found = Directory.EnumerateDirectories(extensionsDir, folderPrefix + "*")
            .Select(Path.GetFileName).FirstOrDefault();
        return found != null
            ? new("ok", $"{what}: {found}")
            : new("FIX", $"{what}: not installed in {extensionsDir}. {whyAndFix}.");
    }

    public static int Run(string[] args)
    {
        NativeMethods.AttachConsole(NativeMethods.ATTACH_PARENT_PROCESS);

        string settingsPath = DefaultSettingsPath;
        string extensionsDir = DefaultExtensionsDir;
        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--settings" when i + 1 < args.Length:
                    settingsPath = Path.GetFullPath(args[++i]);
                    break;
                case "--extensions-dir" when i + 1 < args.Length:
                    extensionsDir = Path.GetFullPath(args[++i]);
                    break;
                default:
                    Console.Error.WriteLine($"tabtower: unknown option '{args[i]}'. " +
                                            "Usage: tabtower doctor [--settings <path>] [--extensions-dir <path>]");
                    return 1;
            }
        }

        var findings = Check(settingsPath, extensionsDir);
        Console.Out.WriteLine("TabTower setup check");
        foreach (var f in findings)
            Console.Out.WriteLine($"  {f.Status,-4}  {f.Text}");
        return findings.Any(f => f.Status == "FIX") ? 1 : 0;
    }
}
