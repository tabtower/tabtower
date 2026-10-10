using System.Net;
using System.Text;
using System.Text.Json;
using TabTower.Services;
using TabTower.Services.Phone;

namespace PhoneHarness;

// Two modes.
//
//   selftest                 In-process checks of the request pipeline with a fake Tailscale:
//                            host / Funnel / header / owner / pairing / revoke / deny rules, the
//                            HTTP parser's limits, the pairing clock, and the string tables.
//                            Exit 0 = all passed.
//
//   serve --port P --control C [--titles <file>]
//                            The real server on 127.0.0.1:P with the REAL Tailscale resolver and
//                            a fake deck, plus a control API on 127.0.0.1:C for the test driver:
//                            GET /state, POST /approve?id=&code=, /deny?id=, /revoke?id=, /quit.
//                            /approve needs the code the device shows, as the PC prompt does.
//                            Used for browser checks and for requests from another tailnet device.
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "selftest") return await SelfTest.Run();
        if (args.Length > 0 && args[0] == "serve") return await Serve(args);
        Console.Error.WriteLine("usage: PhoneHarness selftest | serve --port P --control C [--titles file]");
        return 2;
    }

    private static string Arg(string[] args, string name, string fallback)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
    }

    private static async Task<int> Serve(string[] args)
    {
        int port = int.Parse(Arg(args, "--port", "7056"));
        int controlPort = int.Parse(Arg(args, "--control", "7057"));
        string titlesFile = Arg(args, "--titles", "");
        var titles = titlesFile.Length > 0 ? File.ReadAllLines(titlesFile).Where(l => l.Length > 0).ToList() : null;

        void Log(string m) => Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {m}");
        var backend = new FakeBackend(titles);
        var pairing = new PhonePairing();
        pairing.Requested += p => Log($"PAIRING REQUESTED device=\"{p.Device.Name}\" host=\"{p.Device.HostName}\" os={p.Device.Os} id={p.Device.StableId}");
        var server = new PhoneServer(backend, new TailscaleResolver(Log), pairing, backend.LinkFor, null, Log,
                                     "harness", typeof(Program).Assembly);
        server.Start(port);

        var quit = new TaskCompletionSource();
        var control = new LoopbackHttpServer(controlPort, req =>
        {
            string id = req.QueryValue("id");
            object body = req.Path switch
            {
                "/state" => new
                {
                    // The codes are deliberately NOT listed: the driver must read them off the
                    // device's own answer, the way a person reads them off the phone.
                    pending = pairing.Pending.Select(p => new { p.Device.StableId, p.Device.Name, p.Device.HostName, p.Device.Os }),
                    approved = pairing.Approved.Select(d => new { d.StableId, d.Name, d.Os }),
                },
                "/approve" => new { ok = pairing.Approve(id, req.QueryValue("code")) },
                "/deny" => Do(() => pairing.Deny(id)),
                "/revoke" => new { ok = pairing.Revoke(id) },
                "/quit" => Do(() => quit.TrySetResult()),
                _ => new { ok = false, error = "unknown" },
            };
            Log($"control {req.Path} {id}");
            return Task.FromResult(new HttpResponseData { Body = JsonSerializer.SerializeToUtf8Bytes(body) });
        }, Log);
        control.Start();
        Log($"serving on 127.0.0.1:{port}, control on 127.0.0.1:{controlPort}");
        await quit.Task;
        await Task.Delay(200);
        server.Dispose();
        control.Dispose();
        return 0;
    }

    private static object Do(Action a)
    {
        a();
        return new { ok = true };
    }
}

/// <summary>A Tailscale stand-in: addresses map to devices, and this PC has a fixed login.</summary>
public sealed class FakeResolver : IDeviceResolver
{
    public readonly Dictionary<string, DeviceInfo> Devices = new();
    public SelfInfo? Self = new("owner@example.com", "nSELF", "pc.example.ts.net");
    public Task<DeviceInfo?> WhoIsAsync(string ip) => Task.FromResult(Devices.TryGetValue(ip, out var d) ? d : null);
    public Task<SelfInfo?> SelfAsync() => Task.FromResult(Self);
}

public static class SelfTest
{
    private static int _failed, _passed;

    private static void Check(bool ok, string what)
    {
        if (ok) _passed++; else _failed++;
        Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {what}");
    }

    private static HttpRequestData Req(string method, string path, string host = "localhost:7056",
                                       Dictionary<string, string>? headers = null, string body = "",
                                       IPAddress? remote = null)
    {
        int q = path.IndexOf('?');
        var r = new HttpRequestData
        {
            Method = method,
            Path = q < 0 ? path : path[..q],
            Query = q < 0 ? "" : path[(q + 1)..],
            Body = Encoding.UTF8.GetBytes(body),
            Remote = remote ?? IPAddress.Loopback,
        };
        r.Headers["Host"] = host;
        foreach (var (k, v) in headers ?? new()) r.Headers[k] = v;
        return r;
    }

    private static Dictionary<string, string> Api(string? xff = null, string? login = null)
    {
        var h = new Dictionary<string, string> { [PhoneServer.ApiHeader] = "1" };
        if (xff != null) h["X-Forwarded-For"] = xff;
        if (login != null) h["Tailscale-User-Login"] = login;
        return h;
    }

