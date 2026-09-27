using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ScrapyNet.Sandbox;

/// <summary>An HTTP request as seen by the sandbox.</summary>
public sealed class SandboxRequest
{
    public required string Method { get; init; }

    /// <summary>Path without the query string.</summary>
    public required string Path { get; init; }

    public required string Query { get; init; }

    /// <summary>True when the request arrived in proxy form (<c>GET http://host/path</c>).</summary>
    public bool ViaProxy { get; init; }

    public required Dictionary<string, string> Headers { get; init; }

    public required byte[] Body { get; init; }

    public DateTimeOffset Timestamp { get; } = DateTimeOffset.UtcNow;

    public string? Header(string name) => Headers.TryGetValue(name, out var v) ? v : null;

    public string? QueryValue(string name)
    {
        foreach (var part in Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2);
            if (Uri.UnescapeDataString(kv[0]) == name) return kv.Length > 1 ? Uri.UnescapeDataString(kv[1].Replace('+', ' ')) : "";
        }
        return null;
    }

    public Dictionary<string, string> Form()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in Encoding.UTF8.GetString(Body).Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2);
            result[Uri.UnescapeDataString(kv[0].Replace('+', ' '))] = kv.Length > 1 ? Uri.UnescapeDataString(kv[1].Replace('+', ' ')) : "";
        }
        return result;
    }

    public Dictionary<string, string> Cookies()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in (Header("Cookie") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var kv = part.Split('=', 2);
            result[kv[0]] = kv.Length > 1 ? kv[1] : "";
        }
        return result;
    }

    public override string ToString() => $"{Method} {Path}{Query}";
}

/// <summary>A response produced by a sandbox route.</summary>
public sealed class SandboxResponse
{
    public int Status { get; init; } = 200;

    public string ContentType { get; init; } = "text/html; charset=utf-8";

    public byte[] Body { get; init; } = [];

    public List<KeyValuePair<string, string>> Headers { get; } = [];

    /// <summary>Wait this long before answering (latency simulation).</summary>
    public int DelayMs { get; init; }

    /// <summary>Close the socket without answering (network failure simulation).</summary>
    public bool Abort { get; init; }

    public static SandboxResponse Html(string html, int status = 200) => new() { Status = status, Body = Encoding.UTF8.GetBytes(html) };

    public static SandboxResponse Text(string text, string contentType = "text/plain; charset=utf-8", int status = 200) =>
        new() { Status = status, ContentType = contentType, Body = Encoding.UTF8.GetBytes(text) };

    public static SandboxResponse Json(string json, int status = 200) => Text(json, "application/json; charset=utf-8", status);

    public static SandboxResponse Bytes(byte[] body, string contentType) => new() { ContentType = contentType, Body = body };

    public static SandboxResponse Redirect(string location, int status = 302)
    {
        var r = new SandboxResponse { Status = status, Body = Encoding.UTF8.GetBytes($"<a href=\"{location}\">moved</a>") };
        r.Headers.Add(new("Location", location));
        return r;
    }

    public static SandboxResponse NotFound() => Html("<html><body><h1>404 Not Found</h1></body></html>", 404);

    public SandboxResponse WithHeader(string name, string value)
    {
        Headers.Add(new(name, value));
        return this;
    }
}

