using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace TabTower.Services.Phone;

/// <summary>One parsed HTTP request. Header names are case-insensitive.</summary>
public sealed class HttpRequestData
{
    public string Method { get; init; } = "";
    public string Path { get; init; } = "";
    public string Query { get; init; } = "";
    public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);
    public byte[] Body { get; init; } = Array.Empty<byte>();
    public IPAddress? Remote { get; init; }

    public string Header(string name) => Headers.TryGetValue(name, out var v) ? v : "";

    /// <summary>One query-string value, URL-decoded, or "" when absent.</summary>
    public string QueryValue(string name)
    {
        foreach (var part in Query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = part.IndexOf('=');
            string key = Uri.UnescapeDataString(eq < 0 ? part : part[..eq]);
            if (key == name) return eq < 0 ? "" : Uri.UnescapeDataString(part[(eq + 1)..].Replace('+', ' '));
        }
        return "";
    }
}

public sealed class HttpResponseData
{
    public int Status { get; init; } = 200;
    public string ContentType { get; init; } = "application/json; charset=utf-8";
    public byte[] Body { get; init; } = Array.Empty<byte>();
}

/// <summary>
/// A deliberately small HTTP/1.1 server on 127.0.0.1 only, one request per connection.
///
/// Why not HttpListener: http.sys matches the request's Host header against the registered
/// prefix, and a non-admin user can only register "localhost" / "127.0.0.1". `tailscale serve`
/// forwards the original Host (the machine's *.ts.net name), which http.sys then answers with
/// "400 Invalid Hostname" before any of our code runs. Registering the ts.net name or a
/// wildcard needs an admin URL reservation and would listen on every interface. A plain socket
/// bound to the loopback address has neither problem, and the Host check moves into our own
/// code, where it belongs anyway.
///
/// Scope is kept to what the phone page needs: GET and POST, Content-Length or chunked
/// bodies up to <see cref="MaxBody"/>, no keep-alive, no TLS (the proxy terminates it).
/// </summary>
public sealed class LoopbackHttpServer : IDisposable
{
    public const int MaxHeaderBytes = 16 * 1024;
    public const int MaxBody = 64 * 1024;
    public const int DefaultMaxConnections = 32;
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(15);

    private readonly int _port;
    private readonly Func<HttpRequestData, Task<HttpResponseData>> _handler;
    private readonly Action<string>? _log;
    private readonly SemaphoreSlim _slots;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;

    public LoopbackHttpServer(int port, Func<HttpRequestData, Task<HttpResponseData>> handler,
                              Action<string>? log = null, int maxConnections = DefaultMaxConnections)
    {
        _port = port;
        _handler = handler;
        _log = log;
        _slots = new SemaphoreSlim(maxConnections, maxConnections);
    }

    public int Port => _port;