    private static JsonElement J(HttpResponseData r) => JsonDocument.Parse(r.Body).RootElement;
    private static string Err(HttpResponseData r) => J(r).TryGetProperty("error", out var e) ? e.GetString() ?? "" : "";

    public static async Task<int> Run()
    {
        // ---- host check: loopback names, or exactly this PC's own tailnet name ----
        const string selfName = "pc.example.ts.net.";
        foreach (var h in new[] { "localhost", "localhost:7056", "127.0.0.1:7056", "[::1]:7056", "LOCALHOST" })
            Check(PhoneServer.ClassifyHost(h, selfName) == PhoneServer.HostKind.Loopback, $"host is loopback: {h}");
        foreach (var h in new[] { "pc.example.ts.net", "PC.Example.TS.NET:443", "pc.example.ts.net.", "pc.example.ts.net.:8443" })
            Check(PhoneServer.ClassifyHost(h, selfName) == PhoneServer.HostKind.Self, $"host is this PC: {h}");
        foreach (var h in new[] { "", "evil.example", "127.0.0.1.nip.io", "other.example.ts.net", "pc.example.ts.net.evil",
                                  "xpc.example.ts.net", "localhost:abc", "pc.example.ts.net/x", "[::1]x", "localhost:123456" })
            Check(PhoneServer.ClassifyHost(h, selfName) == PhoneServer.HostKind.Other, $"host refused: \"{h}\"");
        Check(PhoneServer.ClassifyHost("pc.example.ts.net", null) == PhoneServer.HostKind.Other,
              "with Tailscale down, a tailnet name is refused (only loopback names pass)");

        // ---- the pipeline ----
        var resolver = new FakeResolver();
        resolver.Devices["100.64.0.10"] = new DeviceInfo("nPHONE", "my-phone", "iOS", "owner@example.com", false);
        resolver.Devices["100.64.0.11"] = new DeviceInfo("nOTHER", "their-laptop", "windows", "someone@example.com", false);
        resolver.Devices["100.64.0.12"] = new DeviceInfo("nTAGGED", "build-server", "linux", "tagged-devices", true);
        resolver.Devices["100.64.0.13"] = new DeviceInfo("nTABLET", "my-tablet", "android", "OWNER@example.com", false);
        var backend = new FakeBackend { StartDelay = TimeSpan.FromMilliseconds(50) };
        var pairing = new PhonePairing();
        int prompts = 0;
        pairing.Requested += _ => prompts++;
        var server = new PhoneServer(backend, resolver, pairing, backend.LinkFor, null, _ => { }, "test", typeof(Program).Assembly);

        var r = await server.Handle(Req("GET", "/api/list", headers: Api(), remote: IPAddress.Parse("192.168.1.5")));
        Check(r.Status == 403 && Err(r) == "forbidden", "a non-loopback socket is refused");

        var funnel = Api(); funnel["Tailscale-Funnel-Request"] = "?1";
        r = await server.Handle(Req("GET", "/", headers: funnel));
        Check(r.Status == 403 && Err(r) == "funnel", "Funnel traffic is refused, even for the page");

        r = await server.Handle(Req("GET", "/api/list", host: "attacker.example", headers: Api()));
        Check(r.Status == 403 && Err(r) == "bad_host", "a foreign Host is refused (DNS rebinding)");
        r = await server.Handle(Req("GET", "/api/list", host: "other-pc.example.ts.net", headers: Api("100.64.0.10")));
        Check(r.Status == 403 && Err(r) == "bad_host", "another machine's tailnet name is refused, even with X-Forwarded-For");

        // No X-Forwarded-For: local only when the Host is a loopback name.
        r = await server.Handle(Req("GET", "/api/list", host: "pc.example.ts.net", headers: Api()));
        Check(r.Status == 403 && Err(r) == "not_forwarded", "tailnet Host without X-Forwarded-For is refused (tcp / tls-terminated forward)");
        r = await server.Handle(Req("GET", "/", host: "pc.example.ts.net:443"));
        Check(r.Status == 403 && Err(r) == "not_forwarded", "...the page too");
        r = await server.Handle(Req("GET", "/api/list", host: "127.0.0.1:7056", headers: Api()));
        Check(r.Status == 200, "127.0.0.1 Host without X-Forwarded-For is local (allowed)");

        r = await server.Handle(Req("GET", "/api/list"));
        Check(r.Status == 403 && Err(r) == "missing_header", "API without the custom header is refused");

        r = await server.Handle(Req("GET", "/api/list", headers: Api()));
        Check(r.Status == 200 && J(r).GetProperty("device").GetProperty("local").GetBoolean(), "local request (no X-Forwarded-For) is allowed");
        Check(J(r).GetProperty("cards").GetArrayLength() == 4, "list returns the fake deck's 4 cards");
        bool anyLink = J(r).GetProperty("cards").EnumerateArray().SelectMany(c => c.GetProperty("sessions").EnumerateArray())
            .Any(s => s.TryGetProperty("link", out var l) && l.ValueKind == JsonValueKind.String &&
                      l.GetString()!.StartsWith("https://claude.ai/code/"));
        Check(anyLink, "sessions with a bridge id carry the Claude app link");

        r = await server.Handle(Req("GET", "/", headers: new() { ["X-Forwarded-For"] = "100.64.0.99" }));
        Check(r.Status == 403 && Err(r) == "unknown_device", "an address Tailscale does not know is refused");

        r = await server.Handle(Req("GET", "/api/list", host: "pc.example.ts.net", headers: Api("100.64.0.11")));
        Check(r.Status == 403 && Err(r) == "wrong_user", "a device of another Tailscale user is refused");
        r = await server.Handle(Req("GET", "/api/list", host: "pc.example.ts.net", headers: Api("100.64.0.12")));
        Check(r.Status == 403 && Err(r) == "wrong_user", "a tagged device is refused");
        r = await server.Handle(Req("GET", "/api/list", host: "pc.example.ts.net", headers: Api("100.64.0.10", "someone@example.com")));
        Check(r.Status == 403 && Err(r) == "wrong_user", "a serve login header that disagrees with the owner is refused");
        Check(prompts == 0, "no pairing prompt was raised for refused devices");

        r = await server.Handle(Req("GET", "/api/list", host: "pc.example.ts.net", headers: Api("100.64.0.10", "owner@example.com")));
        string code = J(r).TryGetProperty("code", out var c1) ? c1.GetString() ?? "" : "";
        Check(r.Status == 403 && Err(r) == "pairing" && code.Length == 4, $"an unknown own device gets a pairing request (code {code})");
        Check(J(r).GetProperty("device").GetString() == "my-phone", "the pairing answer names the device");
        r = await server.Handle(Req("GET", "/", host: "pc.example.ts.net", headers: new() { ["X-Forwarded-For"] = "100.64.0.10" }));
        Check(r.Status == 200 && Encoding.UTF8.GetString(r.Body).Contains("<html"), "a pending device still gets the page (it shows the code)");
        r = await server.Handle(Req("GET", "/api/list", host: "pc.example.ts.net", headers: Api("100.64.0.10")));
        Check(Err(r) == "pairing" && J(r).GetProperty("code").GetString() == code, "the same request keeps the same code");
        Check(prompts == 1, "exactly one prompt per pending device");

        // X-Forwarded-For: only the LAST entry counts, the one tailscale serve wrote.
        r = await server.Handle(Req("GET", "/api/list", host: "pc.example.ts.net", headers: Api("100.64.0.10, 100.64.0.11")));
        Check(Err(r) == "wrong_user", "a forged leading X-Forwarded-For entry is ignored (last entry decides)");
        r = await server.Handle(Req("GET", "/api/list", host: "pc.example.ts.net", headers: Api("100.64.0.11, 100.64.0.10")));
        Check(Err(r) == "pairing", "the last entry is the device that is checked");

        Check(!pairing.Approve("nPHONE", code == "ZZZZ" ? "YYYY" : "ZZZZ"), "approving with a wrong code fails");
        Check(pairing.IsPending("nPHONE"), "...and the request is still pending");
        r = await server.Handle(Req("GET", "/api/list", host: "pc.example.ts.net", headers: Api("100.64.0.10")));
        Check(Err(r) == "pairing", "...and the device is still not let in");
        Check(pairing.Approve("nPHONE", " " + code.ToLowerInvariant() + " "), "approve with the code the device shows (case and spaces tolerated)");
        r = await server.Handle(Req("GET", "/api/list", host: "pc.example.ts.net", headers: Api("100.64.0.10")));
        Check(r.Status == 200, "an approved device gets the list");
        Check(!pairing.Approve("nPHONE", code), "approving twice does nothing");

        // Same user, different node: approved separately (case-insensitive login match).
        r = await server.Handle(Req("GET", "/api/list", host: "pc.example.ts.net", headers: Api("100.64.0.13")));
        Check(Err(r) == "pairing", "another device of the same user needs its own approval");
        pairing.Deny("nTABLET");
        r = await server.Handle(Req("GET", "/api/list", host: "pc.example.ts.net", headers: Api("100.64.0.13")));
        Check(r.Status == 403 && Err(r) == "denied", "a denied device is refused without a new prompt");
        Check(prompts == 2, "deny does not raise another prompt");

        // ---- actions ----
        r = await server.Handle(Req("POST", "/api/new", headers: Api("100.64.0.10"), host: "pc.example.ts.net", body: "{\"card\":\"ws:1\",\"group\":\"\"}"));
        string newId = J(r).TryGetProperty("sessionId", out var sid) ? sid.GetString() ?? "" : "";
        Check(r.Status == 200 && Guid.TryParse(newId, out _), $"new session returns the real new session id");
        Check(J(r).GetProperty("link").GetString()?.StartsWith("https://claude.ai/code/") == true, "new session returns its app link");
        r = await server.Handle(Req("GET", "/api/list", headers: Api()));
        var newRow = J(r).GetProperty("cards").EnumerateArray().SelectMany(c => c.GetProperty("sessions").EnumerateArray())
            .FirstOrDefault(s => s.GetProperty("id").GetString() == newId);
        Check(newRow.ValueKind == JsonValueKind.Object, "the new session is in the list at once, before any message");
        Check(newRow.ValueKind == JsonValueKind.Object && newRow.GetProperty("new").GetBoolean(), "...marked as new");
        Check(newRow.ValueKind == JsonValueKind.Object && newRow.GetProperty("link").GetString()?.StartsWith("https://claude.ai/code/") == true,
              "...with its app link");
        bool spareListed = J(r).GetProperty("cards").EnumerateArray().SelectMany(c => c.GetProperty("sessions").EnumerateArray())
            .Any(s => s.GetProperty("id").GetString() == backend.SpareSessionId);
        Check(!spareListed, "a spare phantom is not in the list");
        bool oldRowsNotNew = J(r).GetProperty("cards").EnumerateArray().SelectMany(c => c.GetProperty("sessions").EnumerateArray())
            .Where(s => s.GetProperty("id").GetString() != newId).All(s => !s.GetProperty("new").GetBoolean());
        Check(oldRowsNotNew, "sessions with messages are not marked new");
        r = await server.Handle(Req("POST", "/api/new", headers: Api(), body: "{\"card\":\"ws:2:personal\",\"group\":\"personal\"}"));
        Check(r.Status == 200 && J(r).GetProperty("launching").GetBoolean(), "a group whose window is starting reports launching");
        r = await server.Handle(Req("POST", "/api/new", headers: Api(), body: "{\"card\":\"ws:1; rm\",\"group\":\"\"}"));
        Check(r.Status == 400, "a malformed card key is refused");
        r = await server.Handle(Req("POST", "/api/new", headers: Api(), body: "{\"card\":\"ws:9\",\"group\":\"\"}"));
        Check(r.Status == 404, "an unknown card is not found");
        r = await server.Handle(Req("GET", "/api/new", headers: Api()));
        Check(r.Status == 404, "GET on an action route does nothing");

        // A session with messages, for the close-then-reopen round trip below.
        r = await server.Handle(Req("GET", "/api/list", headers: Api()));
        string realId = J(r).GetProperty("cards").EnumerateArray().SelectMany(c => c.GetProperty("sessions").EnumerateArray())
            .Where(s => !s.GetProperty("new").GetBoolean()).Select(s => s.GetProperty("id").GetString() ?? "").First();

        r = await server.Handle(Req("POST", "/api/close", headers: Api(), body: $"{{\"id\":\"{newId}\"}}"));
        Check(r.Status == 200, "close the new session");
        Check(J(r).GetProperty("removed").GetBoolean(), "...reported as removed: it had no message, so there is nothing to reopen");
        r = await server.Handle(Req("POST", "/api/close", headers: Api(), body: "{\"id\":\"not-an-id\"}"));
        Check(r.Status == 400, "close with a malformed id is refused");
        r = await server.Handle(Req("POST", "/api/close", headers: Api(), body: $"{{\"id\":\"{backend.HiddenSessionId}\"}}"));
        Check(r.Status == 404, "close of a live session the page does not list (headless) is refused");
        // The rule the list and close share: (closed, phantom, headless, replaced, evidentlyReal).
        Check(PhoneRules.IsListed(false, false, false, false, false), "an open session with messages is listed");
        Check(PhoneRules.IsListed(false, true, false, false, true), "a brand-new session (no message yet) with a live process, or opened from the phone, is listed");
        Check(!PhoneRules.IsListed(false, true, false, false, false), "a spare id with no message and no live process stays hidden");
        Check(!PhoneRules.IsListed(false, false, true, false, true) && !PhoneRules.IsListed(false, true, true, false, true),
              "a headless run stays hidden, new or not");
        Check(!PhoneRules.IsListed(true, false, false, false, true) && !PhoneRules.IsListed(false, false, false, true, true),
              "closed and replaced sessions stay hidden");
        r = await server.Handle(Req("POST", "/api/close", headers: Api(), body: $"{{\"id\":\"{backend.SpareSessionId}\"}}"));
        Check(r.Status == 404, "close of a spare (unlisted) phantom is refused");
        r = await server.Handle(Req("GET", "/api/closed", headers: Api()));
        Check(!J(r).GetProperty("sessions").EnumerateArray().Any(s => s.GetProperty("id").GetString() == newId),
              "a session closed before its first message is not in the recently-closed list (live report 09-10-2026)");
        r = await server.Handle(Req("POST", "/api/reopen", headers: Api(), body: $"{{\"id\":\"{newId}\"}}"));
        Check(r.Status == 404, "...and reopening it is refused as not found");

        r = await server.Handle(Req("POST", "/api/close", headers: Api(), body: $"{{\"id\":\"{realId}\"}}"));
        Check(r.Status == 200 && !J(r).GetProperty("removed").GetBoolean(), "close a session that has messages: kept, not removed");
        r = await server.Handle(Req("GET", "/api/closed", headers: Api()));
        Check(J(r).GetProperty("sessions").EnumerateArray().Any(s => s.GetProperty("id").GetString() == realId),
              "...it is in the recently-closed list");
        r = await server.Handle(Req("POST", "/api/reopen", headers: Api(), body: $"{{\"id\":\"{realId}\"}}"));
        Check(r.Status == 200 && J(r).GetProperty("ok").GetBoolean(), "reopen it");
        r = await server.Handle(Req("POST", "/api/reopen", headers: Api(), body: "{\"id\":\"not-an-id\"}"));
        Check(r.Status == 400, "reopen with a malformed id is refused");

        // Two actions at once: the second is refused rather than queued.
        backend.StartDelay = TimeSpan.FromMilliseconds(400);
        var first = server.Handle(Req("POST", "/api/new", headers: Api(), body: "{\"card\":\"ws:3\",\"group\":\"\"}"));
        await Task.Delay(50);
        var second = await server.Handle(Req("POST", "/api/new", headers: Api(), body: "{\"card\":\"ws:3\",\"group\":\"\"}"));
        Check(second.Status == 409 && Err(second) == "busy", "a second action while one runs is refused (no double session)");
        Check((await first).Status == 200, "the first action completes");

        // Revoke: the device is back to pairing, with a new prompt.
        Check(pairing.Revoke("nPHONE"), "revoke the phone");
        r = await server.Handle(Req("GET", "/api/list", host: "pc.example.ts.net", headers: Api("100.64.0.10")));
        Check(Err(r) == "pairing" && prompts == 3, "a revoked device has to pair again");

        // ---- pairing: a look-alike device, and the cap on waiting requests ----
        var cap = new PhonePairing();
        int capPrompts = 0;
        cap.Requested += _ => capPrompts++;
        var capServer = new PhoneServer(backend, resolver, cap, backend.LinkFor, null, _ => { }, "test", typeof(Program).Assembly);
        resolver.Devices["100.64.0.20"] = new DeviceInfo("nREAL", "iphone", "iOS", "owner@example.com", false, "iPhone");
        resolver.Devices["100.64.0.21"] = new DeviceInfo("nSPOOF", "iphone-1", "iOS", "owner@example.com", false, "iPhone");
        resolver.Devices["100.64.0.22"] = new DeviceInfo("nTHIRD", "laptop", "windows", "owner@example.com", false, "laptop");
        string realCode = J(await capServer.Handle(Req("GET", "/api/list", host: "pc.example.ts.net", headers: Api("100.64.0.20")))).GetProperty("code").GetString()!;
        string spoofCode = J(await capServer.Handle(Req("GET", "/api/list", host: "pc.example.ts.net", headers: Api("100.64.0.21")))).GetProperty("code").GetString()!;
        r = await capServer.Handle(Req("GET", "/api/list", host: "pc.example.ts.net", headers: Api("100.64.0.22")));
        Check(r.Status == 403 && Err(r) == "pairing_busy" && capPrompts == 2, $"a third waiting request is refused as busy, with no prompt (max {PhonePairing.MaxPending})");
        if (realCode != spoofCode)
            Check(!cap.Approve("nSPOOF", realCode), "the look-alike cannot be approved with the real phone's code");
        Check(cap.IsPending("nSPOOF"), "...and stays pending");
        Check(cap.Approve("nREAL", realCode), "the real phone is approved with its own code");
        r = await capServer.Handle(Req("GET", "/api/list", host: "pc.example.ts.net", headers: Api("100.64.0.21")));
        Check(Err(r) == "pairing", "the look-alike is still not let in");
        cap.Deny("nSPOOF");
        r = await capServer.Handle(Req("GET", "/api/list", host: "pc.example.ts.net", headers: Api("100.64.0.22")));
        Check(Err(r) == "pairing" && capPrompts == 3, "once the queue has room, the next device can ask");

        // ---- pairing clock ----
        var now = new DateTime(2026, 1, 1, 12, 0, 0);
        var clock = new PhonePairing(now: () => now);
        var dev = new DeviceInfo("nX", "x", "ios", "owner@example.com", false);
        var (_, codeA) = clock.Check(dev);
        now += PhonePairing.PendingTtl + TimeSpan.FromSeconds(1);
        Check(!clock.IsPending("nX"), "a pending request expires");
        var (_, codeB) = clock.Check(dev);
        Check(clock.IsPending("nX") && codeB.Length == 4, $"a new request after expiry gets a fresh code ({codeA} then {codeB})");
        clock.Deny("nX");
        Check(clock.Check(dev).State == PairState.Denied, "denied within the cooldown");
        now += PhonePairing.DenyCooldown + TimeSpan.FromSeconds(1);
        Check(clock.Check(dev).State == PairState.Pending, "can ask again after the cooldown");

        // ---- string tables ----
        r = await server.Handle(Req("GET", "/i18n?langs=xx-YY,en-US"));
        var en = J(r);
        Check(en.GetProperty("lang").GetString() == "en" && en.GetProperty("strings").GetProperty("_dir").GetString() == "ltr",
              "an unknown language falls back to English");
        var enKeys = en.GetProperty("strings").EnumerateObject().Select(p => p.Name).ToHashSet();
        var tables = typeof(Program).Assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith("phone/strings.") && n.EndsWith(".json"))
            .Select(n => n["phone/strings.".Length..^".json".Length]).ToList();
        Check(tables.Count >= 2, $"string tables found: {string.Join(", ", tables)}");
        bool anyRtl = false;
        foreach (var lang in tables)
        {
            r = await server.Handle(Req("GET", $"/i18n?langs={lang}-XX"));
            var tbl = J(r);
            Check(tbl.GetProperty("lang").GetString() == lang, $"table '{lang}' is picked for its own language (with a region)");
            var keys = tbl.GetProperty("strings").EnumerateObject().Select(p => p.Name).ToHashSet();
            Check(keys.SetEquals(enKeys), $"table '{lang}' has exactly the English keys ({keys.Count})");
            anyRtl |= tbl.GetProperty("strings").GetProperty("_dir").GetString() == "rtl";
            r = await server.Handle(Req("GET", "/i18n", headers: new() { ["Accept-Language"] = $"{lang};q=0.9,en;q=0.5" }));
            Check(J(r).GetProperty("lang").GetString() == lang, $"table '{lang}' is picked from Accept-Language too");
        }
        Check(anyRtl, "a right-to-left table exists and says so");

