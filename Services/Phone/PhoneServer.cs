using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TabTower.Services.Phone;

/// <summary>
/// The phone page: a small web app and its JSON API, served on 127.0.0.1 only and reached from
/// a phone through `tailscale serve`, which adds HTTPS and the tailnet in front of it.
///
/// Every request passes these checks, in this order, before anything else happens:
///   1. Funnel traffic (public internet) is refused outright.
///   2. The Host header must be localhost / 127.0.0.1 / [::1] or exactly this PC's own tailnet
///      name, which stops a DNS-rebinding page (or a request meant for another machine) from
///      talking to the port through a name it controls.
///   3. The device: `tailscale serve` sets X-Forwarded-For to the caller's tailnet address
///      (overwriting anything the caller sent). That address is resolved with `tailscale
///      whois` to a node, whose owner must be the same Tailscale user as this PC, and whose
///      StableID must be on the approved list. An unknown device starts a pairing request
///      that only the PC can approve, by typing the code the device shows. A request with NO
///      X-Forwarded-For is treated as this PC's own only when its Host is a loopback name;
///      with the tailnet name and no X-Forwarded-For it came through some other forwarder
///      and is refused.
///   4. API calls must carry the X-TabTower-Phone header. A custom header cannot be sent
///      cross-site without a CORS preflight, which this server never approves, so another
///      website open on the phone cannot drive the API with the phone's identity.
/// </summary>
public sealed class PhoneServer : IDisposable
{
    public const string ApiHeader = "X-TabTower-Phone";
    private const string ResourcePrefix = "phone/";