    /// <summary>Binds 127.0.0.1:port. Throws SocketException when the port is taken.</summary>
    public void Start()
    {
        _cts = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Loopback, _port);
        // Exclusive: another process must not be able to bind the same port beside us and
        // receive part of the traffic.
        _listener.ExclusiveAddressUse = true;
        _listener.Start();
        _ = Task.Run(() => AcceptLoop(_listener, _cts.Token));
    }

    private async Task AcceptLoop(TcpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(ct); }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException) { continue; }
            // A cap on connections in flight: a local process opening sockets and never finishing
            // its request would otherwise hold a task and a buffer each until it stops.
            if (!_slots.Wait(0))
            {
                _ = Task.Run(() => Reject(client));
                continue;
            }
            _ = Task.Run(async () =>
            {
                try { await Serve(client, ct); }
                finally { _slots.Release(); }
            });
        }
    }

    private async Task Serve(TcpClient client, CancellationToken ct)
    {
        using (client)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(ReadTimeout);
                var stream = client.GetStream();
                var remote = (client.Client.RemoteEndPoint as IPEndPoint)?.Address;
                HttpResponseData response;
                var (request, error) = await ReadRequest(stream, remote, timeout.Token);
                if (request == null)
                    response = Text(error == 413 ? 413 : 400, error == 413 ? "too large" : "bad request");
                else
                {
                    try { response = await _handler(request); }
                    catch (Exception ex)
                    {
                        _log?.Invoke($"handler failed: {ex.Message}");
                        response = Text(500, "server error");
                    }
                }
                await Write(stream, response, ct);
                await LingeringClose(client, stream);
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or SocketException)
            {
                // A client that went away or never finished its request: nothing to answer.
            }
        }
    }

    /// <summary>Over the connection cap: answer 503 without reading the request, and close.</summary>
    private static async Task Reject(TcpClient client)
    {
        using (client)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await Write(client.GetStream(), Text(503, "busy"), cts.Token);
            }
            catch { /* the client is gone: nothing to answer */ }
        }
    }

    /// <summary>Closing a socket that still has unread request bytes makes Windows send a reset,
    /// and the client can lose the response it was just sent (a refused oversized body is the
    /// usual case). So: stop sending, then read and discard what is left for a moment.</summary>
    private static async Task LingeringClose(TcpClient client, Stream stream)
    {
        try
        {
            client.Client.Shutdown(SocketShutdown.Send);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            var sink = new byte[8192];
            int total = 0;
            while (total < 1024 * 1024)
            {
                int n = await stream.ReadAsync(sink, cts.Token);
                if (n == 0) break;
                total += n;
            }
        }
        catch
        {
            // Timed out or already closed by the client: either way we are done.
        }
    }

    private static HttpResponseData Text(int status, string text) => new()
    {
        Status = status,
        ContentType = "text/plain; charset=utf-8",
        Body = Encoding.UTF8.GetBytes(text),
    };

    /// <summary>Reads and parses one request. Returns (null, 400|413) on anything malformed.</summary>
    internal static async Task<(HttpRequestData?, int)> ReadRequest(Stream stream, IPAddress? remote,
                                                                    CancellationToken ct)
    {
        var buffer = new byte[MaxHeaderBytes + 4 + MaxBody];
        int filled = 0, headerEnd = -1;
        while (headerEnd < 0)
        {
            if (filled >= MaxHeaderBytes) return (null, 413);
            int n = await stream.ReadAsync(buffer.AsMemory(filled, MaxHeaderBytes - filled), ct);
            if (n == 0) return (null, 400);
            filled += n;
            headerEnd = IndexOf(buffer, filled, "\r\n\r\n"u8);
        }

        string head = Encoding.ASCII.GetString(buffer, 0, headerEnd);
        var lines = head.Split("\r\n");
        var requestLine = lines[0].Split(' ');
        if (requestLine.Length != 3 || !requestLine[2].StartsWith("HTTP/1.", StringComparison.Ordinal))
            return (null, 400);
        string method = requestLine[0];
        string target = requestLine[1];
        if (!target.StartsWith('/')) return (null, 400);

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i];
            if (line.Length == 0 || line[0] is ' ' or '\t') return (null, 400);   // no obsolete folding
            int colon = line.IndexOf(':');
            if (colon <= 0) return (null, 400);
            string name = line[..colon].Trim();
            string value = line[(colon + 1)..].Trim();
            if (headers.TryGetValue(name, out var existing))
            {
                // A repeated Host or Content-Length is how request smuggling starts; refuse it.
                if (name.Equals("Host", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                    return (null, 400);
                headers[name] = existing + ", " + value;
            }
            else headers[name] = value;
        }

        int bodyStart = headerEnd + 4;
        byte[] body;
        // Only the one transfer coding a browser or proxy sends. "gzip, chunked" or anything
        // else is refused rather than half-understood.
        bool chunked = false;
        if (headers.TryGetValue("Transfer-Encoding", out var te))
        {
            if (!te.Trim().Equals("chunked", StringComparison.OrdinalIgnoreCase)) return (null, 400);
            chunked = true;
        }
        if (chunked && headers.ContainsKey("Content-Length")) return (null, 400);
        if (chunked)
        {
            var raw = new MemoryStream();
            raw.Write(buffer, bodyStart, filled - bodyStart);
            var decoded = await ReadChunked(stream, raw, ct);
            if (decoded == null) return (null, 400);
            if (decoded.Length > MaxBody) return (null, 413);
            body = decoded;
        }
        else if (headers.TryGetValue("Content-Length", out var clText))
        {
            // Digits only: int.TryParse would also take "+5", " 5" and other forms that
            // another parser in the chain might read differently.
            if (clText.Length is 0 or > 9 || !clText.All(char.IsAsciiDigit)) return (null, 400);
            int length = int.Parse(clText);
            if (length > MaxBody) return (null, 413);
            while (filled - bodyStart < length)
            {
                int n = await stream.ReadAsync(buffer.AsMemory(filled, bodyStart + length - filled), ct);
                if (n == 0) return (null, 400);
                filled += n;
            }
            body = buffer.AsSpan(bodyStart, length).ToArray();
        }
        else body = Array.Empty<byte>();

        int q = target.IndexOf('?');
        return (new HttpRequestData
        {
            Method = method,
            Path = q < 0 ? target : target[..q],
            Query = q < 0 ? "" : target[(q + 1)..],
            Body = body,
            Remote = remote,
        }.WithHeaders(headers), 0);
    }

    /// <summary>Decodes a chunked body. <paramref name="raw"/> holds whatever arrived with the
    /// headers; more is read from the stream as needed. Null on a malformed or oversized body.</summary>
    private static async Task<byte[]?> ReadChunked(Stream stream, MemoryStream raw, CancellationToken ct)
    {
        var output = new MemoryStream();
        int pos = 0;
        var chunk = new byte[4096];
        async Task<bool> More()
        {
            if (raw.Length > MaxBody * 2) return false;
            int n = await stream.ReadAsync(chunk, ct);
            if (n == 0) return false;
            raw.Seek(0, SeekOrigin.End);
            raw.Write(chunk, 0, n);
            return true;
        }
        while (true)
        {
            int lineEnd;
            while ((lineEnd = IndexOf(raw.GetBuffer(), (int)raw.Length, "\r\n"u8, pos)) < 0)
                if (!await More()) return null;
            string sizeText = Encoding.ASCII.GetString(raw.GetBuffer(), pos, lineEnd - pos).Split(';')[0].Trim();
            if (!int.TryParse(sizeText, System.Globalization.NumberStyles.HexNumber, null, out int size) || size < 0)
                return null;
            pos = lineEnd + 2;
            if (size == 0) return output.ToArray();
            if (output.Length + size > MaxBody) return null;
            while (raw.Length < pos + size + 2)
                if (!await More()) return null;
            output.Write(raw.GetBuffer(), pos, size);
            pos += size + 2;
        }
    }

    private static int IndexOf(byte[] data, int length, ReadOnlySpan<byte> needle, int start = 0)
    {
        int at = data.AsSpan(start, Math.Max(0, length - start)).IndexOf(needle);
        return at < 0 ? -1 : at + start;
    }

    private static async Task Write(Stream stream, HttpResponseData r, CancellationToken ct)
    {
        string reason = r.Status switch
        {
            200 => "OK", 400 => "Bad Request", 403 => "Forbidden", 404 => "Not Found",
            405 => "Method Not Allowed", 409 => "Conflict", 413 => "Payload Too Large",
            500 => "Internal Server Error", 502 => "Bad Gateway", 503 => "Service Unavailable",
            504 => "Gateway Timeout",
            _ => "Status",
        };
        var head = new StringBuilder()
            .Append($"HTTP/1.1 {r.Status} {reason}\r\n")
            .Append($"Content-Type: {r.ContentType}\r\n")
            .Append($"Content-Length: {r.Body.Length}\r\n")
            .Append("Cache-Control: no-store\r\n")
            .Append("X-Content-Type-Options: nosniff\r\n")
            .Append("X-Frame-Options: DENY\r\n")
            .Append("Referrer-Policy: no-referrer\r\n")
            .Append("Content-Security-Policy: default-src 'self'; script-src 'self' 'unsafe-inline'; " +
                    "style-src 'self' 'unsafe-inline'; connect-src 'self'; frame-ancestors 'none'\r\n")
            .Append("Connection: close\r\n\r\n");
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head.ToString()), ct);
        if (r.Body.Length > 0) await stream.WriteAsync(r.Body, ct);
        await stream.FlushAsync(ct);
    }

    public void Dispose()
    {
        try { _cts?.Cancel(); } catch { }
        try { _listener?.Stop(); } catch { }
        _cts?.Dispose();
    }
}

internal static class HttpRequestDataExtensions
{
    public static HttpRequestData WithHeaders(this HttpRequestData r, Dictionary<string, string> headers)
    {
        foreach (var (k, v) in headers) r.Headers[k] = v;
        return r;
    }
}