        // ---- HTTP parser limits ----
        async Task<(HttpRequestData?, int)> Parse(string raw)
            => await LoopbackHttpServer.ReadRequest(new MemoryStream(Encoding.ASCII.GetBytes(raw)), IPAddress.Loopback, CancellationToken.None);
        var (p1, _) = await Parse("POST /api/x?a=1 HTTP/1.1\r\nHost: localhost\r\nContent-Length: 5\r\n\r\nhello");
        Check(p1 != null && p1.Path == "/api/x" && p1.Query == "a=1" && Encoding.ASCII.GetString(p1.Body) == "hello", "Content-Length body is read");
        var (p2, _) = await Parse("POST / HTTP/1.1\r\nHost: localhost\r\nTransfer-Encoding: chunked\r\n\r\n3\r\nabc\r\n2\r\nde\r\n0\r\n\r\n");
        Check(p2 != null && Encoding.ASCII.GetString(p2.Body) == "abcde", "a chunked body is decoded");
        var (p3, e3) = await Parse("POST / HTTP/1.1\r\nHost: localhost\r\nContent-Length: 1\r\nContent-Length: 1\r\n\r\nx");
        Check(p3 == null && e3 == 400, "a repeated Content-Length is refused");
        var (p4, e4) = await Parse("GET / HTTP/1.1\r\nHost: a\r\nHost: b\r\n\r\n");
        Check(p4 == null && e4 == 400, "a repeated Host is refused");
        var (p5, e5) = await Parse($"POST / HTTP/1.1\r\nHost: a\r\nContent-Length: {LoopbackHttpServer.MaxBody + 1}\r\n\r\n");
        Check(p5 == null && e5 == 413, "an oversized body is refused");
        var (p6, e6) = await Parse("GET / HTTP/1.1\r\nHost: a\r\nX-Big: " + new string('a', LoopbackHttpServer.MaxHeaderBytes) + "\r\n\r\n");
        Check(p6 == null && e6 == 413, "oversized headers are refused");
        var (p7, e7) = await Parse("GET http://elsewhere/ HTTP/1.1\r\nHost: a\r\n\r\n");
        Check(p7 == null && e7 == 400, "an absolute-form target is refused");
        var (p8, e8) = await Parse("POST / HTTP/1.1\r\nHost: a\r\nTransfer-Encoding: chunked\r\nContent-Length: 3\r\n\r\n0\r\n\r\n");
        Check(p8 == null && e8 == 400, "chunked plus Content-Length is refused");
        var (p9, _) = await Parse("POST / HTTP/1.1\r\nHost: a\r\nTransfer-Encoding:  Chunked \r\n\r\n2\r\nok\r\n0\r\n\r\n");
        Check(p9 != null && Encoding.ASCII.GetString(p9.Body) == "ok", "Transfer-Encoding \"Chunked\" (case, spaces) is accepted");
        foreach (var te in new[] { "gzip, chunked", "chunked, gzip", "xchunked", "gzip" })
        {
            var (pt, et) = await Parse($"POST / HTTP/1.1\r\nHost: a\r\nTransfer-Encoding: {te}\r\n\r\n0\r\n\r\n");
            Check(pt == null && et == 400, $"Transfer-Encoding \"{te}\" is refused");
        }
        foreach (var cl in new[] { "+5", "-5", "0x5", "5.0", "5 5", "1e1", "" })
        {
            var (pc, ec) = await Parse($"POST / HTTP/1.1\r\nHost: a\r\nContent-Length: {cl}\r\n\r\nhello");
            Check(pc == null && ec == 400, $"Content-Length \"{cl}\" is refused");
        }
        var (p10, _) = await Parse("POST / HTTP/1.1\r\nHost: a\r\nContent-Length: 005\r\n\r\nhello");
        Check(p10 != null && p10.Body.Length == 5, "Content-Length of digits only is accepted");

