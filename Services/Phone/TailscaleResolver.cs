using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace TabTower.Services.Phone;

/// <summary>
/// Asks the local Tailscale client who a tailnet address belongs to (`tailscale whois`) and
/// who this machine is (`tailscale status`). The CLI is used rather than the LocalAPI because
/// on Windows the LocalAPI is a named pipe with its own auth handshake, and the CLI already
/// speaks it, needs no admin, and prints JSON.
///
/// Answers are cached per address for a minute (a failed lookup for ten seconds), so a page
/// polling every few seconds costs one process start per minute, not one per request.
/// </summary>
public sealed class TailscaleResolver : IDeviceResolver
{
    private static readonly TimeSpan HitTtl = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan MissTtl = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan SelfTtl = TimeSpan.FromMinutes(5);

    private readonly object _gate = new();
    private readonly Dictionary<string, (DeviceInfo? Info, DateTime At)> _whois = new();
    private (SelfInfo? Info, DateTime At) _self;
    private readonly Action<string>? _log;

    public TailscaleResolver(Action<string>? log = null) => _log = log;

    public async Task<DeviceInfo?> WhoIsAsync(string ip)
    {
        lock (_gate)
        {
            if (_whois.TryGetValue(ip, out var c) && DateTime.UtcNow - c.At < (c.Info != null ? HitTtl : MissTtl))
                return c.Info;
        }
        var json = await RunJson("whois", "--json", ip);
        DeviceInfo? info = json is { } j ? ParseWhois(j) : null;
        lock (_gate)
        {
            if (_whois.Count > 256) _whois.Clear();
            _whois[ip] = (info, DateTime.UtcNow);
        }
        return info;
    }

    public async Task<SelfInfo?> SelfAsync()
    {
        lock (_gate)
        {
            // A failure is remembered for a short while too, so a Tailscale that is down costs
            // one process start per ten seconds rather than one per request.
            if (DateTime.UtcNow - _self.At < (_self.Info != null ? SelfTtl : MissTtl)) return _self.Info;
        }
        var json = await RunJson("status", "--json");
        SelfInfo? info = json is { } j ? ParseStatus(j) : null;
        lock (_gate) _self = (info, DateTime.UtcNow);
        return info;
    }

    /// <summary>`tailscale whois --json` → Node.StableID, Node.ComputedName / Hostinfo, the
    /// owning user's login, and whether the node is tagged (a tagged node has no user).</summary>
    public static DeviceInfo? ParseWhois(JsonElement root)
    {
        if (!root.TryGetProperty("Node", out var node)) return null;
        string stable = Str(node, "StableID");
        if (stable.Length == 0) return null;
        string name = Str(node, "ComputedName");
        string os = "", hostName = "";
        if (node.TryGetProperty("Hostinfo", out var hi))
        {
            hostName = Str(hi, "Hostname");
            if (name.Length == 0) name = hostName;
            os = Str(hi, "OS");
        }
        if (name.Length == 0) name = Str(node, "Name").TrimEnd('.');
        bool tagged = node.TryGetProperty("Tags", out var tags) && tags.ValueKind == JsonValueKind.Array &&
                      tags.GetArrayLength() > 0;
        string login = root.TryGetProperty("UserProfile", out var up) ? Str(up, "LoginName") : "";
        return new DeviceInfo(stable, name, os, login, tagged, hostName);
    }

    /// <summary>`tailscale status --json` → the logged-in user of Self, and Self's StableID.</summary>
    public static SelfInfo? ParseStatus(JsonElement root)
    {
        if (!root.TryGetProperty("Self", out var self)) return null;
        string userId = self.TryGetProperty("UserID", out var uid) ? uid.ToString() : "";
        string login = "";
        if (userId.Length > 0 && root.TryGetProperty("User", out var users) &&
            users.ValueKind == JsonValueKind.Object && users.TryGetProperty(userId, out var u))
            login = Str(u, "LoginName");
        if (login.Length == 0) return null;
        return new SelfInfo(login, Str(self, "ID"), Str(self, "DNSName").TrimEnd('.'));
    }

    private static string Str(JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) &&
           v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    /// <summary>Runs the Tailscale CLI with no window and parses its stdout. Null on any failure.</summary>
    private async Task<JsonElement?> RunJson(params string[] args)
    {
        string exe = FindExe();
        try
        {
            var psi = new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi);
            if (p == null) return null;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var stdout = p.StandardOutput.ReadToEndAsync(cts.Token);
            _ = p.StandardError.ReadToEndAsync(cts.Token);
            await p.WaitForExitAsync(cts.Token);
            if (p.ExitCode != 0) return null;
            using var doc = JsonDocument.Parse(await stdout);
            return doc.RootElement.Clone();
        }
        catch (Exception ex)
        {
            _log?.Invoke($"tailscale {args[0]} failed: {ex.Message}");
            return null;
        }
    }

    private static string FindExe()
    {
        // The installer puts it here and on PATH; prefer the fixed location, then PATH.
        string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string fixedPath = Path.Combine(pf, "Tailscale", "tailscale.exe");
        return File.Exists(fixedPath) ? fixedPath : "tailscale";
    }
}