/// <summary>
/// A tiny, dependency-free HTTP/1.1 server on a raw socket. Small enough to embed anywhere (tests,
/// notebooks, the gallery) and low-level enough to misbehave on purpose: drop connections, stall,
/// answer as a forward proxy.
/// </summary>
public sealed class SandboxServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly Func<SandboxRequest, Task<SandboxResponse>> _handler;
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentDictionary<Task, byte> _connections = new();
    private Task? _acceptLoop;

    public SandboxServer(Func<SandboxRequest, Task<SandboxResponse>> handler, int port = 0)
    {
        _handler = handler;
        _listener = new TcpListener(IPAddress.Loopback, port);
    }

    public int Port { get; private set; }

    public string BaseUrl => $"http://127.0.0.1:{Port}";

    /// <summary>Requests received so far (most recent last).</summary>
    public ConcurrentQueue<SandboxRequest> RequestLog { get; } = new();

    public long RequestCount => RequestLog.Count;

    public void Start()
    {
        _listener.Start(512);
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _acceptLoop = AcceptLoopAsync();
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_cts.Token).ConfigureAwait(false);
            }
            catch (Exception) when (_cts.IsCancellationRequested)
            {
                return;
            }
            catch (SocketException)
            {
                continue;
            }
            client.NoDelay = true;
            var task = HandleConnectionAsync(client);
            _connections.TryAdd(task, 0);
            _ = task.ContinueWith(t => _connections.TryRemove(t, out _), TaskScheduler.Default);
        }
    }

    private async Task HandleConnectionAsync(TcpClient client)
    {
        using (client)
        {
            var stream = client.GetStream();
            var reader = new BufferedReader(stream);
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    var request = await ReadRequestAsync(reader, _cts.Token).ConfigureAwait(false);
                    if (request is null) return;
                    RequestLog.Enqueue(request);
                    while (RequestLog.Count > 10000) RequestLog.TryDequeue(out _);

                    SandboxResponse response;
                    try
                    {
                        response = await _handler(request).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        response = SandboxResponse.Text("Internal error: " + ex.Message, status: 500);
                    }

                    if (response.DelayMs > 0) await Task.Delay(response.DelayMs, _cts.Token).ConfigureAwait(false);
                    if (response.Abort)
                    {
                        client.Client.LingerState = new LingerOption(true, 0);
                        return;
                    }

                    var keepAlive = !string.Equals(request.Header("Connection"), "close", StringComparison.OrdinalIgnoreCase);
                    await WriteResponseAsync(stream, request, response, keepAlive, _cts.Token).ConfigureAwait(false);
                    if (!keepAlive) return;
                }
            }
            catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
            {
            }
        }
    }

    private static async Task<SandboxRequest?> ReadRequestAsync(BufferedReader reader, CancellationToken ct)
    {
        var requestLine = await reader.ReadLineAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(requestLine)) return null;
        var parts = requestLine.Split(' ');
        if (parts.Length < 3) return null;
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        while (true)
        {
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (line is null) return null;
            if (line.Length == 0) break;
            var colon = line.IndexOf(':');
            if (colon <= 0) continue;
            var name = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();
            headers[name] = headers.TryGetValue(name, out var existing) ? existing + ", " + value : value;
        }

        var body = Array.Empty<byte>();
        if (headers.TryGetValue("Content-Length", out var cl) && int.TryParse(cl, out var length) && length > 0)
            body = await reader.ReadBytesAsync(length, ct).ConfigureAwait(false);

        var target = parts[1];
        var viaProxy = false;
        if (target.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            viaProxy = true;
            var uri = new Uri(target);
            target = uri.PathAndQuery;
        }
        var q = target.IndexOf('?');
        return new SandboxRequest
        {
            Method = parts[0].ToUpperInvariant(),
            Path = Uri.UnescapeDataString(q >= 0 ? target[..q] : target),
            Query = q >= 0 ? target[q..] : "",
            ViaProxy = viaProxy,
            Headers = headers,
            Body = body,
        };
    }

    private static async Task WriteResponseAsync(NetworkStream stream, SandboxRequest request, SandboxResponse response, bool keepAlive, CancellationToken ct)
    {
        var body = response.Body;
        var gzip = body.Length > 1024 && response.Status != 304 &&
                   (request.Header("Accept-Encoding") ?? "").Contains("gzip", StringComparison.OrdinalIgnoreCase) &&
                   (response.ContentType.StartsWith("text/", StringComparison.Ordinal) || response.ContentType.Contains("json") || response.ContentType.Contains("xml"));
        if (gzip)
        {
            using var ms = new MemoryStream();
            using (var gz = new GZipStream(ms, CompressionLevel.Fastest, leaveOpen: true)) gz.Write(body);
            body = ms.ToArray();
        }
        if (request.Method == "HEAD" || response.Status == 304) body = [];

        var sb = new StringBuilder();
        sb.Append("HTTP/1.1 ").Append(response.Status.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(Reason(response.Status)).Append("\r\n");
        sb.Append("Server: ScrapyNet-Sandbox\r\n");
        sb.Append("Date: ").Append(DateTimeOffset.UtcNow.ToString("R", CultureInfo.InvariantCulture)).Append("\r\n");
        if (response.Status != 304) sb.Append("Content-Type: ").Append(response.ContentType).Append("\r\n");
        if (gzip) sb.Append("Content-Encoding: gzip\r\n");
        sb.Append("Content-Length: ").Append(body.Length.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
        sb.Append(keepAlive ? "Connection: keep-alive\r\n" : "Connection: close\r\n");
        foreach (var (k, v) in response.Headers) sb.Append(k).Append(": ").Append(v).Append("\r\n");
        sb.Append("\r\n");
        await stream.WriteAsync(Encoding.ASCII.GetBytes(sb.ToString()), ct).ConfigureAwait(false);
        if (body.Length > 0) await stream.WriteAsync(body, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    private static string Reason(int status) => status switch
    {
        200 => "OK",
        301 => "Moved Permanently",
        302 => "Found",
        303 => "See Other",
        304 => "Not Modified",
        307 => "Temporary Redirect",
        308 => "Permanent Redirect",
        400 => "Bad Request",
        401 => "Unauthorized",
        403 => "Forbidden",
        404 => "Not Found",
        408 => "Request Timeout",
        429 => "Too Many Requests",
        500 => "Internal Server Error",
        502 => "Bad Gateway",
        503 => "Service Unavailable",
        504 => "Gateway Timeout",
        _ => "Status",
    };

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        _listener.Stop();
        if (_acceptLoop is not null)
        {
            try
            {
                await _acceptLoop.ConfigureAwait(false);
            }
            catch (Exception)
            {
            }
        }
        try
        {
            await Task.WhenAll(_connections.Keys.ToArray()).WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
        _cts.Dispose();
    }

    /// <summary>Line and byte reads over a network stream with one reusable buffer.</summary>
    private sealed class BufferedReader(Stream stream)
    {
        private readonly byte[] _buffer = new byte[16384];
        private int _start;
        private int _end;

        private async ValueTask<bool> FillAsync(CancellationToken ct)
        {
            if (_start > 0)
            {
                Buffer.BlockCopy(_buffer, _start, _buffer, 0, _end - _start);
                _end -= _start;
                _start = 0;
            }
            if (_end == _buffer.Length) throw new IOException("Request header too large");
            var n = await stream.ReadAsync(_buffer.AsMemory(_end), ct).ConfigureAwait(false);
            if (n == 0) return false;
            _end += n;
            return true;
        }

        public async ValueTask<string?> ReadLineAsync(CancellationToken ct)
        {
            while (true)
            {
                var idx = Array.IndexOf(_buffer, (byte)'\n', _start, _end - _start);
                if (idx >= 0)
                {
                    var len = idx - _start;
                    if (len > 0 && _buffer[idx - 1] == '\r') len--;
                    var line = Encoding.Latin1.GetString(_buffer, _start, len);
                    _start = idx + 1;
                    return line;
                }
                if (!await FillAsync(ct).ConfigureAwait(false)) return null;
            }
        }

        public async ValueTask<byte[]> ReadBytesAsync(int count, CancellationToken ct)
        {
            var result = new byte[count];
            var copied = 0;
            while (copied < count)
            {
                if (_start == _end && !await FillAsync(ct).ConfigureAwait(false)) break;
                var n = Math.Min(count - copied, _end - _start);
                Buffer.BlockCopy(_buffer, _start, result, copied, n);
                _start += n;
                copied += n;
            }
            return result;
        }
    }
}