        // ---- connection cap: over it, 503 at once ----
        var held = new List<System.Net.Sockets.TcpClient>();
        var capped = new LoopbackHttpServer(7095, _ => Task.FromResult(new HttpResponseData { Body = "{}"u8.ToArray() }), null, maxConnections: 4);
        capped.Start();
        try
        {
            for (int i = 0; i < 4; i++) held.Add(new System.Net.Sockets.TcpClient("127.0.0.1", 7095));   // open, never send
            await Task.Delay(200);
            using (var extra = new System.Net.Sockets.TcpClient("127.0.0.1", 7095))
            {
                var buf = new byte[64];
                int n = await extra.GetStream().ReadAsync(buf);
                Check(Encoding.ASCII.GetString(buf, 0, n).StartsWith("HTTP/1.1 503"), "a connection over the cap gets 503 at once");
            }
            foreach (var c in held) c.Dispose();
            held.Clear();
            await Task.Delay(300);
            using var again = new System.Net.Sockets.TcpClient("127.0.0.1", 7095);
            var s = again.GetStream();
            await s.WriteAsync(Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\nHost: a\r\n\r\n"));
            var buf2 = new byte[64];
            int n2 = await s.ReadAsync(buf2);
            Check(Encoding.ASCII.GetString(buf2, 0, n2).StartsWith("HTTP/1.1 200"), "slots free again once those connections close");
        }
        finally
        {
            foreach (var c in held) c.Dispose();
            capped.Dispose();
        }

        // ---- Tailscale JSON parsing (shapes measured on tailscale 1.102) ----
        var who = TailscaleResolver.ParseWhois(JsonDocument.Parse(
            "{\"Node\":{\"StableID\":\"nAbC\",\"Name\":\"phone.tail.ts.net.\",\"ComputedName\":\"phone\",\"Hostinfo\":{\"Hostname\":\"Phone\",\"OS\":\"iOS\"}},\"UserProfile\":{\"LoginName\":\"owner@example.com\"}}").RootElement);
        Check(who is { StableId: "nAbC", Name: "phone", Os: "iOS", Login: "owner@example.com", Tagged: false, HostName: "Phone" }, "whois JSON is parsed");
        var tagged = TailscaleResolver.ParseWhois(JsonDocument.Parse(
            "{\"Node\":{\"StableID\":\"nT\",\"Tags\":[\"tag:server\"],\"Hostinfo\":{\"Hostname\":\"srv\"}},\"UserProfile\":{\"LoginName\":\"tagged-devices\"}}").RootElement);
        Check(tagged is { Tagged: true }, "a tagged node is recognised");
        var self = TailscaleResolver.ParseStatus(JsonDocument.Parse(
            "{\"Self\":{\"ID\":\"nSelf\",\"UserID\":42,\"DNSName\":\"pc.tail.ts.net.\"},\"User\":{\"42\":{\"LoginName\":\"owner@example.com\"}}}").RootElement);
        Check(self is { Login: "owner@example.com", StableId: "nSelf", DnsName: "pc.tail.ts.net" }, "status JSON is parsed");

        // ---- prompt text: blocks the harness puts in front of the user's words (InjectedPrefix) ----
        const string chrome = "<browser_instruction># Claude in Chrome browser automation\n\nYou have access to tools.</browser_instruction>";
        Check(InjectedPrefix.Strip(chrome + "\nFix the login page") == "Fix the login page",
              "a browser-extension block before a prompt is skipped");
        Check(InjectedPrefix.Strip("<system-reminder>be brief</system-reminder>\n" + chrome + "\n\nFix the login page") == "Fix the login page",
              "two stacked blocks before a prompt are both skipped");
        Check(InjectedPrefix.Strip("<pasted_content source=\"clipboard\" lines=\"3\">a\nb</pasted_content> what is this?") == "what is this?",
              "a block with attributes is skipped");
        Check(InjectedPrefix.Strip("Fix the login page") == "Fix the login page", "a plain prompt is untouched");
        Check(InjectedPrefix.Strip("Fix <browser_instruction>x</browser_instruction> later") == "Fix <browser_instruction>x</browser_instruction> later",
              "a block that is not leading is untouched");
        // Decided: an HTML-looking opener is the user's own text. It is never stripped - not
        // when unclosed, and not when closed either, because a harness tag always has _ or -.
        Check(InjectedPrefix.Strip("<div> has no padding, why?") == "<div> has no padding, why?",
              "a prompt opening with an unclosed <div> is untouched");
        Check(InjectedPrefix.Strip("<div>x</div> has no padding") == "<div>x</div> has no padding",
              "a prompt opening with a closed <div> is untouched (no _ or - in the name)");
        Check(InjectedPrefix.Strip("<3 this deck") == "<3 this deck", "a prompt that merely starts with < is untouched");
        Check(InjectedPrefix.Strip(chrome) == chrome, "a prompt that is only a block falls back to the original text");
        Check(InjectedPrefix.Strip("<browser_instruction># Claude in Chrome brow") == "<browser_instruction># Claude in Chrome brow",
              "a cut-off block is left as it is");
        Check(InjectedPrefix.StartsWithUnclosedBlock("<browser_instruction># Claude in Chrome brow"),
              "a cut-off block is recognised (a detail saved by an older hook)");
        Check(!InjectedPrefix.StartsWithUnclosedBlock(chrome + " Fix it") && !InjectedPrefix.StartsWithUnclosedBlock("<div> has no padding"),
              "a complete block, or a <div> prompt, is not a cut-off block");
        const string relay = "<cross-session-message from=\"pipe-1\">Continue item 7</cross-session-message>";
        Check(InjectedPrefix.Strip(relay) == relay && !InjectedPrefix.StartsWithUnclosedBlock("<cross-session-message from=\"p\">Contin"),
              "a cross-session envelope is never stripped: its content is the prompt");

        // ---- one wording for a permission dialog (PermissionWait) ----
        Check(PermissionWait.Text("Bash") == "Waiting for permission: Bash", "the scanner's line names the tool");
        Check(PermissionWait.Text(null) == "Waiting for permission" && PermissionWait.Text("  ") == "Waiting for permission",
              "no tool name leaves the bare line");
        Check(PermissionWait.Normalize("Claude needs your permission to use Bash") == PermissionWait.Text("Bash"),
              "Claude Code's Notification sentence becomes the same line the scanner writes");
        Check(PermissionWait.Normalize("Claude needs your permission to use mcp__github__create_issue.") == "Waiting for permission: mcp__github__create_issue",
              "a trailing full stop and an MCP tool name are handled");
        Check(PermissionWait.Normalize("Claude is waiting for your input") == "Claude is waiting for your input",
              "another notification is untouched");
        Check(PermissionWait.Normalize("Why does it say Claude needs your permission to use Bash?") == "Why does it say Claude needs your permission to use Bash?",
              "a prompt that merely quotes the sentence is untouched");
        Check(PermissionWait.Normalize(PermissionWait.Text("Edit")) == "Waiting for permission: Edit",
              "the deck's own line passes through unchanged");
        // ForCard: what the app does with every hook detail. `Bound20` stands in for the app's own
        // one-line clean-up and 300-character limit.
        static string Bound20(string s) => s.Length <= 20 ? s : s[..19] + "…";
        Check(PermissionWait.ForCard("Waiting for permission: Bash: npm test", Bound20) == "Waiting for permission: Bash: npm test",
              "the hook's line keeps the command after the tool");
        Check(PermissionWait.ForCard("Waiting for permission: Bash: npm test 2>&1 | tail -25", Bound20) == "Waiting for permission: Bash: npm test 2>&1…",
              "only the tool and its command are cut to the limit, as when they stood alone; the lead is not counted");
        Check(PermissionWait.ForCard("Waiting for permission: Write", Bound20) == "Waiting for permission: Write",
              "a tool with no argument gives the stable line");
        Check(PermissionWait.ForCard("Waiting for permission", s => s) == "Waiting for permission",
              "the bare line passes through");
        Check(PermissionWait.ForCard("Claude needs your permission to use Bash", s => s) == "Waiting for permission: Bash",
              "Claude Code's sentence still becomes the stable line");
        Check(PermissionWait.ForCard("Fix the login page and the signup page", Bound20) == "Fix the login page …",
              "any other detail is only cut to the limit");

        // ---- what `session end --close-tab` answers (SessionEndReply) ----
        // The three words the live suite (tests/close-tab.tests.ps1) matches on must survive:
        // "ended", "closing its VSCode tab", "left open".
        const string endId = "session-under-test";
        string asked = TabTower.Cli.SessionEndReply.WithCloseTab(endId, $"session {endId} ended", endedBefore: false, tabAsked: true, whyNot: "");
        Check(asked == $"session {endId} ended; closing its VSCode tab", "a live session: ended, and its tab asked for");
        string refused = TabTower.Cli.SessionEndReply.WithCloseTab(endId, $"session {endId} ended", endedBefore: false, tabAsked: false,
                                                                 whyNot: "no VSCode connector for this workspace");
        Check(refused == $"session {endId} ended; its VSCode tab was left open: no VSCode connector for this workspace",
              "a live session whose tab could not be asked for: ended, tab left open, and why");
        // The refusal that `session close-tab` gives for an ended session, as CloseSessionTab words it.
        const string closeTabRefusal = "this session has already ended: revealing it would resume it off its transcript, so its tab " +
                                       "is left for you to close. Pass --close-tab to `session end` instead, which asks while the session is still alive";
        string late = TabTower.Cli.SessionEndReply.WithCloseTab(endId, $"session {endId} ended", endedBefore: true, tabAsked: false, whyNot: closeTabRefusal);
        Check(late.Contains("already ended") && late.Contains(endId), "a session that had already ended: the answer says so, and names it");
        Check(!late.Contains("Pass --close-tab") && !late.Contains("session end"),
              "it does not advise running the command that was just run");
        Check(!late.Contains("left open") && late.Contains("If that tab is still open"),
              "it does not claim the tab is open: the deck did not ask, and says what to do if it is");
        Check(late.Contains("VSCode tab was not asked for") && late.Contains("resume it"),
              "it says the tab was not asked for, and why");

        Console.WriteLine($"\n{_passed} passed, {_failed} failed");
        return _failed == 0 ? 0 : 1;
    }
}