    private static readonly Regex SessionIdRx = new(
        "^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$", RegexOptions.Compiled);
    private static readonly Regex CardKey = new("^ws:[0-9]{1,6}(:[A-Za-z0-9_-]{1,32})?$", RegexOptions.Compiled);
    private static readonly Regex GroupId = new("^[A-Za-z0-9_-]{0,32}$", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly IPhoneBackend _backend;
    private readonly IDeviceResolver _resolver;
    private readonly PhonePairing _pairing;
    private readonly Func<string, string?> _linkFor;
    private readonly Action? _invalidateLinks;
    private readonly Action<string> _log;
    private readonly string _version;
    private readonly Dictionary<string, byte[]> _resources;
    private readonly SemaphoreSlim _actionGate = new(1, 1);
    private LoopbackHttpServer? _http;

    public PhoneServer(IPhoneBackend backend, IDeviceResolver resolver, PhonePairing pairing,
                       Func<string, string?> linkFor, Action? invalidateLinks,
                       Action<string> log, string version, Assembly resourceAssembly)
    {
        _backend = backend;
        _resolver = resolver;
        _pairing = pairing;
        _linkFor = linkFor;
        _invalidateLinks = invalidateLinks;
        _log = log;
        _version = version;
        _resources = LoadResources(resourceAssembly);
    }

    public int Port => _http?.Port ?? 0;

    /// <summary>Starts listening on 127.0.0.1:port. Throws when the port cannot be bound.</summary>
    public void Start(int port)
    {
        _http = new LoopbackHttpServer(port, Handle, _log);
        _http.Start();
        _log($"listening on 127.0.0.1:{port}");
    }

    public void Dispose()
    {
        _http?.Dispose();
        _http = null;
    }

    // ---- the pipeline ----

    /// <summary>Who is asking, as far as the checks are concerned.</summary>
    private sealed record Caller(bool Local, DeviceInfo? Device, PairState State, string Code, string? Refusal);

    internal async Task<HttpResponseData> Handle(HttpRequestData req)
    {
        // The socket is bound to loopback, so this can only fail if that ever changes.
        if (req.Remote is not { } remote || !IPAddress.IsLoopback(remote))
            return Refuse(req, "forbidden", "not loopback");

        if (req.Headers.ContainsKey("Tailscale-Funnel-Request"))
            return Refuse(req, "funnel", "Funnel request");

        // The PC's own tailnet name, for the Host check. Null while Tailscale is down, and then
        // only the loopback names are accepted.
        var self = await _resolver.SelfAsync();
        var host = ClassifyHost(req.Header("Host"), self?.DnsName);
        if (host == HostKind.Other)
            return Refuse(req, "bad_host", $"host \"{Trunc(req.Header("Host"), 80)}\"");

        var caller = await Identify(req, host, self);
        if (caller.Refusal != null)
            return Refuse(req, caller.Refusal, caller.Device != null ? $"device {caller.Device.Name}" : "device unknown");

        // Static files: the page itself and its string tables. Served to a device that is still
        // waiting for approval too, because the page is what shows it the match code.
        if (req.Method == "GET" && req.Path is "/" or "/index.html")
            return Resource("index.html", "text/html; charset=utf-8");
        if (req.Method == "GET" && req.Path == "/i18n")
            return Strings(req);

        if (!req.Path.StartsWith("/api/", StringComparison.Ordinal))
            return Error(404, "not_found");

        if (req.Header(ApiHeader) != "1")
            return Refuse(req, "missing_header", "no API header");

        if (caller.State == PairState.Pending)
            return JsonResponse(403, new { ok = false, error = "pairing", code = caller.Code, device = caller.Device?.Name ?? "" });
        if (caller.State == PairState.Denied)
            return JsonResponse(403, new { ok = false, error = "denied" });
        if (caller.State == PairState.Busy)
            return JsonResponse(403, new { ok = false, error = "pairing_busy" });

        return (req.Method, req.Path) switch
        {
            ("GET", "/api/list") => await List(caller),
            ("GET", "/api/closed") => await Closed(),
            ("POST", "/api/new") => await Exclusive(() => New(req)),
            ("POST", "/api/close") => await Exclusive(() => Close(req)),
            ("POST", "/api/reopen") => await Exclusive(() => Reopen(req)),
            _ => UnknownRoute(req),
        };
    }

    internal enum HostKind { Loopback, Self, Other }

    /// <summary>What the Host header names: a loopback name (localhost, 127.0.0.1, [::1]), this
    /// PC's own tailnet name (exactly, case-insensitive, trailing dot and port tolerated), or
    /// anything else, which is refused: a page that resolved its own domain to 127.0.0.1, or
    /// another tailnet machine's name.</summary>
    internal static HostKind ClassifyHost(string host, string? selfDnsName)
    {
        if (host.Length == 0 || host.Length > 255) return HostKind.Other;
        string name = host;
        if (name.StartsWith('['))
        {
            int close = name.IndexOf(']');
            if (close < 0) return HostKind.Other;
            string rest = name[(close + 1)..];
            if (rest.Length > 0 && !(rest[0] == ':' && IsPort(rest[1..]))) return HostKind.Other;
            name = name[..(close + 1)];
        }
        else
        {
            int colon = name.LastIndexOf(':');
            if (colon >= 0)
            {
                if (!IsPort(name[(colon + 1)..])) return HostKind.Other;
                name = name[..colon];
            }
        }
        name = name.TrimEnd('.').ToLowerInvariant();
        if (name is "localhost" or "127.0.0.1" or "[::1]") return HostKind.Loopback;
        string self = (selfDnsName ?? "").TrimEnd('.').ToLowerInvariant();
        return self.Length > 0 && name == self ? HostKind.Self : HostKind.Other;
    }

    private static bool IsPort(string s) => s.Length is > 0 and <= 5 && s.All(char.IsAsciiDigit);

    private async Task<Caller> Identify(HttpRequestData req, HostKind host, SelfInfo? self)
    {
        string xff = req.Header("X-Forwarded-For");
        if (xff.Length == 0)
        {
            // No X-Forwarded-For: the request was not proxied by `tailscale serve` in HTTP mode.
            // It is trusted as this PC's own only when it also ADDRESSED this PC by a loopback
            // name. Anything that carries remote traffic to 127.0.0.1 without that header and
            // keeps the tailnet name (serve --tcp / --tls-terminated-tcp, an SSH or editor port
            // forward, WSL) would otherwise walk past pairing as the owner.
            return host == HostKind.Loopback
                ? new Caller(true, null, PairState.Approved, "", null)
                : new Caller(false, null, PairState.Denied, "", "not_forwarded");
        }

        // tailscale serve SETS this header to the caller's own address, so the last entry is the
        // one it wrote; anything earlier would be the caller's own claim and is ignored.
        string ip = xff.Split(',')[^1].Trim();
        if (!IPAddress.TryParse(ip, out _))
            return new Caller(false, null, PairState.Denied, "", "unknown_device");

        var device = await _resolver.WhoIsAsync(ip);
        if (self == null || device == null)
            return new Caller(false, device, PairState.Denied, "", "unknown_device");

        // The owner check. A tagged node belongs to no user; a node shared in from another
        // account belongs to that account. Neither is this PC's user.
        string login = req.Header("Tailscale-User-Login");
        if (device.Tagged ||
            !string.Equals(device.Login, self.Login, StringComparison.OrdinalIgnoreCase) ||
            (login.Length > 0 && !string.Equals(login, self.Login, StringComparison.OrdinalIgnoreCase)))
            return new Caller(false, device, PairState.Denied, "", "wrong_user");

        var (state, code) = _pairing.Check(device);
        return new Caller(false, device, state, code, null);
    }

    // ---- API ----

    private async Task<HttpResponseData> List(Caller caller)
    {
        var cards = await _backend.ListCardsAsync();
        var withLinks = cards.Select(c => c with
        {
            Sessions = c.Sessions.Select(s => s with { Link = _linkFor(s.Id) }).ToList(),
        }).ToList();
        return JsonResponse(200, new
        {
            ok = true,
            version = _version,
            device = new { local = caller.Local, name = caller.Device?.Name ?? "" },
            cards = withLinks,
        });
    }

    private async Task<HttpResponseData> Closed()
        => JsonResponse(200, new { ok = true, sessions = await _backend.ListClosedAsync() });

    private async Task<HttpResponseData> New(HttpRequestData req)
    {
        var body = ParseBody(req);
        string card = Prop(body, "card"), group = Prop(body, "group");
        if (!CardKey.IsMatch(card) || !GroupId.IsMatch(group)) return Error(400, "bad_request");
        _log($"new session: card={card} group={(group.Length > 0 ? group : "-")}");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var r = await _backend.NewSessionAsync(card, group, cts.Token);
        _log($"new session result: ok={r.Ok} error={r.Error ?? "-"} launching={r.Launching} id={r.SessionId ?? "-"}");
        string? link = r.Ok && r.SessionId != null ? await WaitForLink(r.SessionId, TimeSpan.FromSeconds(10)) : null;
        return JsonResponse(r.Ok ? 200 : r.Error is "not_found" ? 404 : 409, new
        {
            ok = r.Ok, error = r.Error, detail = r.Detail, launching = r.Launching, sessionId = r.SessionId, link,
        });
    }

    private async Task<HttpResponseData> Close(HttpRequestData req)
    {
        string id = Prop(ParseBody(req), "id");
        if (!SessionIdRx.IsMatch(id)) { _log($"close refused: bad id \"{Trunc(id, 60)}\""); return Error(400, "bad_request"); }
        _log($"close session {id}");
        var r = await _backend.CloseSessionAsync(id);
        _log($"close result: ok={r.Ok} error={r.Error ?? "-"} removed={r.Removed}{(r.Detail != null ? $" ({Trunc(r.Detail, 120)})" : "")}");
        return JsonResponse(r.Ok ? 200 : r.Error is "not_found" ? 404 : 409,
            new { ok = r.Ok, error = r.Error, detail = r.Detail, removed = r.Removed });
    }

    private async Task<HttpResponseData> Reopen(HttpRequestData req)
    {
        string id = Prop(ParseBody(req), "id");
        if (!SessionIdRx.IsMatch(id)) { _log($"reopen refused: bad id \"{Trunc(id, 60)}\""); return Error(400, "bad_request"); }
        _log($"reopen session {id}");
        var r = await _backend.ReopenSessionAsync(id);
        _log($"reopen result: ok={r.Ok} error={r.Error ?? "-"}{(r.Detail != null ? $" ({Trunc(r.Detail, 120)})" : "")}");
        string? link = r.Ok ? await WaitForLink(id, TimeSpan.FromSeconds(15)) : null;
        return JsonResponse(r.Ok ? 200 : r.Error is "not_found" ? 404 : 409,
            new { ok = r.Ok, error = r.Error, detail = r.Detail, link });
    }

    /// <summary>One action at a time: two quick taps must not open two sessions.</summary>
    private async Task<HttpResponseData> Exclusive(Func<Task<HttpResponseData>> action)
    {
        if (!await _actionGate.WaitAsync(0)) { _log("action refused: another one is still running (busy)"); return Error(409, "busy"); }
        try { return await action(); }
        finally { _actionGate.Release(); }
    }

    /// <summary>A fresh or resumed session publishes its app link a moment after it starts.</summary>
    private async Task<string?> WaitForLink(string sessionId, TimeSpan max)
    {
        var until = DateTime.UtcNow + max;
        while (true)
        {
            _invalidateLinks?.Invoke();
            if (_linkFor(sessionId) is { } link) return link;
            if (DateTime.UtcNow >= until) return null;
            await Task.Delay(1000);
        }
    }

    // ---- static resources ----

    private static Dictionary<string, byte[]> LoadResources(Assembly asm)
    {
        var map = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var name in asm.GetManifestResourceNames().Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal)))
        {
            using var s = asm.GetManifestResourceStream(name);
            if (s == null) continue;
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            map[name[ResourcePrefix.Length..]] = ms.ToArray();
        }
        return map;
    }

    private HttpResponseData Resource(string name, string type)
        => _resources.TryGetValue(name, out var bytes)
            ? new HttpResponseData { Status = 200, ContentType = type, Body = bytes }
            : Error(404, "not_found");

    /// <summary>The string table for the first language in the browser's list that has one,
    /// else English. Tables are the embedded files strings.&lt;language&gt;.json; adding a
    /// language is adding a file.</summary>
    private HttpResponseData Strings(HttpRequestData req)
    {
        string wanted = req.QueryValue("langs");
        if (wanted.Length == 0) wanted = req.Header("Accept-Language");
        foreach (var raw in wanted.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string tag = raw.Split(';')[0].Trim().ToLowerInvariant();
            if (!Regex.IsMatch(tag, "^[a-z]{2,3}(-[a-z0-9]{1,8})*$")) continue;
            foreach (var candidate in new[] { tag, tag.Split('-')[0] })
                if (_resources.ContainsKey($"strings.{candidate}.json"))
                    return StringTable(candidate);
        }
        return StringTable("en");
    }

    /// <summary>{ "lang": the table's language, "strings": the table }. The page sets its own
    /// lang and dir from this, so the code never has to name a language itself.</summary>
    private HttpResponseData StringTable(string lang)
    {
        if (!_resources.TryGetValue($"strings.{lang}.json", out var bytes)) return Error(404, "not_found");
        using var doc = JsonDocument.Parse(bytes);
        return JsonResponse(200, new { lang, strings = doc.RootElement.Clone() });
    }

    // ---- helpers ----

    private HttpResponseData Refuse(HttpRequestData req, string code, string why)
    {
        _log($"refused {req.Method} {Trunc(req.Path, 60)}: {code} ({why})");
        return Error(403, code);
    }

    private HttpResponseData UnknownRoute(HttpRequestData req)
    {
        _log($"no such API route: {req.Method} {Trunc(req.Path, 60)}");
        return Error(404, "not_found");
    }

    private static HttpResponseData Error(int status, string code) => JsonResponse(status, new { ok = false, error = code });

    private static HttpResponseData JsonResponse(int status, object body) => new()
    {
        Status = status,
        ContentType = "application/json; charset=utf-8",
        Body = JsonSerializer.SerializeToUtf8Bytes(body, Json),
    };

    private static JsonElement? ParseBody(HttpRequestData req)
    {
        try
        {
            using var doc = JsonDocument.Parse(req.Body.Length == 0 ? "{}"u8.ToArray() : req.Body);
            return doc.RootElement.ValueKind == JsonValueKind.Object ? doc.RootElement.Clone() : null;
        }
        catch { return null; }
    }

    private static string Prop(JsonElement? body, string name)
        => body is { } b && b.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? "" : "";

    private static string Trunc(string s, int max) => s.Length > max ? s[..max] + "..." : s;
}
