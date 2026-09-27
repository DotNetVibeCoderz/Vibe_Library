using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Text;
using Microsoft.Extensions.Logging;

namespace ScrapyNet.Handlers;

/// <summary>Downloads requests for one or more URL schemes (Scrapy's <c>DOWNLOAD_HANDLERS</c>).</summary>
public interface IDownloadHandler
{
    Task<Response> DownloadAsync(Request request, Spider spider, CancellationToken cancellationToken);
}

/// <summary>
/// HTTP(S) download handler on <see cref="SocketsHttpHandler"/>: pooled keep-alive connections,
/// gzip/deflate/brotli decompression, per-request timeouts, size limits, and proxies from
/// <c>request.Meta["proxy"]</c>.
/// </summary>
/// <remarks>
/// Redirects and cookies are deliberately left to the redirect and cookies middlewares (the socket
/// handler has both switched off) so that they go through the scheduler, the dupe filter and the stats
/// exactly as in Scrapy. One pooled client is kept per distinct proxy, since a
/// <see cref="SocketsHttpHandler"/> has a single proxy.
/// </remarks>
public sealed class HttpDownloadHandler : IDownloadHandler, IDisposable
{
    private readonly ConcurrentDictionary<string, HttpMessageInvoker> _clients = new(StringComparer.Ordinal);
    private readonly Settings _settings;
    private readonly ILogger _logger;
    private readonly TimeSpan _defaultTimeout;
    private readonly long _maxSize;
    private readonly long _warnSize;
    private readonly bool _verifySsl;
    private readonly bool _http2;
    private readonly int _perServer;

    public HttpDownloadHandler(Crawler crawler)
    {
        _settings = crawler.Settings;
        _logger = crawler.LoggerFactory.CreateLogger("scrapynet.core.downloader.handlers.http");
        _defaultTimeout = TimeSpan.FromSeconds(_settings.GetDouble(SettingKeys.DownloadTimeout, 180));
        _maxSize = _settings.GetLong(SettingKeys.DownloadMaxSize);
        _warnSize = _settings.GetLong(SettingKeys.DownloadWarnSize);
        _verifySsl = _settings.GetBool(SettingKeys.DownloadVerifySsl, true);
        _http2 = _settings.GetBool(SettingKeys.DownloadHttp2);
        _perServer = Math.Max(_settings.GetInt(SettingKeys.ConcurrentRequestsPerDomain, 8), _settings.GetInt(SettingKeys.ConcurrentRequestsPerIp));
    }

    public async Task<Response> DownloadAsync(Request request, Spider spider, CancellationToken cancellationToken)
    {
        var proxy = request.GetMeta<string>(MetaKeys.Proxy);
        var client = GetClient(proxy);
        var timeout = request.Meta.TryGetValue(MetaKeys.DownloadTimeout, out var t) && t is not null
            ? TimeSpan.FromSeconds(Convert.ToDouble(t, System.Globalization.CultureInfo.InvariantCulture))
            : _defaultTimeout;
        var maxSize = request.Meta.TryGetValue(MetaKeys.DownloadMaxSize, out var ms) && ms is not null ? Convert.ToInt64(ms) : _maxSize;
        var warnSize = request.Meta.TryGetValue(MetaKeys.DownloadWarnSize, out var ws) && ws is not null ? Convert.ToInt64(ws) : _warnSize;

        using var message = BuildMessage(request, proxy);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);
        var started = Stopwatch.GetTimestamp();
        HttpResponseMessage httpResponse;
        try
        {
            httpResponse = await client.SendAsync(message, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DownloadTimeoutException($"Getting {request.Url} took longer than {timeout.TotalSeconds} seconds.", ex);
        }

        using (httpResponse)
        {
            request.Meta[MetaKeys.DownloadLatency] = Stopwatch.GetElapsedTime(started).TotalSeconds;
            var headers = new Headers();
            foreach (var h in httpResponse.Headers) headers.Set(h.Key, h.Value);
            foreach (var h in httpResponse.Content.Headers)
            {
                // Content-Encoding is consumed by automatic decompression; keeping it would make
                // downstream code try to decompress the body a second time.
                if (h.Key.Equals("Content-Encoding", StringComparison.OrdinalIgnoreCase)) continue;
                headers.Set(h.Key, h.Value);
            }

            var declared = httpResponse.Content.Headers.ContentLength;
            if (maxSize > 0 && declared > maxSize)
                throw new DownloadSizeExceededException($"Cancelling download of {request.Url}: expected response size ({declared}) larger than download max size ({maxSize}).");
            if (warnSize > 0 && declared > warnSize)
                _logger.LogWarning("Expected response size ({Size}) larger than download warn size ({Warn}) in request {Request}.", declared, warnSize, request);

            byte[] body;
            try
            {
                body = await ReadBodyAsync(httpResponse.Content, declared, maxSize, request, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                throw new DownloadTimeoutException($"Getting {request.Url} took longer than {timeout.TotalSeconds} seconds.", ex);
            }
            if (warnSize > 0 && body.Length > warnSize && declared is null)
                _logger.LogWarning("Received ({Size}) bytes larger than download warn size ({Warn}) in request {Request}.", body.Length, warnSize, request);

            var protocol = $"HTTP/{httpResponse.Version.Major}.{httpResponse.Version.Minor}";
            var finalUrl = request.Url;
            return ResponseTypes.Create(finalUrl, (int)httpResponse.StatusCode, headers, body, request, protocol: protocol);
        }
    }

    private static async Task<byte[]> ReadBodyAsync(HttpContent content, long? declared, long maxSize, Request request, CancellationToken ct)
    {
        await using var stream = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        if (declared is > 0 and < int.MaxValue)
        {
            // Known length: read straight into an exact-size array, no intermediate copies.
            var exact = new byte[declared.Value];
            var offset = 0;
            while (offset < exact.Length)
            {
                var n = await stream.ReadAsync(exact.AsMemory(offset), ct).ConfigureAwait(false);
                if (n == 0) break;
                offset += n;
            }
            if (offset == exact.Length)
            {
                // Decompressed bodies can be longer than Content-Length; drain whatever follows.
                var extra = await ReadRemainderAsync(stream, maxSize - offset, request, ct).ConfigureAwait(false);
                return extra.Length == 0 ? exact : [.. exact, .. extra];
            }
            return exact[..offset];
        }
        return await ReadRemainderAsync(stream, maxSize, request, ct).ConfigureAwait(false);
    }

    private static async Task<byte[]> ReadRemainderAsync(Stream stream, long maxSize, Request request, CancellationToken ct)
    {
        // Unknown length: grow a pooled buffer, then copy once into an exact-size array.
        var buffer = ArrayPool<byte>.Shared.Rent(16 * 1024);
        var length = 0;
        try
        {
            while (true)
            {
                if (length == buffer.Length)
                {
                    var bigger = ArrayPool<byte>.Shared.Rent(buffer.Length * 2);
                    Buffer.BlockCopy(buffer, 0, bigger, 0, length);
                    ArrayPool<byte>.Shared.Return(buffer);
                    buffer = bigger;
                }
                var n = await stream.ReadAsync(buffer.AsMemory(length), ct).ConfigureAwait(false);
                if (n == 0) break;
                length += n;
                if (maxSize > 0 && length > maxSize)
                    throw new DownloadSizeExceededException($"Cancelling download of {request.Url}: received response size ({length}) larger than download max size ({maxSize}).");
            }
            return length == 0 ? [] : buffer.AsSpan(0, length).ToArray();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private HttpRequestMessage BuildMessage(Request request, string? proxy)
    {
        var message = new HttpRequestMessage(new HttpMethod(request.Method), request.Uri);
        if (_http2)
        {
            message.Version = HttpVersion.Version20;
            message.VersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
        }
        if (request.Body.Length > 0 || request.Method is "POST" or "PUT" or "PATCH")
            message.Content = new ByteArrayContent(request.Body);

        foreach (var (name, values) in request.Headers)
        {
            if (name.Equals("Proxy-Authorization", StringComparison.OrdinalIgnoreCase) && proxy is null) continue;
            if (!message.Headers.TryAddWithoutValidation(name, values))
            {
                message.Content ??= new ByteArrayContent([]);
                message.Content.Headers.Remove(name);
                message.Content.Headers.TryAddWithoutValidation(name, values);
            }
        }
        return message;
    }

    private HttpMessageInvoker GetClient(string? proxy) =>
        _clients.GetOrAdd(proxy ?? "", key =>
        {
            var handler = new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false,
                AutomaticDecompression = DecompressionMethods.All,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                PooledConnectionIdleTimeout = TimeSpan.FromSeconds(90),
                MaxConnectionsPerServer = Math.Max(2, _perServer),
                ConnectTimeout = _defaultTimeout,
                UseProxy = key.Length > 0,
            };
            if (key.Length > 0) handler.Proxy = CreateProxy(key);
            if (!_verifySsl)
                handler.SslOptions = new SslClientAuthenticationOptions { RemoteCertificateValidationCallback = static (_, _, _, _) => true };
            return new HttpMessageInvoker(handler, disposeHandler: true);
        });

    private static ExplicitProxy CreateProxy(string proxyUrl)
    {
        var uri = new Uri(proxyUrl);
        var proxy = new ExplicitProxy(new Uri($"{uri.Scheme}://{uri.Host}:{uri.Port}"));
        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            var parts = uri.UserInfo.Split(':', 2);
            proxy.Credentials = new NetworkCredential(Uri.UnescapeDataString(parts[0]), parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : "");
        }
        return proxy;
    }

    public void Dispose()
    {
        foreach (var client in _clients.Values) client.Dispose();
        _clients.Clear();
    }

    /// <summary>
    /// A proxy that is never bypassed. <see cref="WebProxy"/> always bypasses loopback hosts, which would
    /// silently skip the proxy for local targets; a spider that sets a proxy means it.
    /// </summary>
    private sealed class ExplicitProxy(Uri address) : IWebProxy
    {
        public ICredentials? Credentials { get; set; }

        public Uri GetProxy(Uri destination) => address;

        public bool IsBypassed(Uri host) => false;
    }
}

/// <summary>Serves <c>file://</c> URLs from the local disk.</summary>
public sealed class FileDownloadHandler : IDownloadHandler
{
    public async Task<Response> DownloadAsync(Request request, Spider spider, CancellationToken cancellationToken)
    {
        var path = request.Uri.LocalPath;
        if (!File.Exists(path)) throw new FileNotFoundException($"No such file: {path}", path);
        var body = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        return ResponseTypes.Create(request.Url, 200, new Headers(), body, request);
    }
}

/// <summary>Serves <c>data:</c> URIs (RFC 2397), handy for tests and inline documents.</summary>
public sealed class DataUriDownloadHandler : IDownloadHandler
{
    public Task<Response> DownloadAsync(Request request, Spider spider, CancellationToken cancellationToken)
    {
        var url = request.Url;
        var comma = url.IndexOf(',');
        if (!url.StartsWith("data:", StringComparison.OrdinalIgnoreCase) || comma < 0) throw new FormatException($"Invalid data URI: {url}");
        var meta = url[5..comma];
        var data = url[(comma + 1)..];
        var isBase64 = meta.EndsWith(";base64", StringComparison.OrdinalIgnoreCase);
        if (isBase64) meta = meta[..^7];
        var mediaType = string.IsNullOrEmpty(meta) ? "text/plain;charset=US-ASCII" : meta;
        var body = isBase64 ? Convert.FromBase64String(Uri.UnescapeDataString(data)) : Encoding.UTF8.GetBytes(Uri.UnescapeDataString(data));
        var headers = new Headers();
        headers.Set("Content-Type", mediaType);
        return Task.FromResult(ResponseTypes.Create(request.Url, 200, headers, body, request));
    }
}

/// <summary>Routes each request to the handler registered for its scheme.</summary>
public sealed class DownloadHandlerRegistry : IDisposable
{
    private readonly Dictionary<string, IDownloadHandler?> _byScheme = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Type, IDownloadHandler> _byType = [];
    private readonly Crawler _crawler;
    private readonly Dictionary<string, object?> _configured;

    public DownloadHandlerRegistry(Crawler crawler)
    {
        _crawler = crawler;
        _configured = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["http"] = typeof(HttpDownloadHandler),
            ["https"] = typeof(HttpDownloadHandler),
            ["file"] = typeof(FileDownloadHandler),
            ["data"] = typeof(DataUriDownloadHandler),
        };
        foreach (var (scheme, value) in crawler.Settings.GetDict(SettingKeys.DownloadHandlers)) _configured[scheme] = value;
    }

    public IDownloadHandler GetHandler(string scheme)
    {
        lock (_byScheme)
        {
            if (_byScheme.TryGetValue(scheme, out var cached))
                return cached ?? throw new NotSupportedException($"Unsupported URL scheme '{scheme}': no handler available for that scheme");

            IDownloadHandler? handler = null;
            if (_configured.TryGetValue(scheme, out var entry) && entry is not null)
            {
                var type = entry as Type ?? TypeResolver.Resolve(entry.ToString()!) ?? throw new InvalidOperationException($"Unknown download handler {entry}");
                // One instance per handler type: http and https share a connection pool.
                if (!_byType.TryGetValue(type, out handler))
                {
                    handler = (IDownloadHandler?)ComponentFactory.Create(type, _crawler);
                    if (handler is not null) _byType[type] = handler;
                }
            }
            _byScheme[scheme] = handler;
            return handler ?? throw new NotSupportedException($"Unsupported URL scheme '{scheme}': no handler available for that scheme");
        }
    }

    /// <summary>A shared default HTTP handler, for handlers that fall back to plain HTTP.</summary>
    public IDownloadHandler GetOrCreate(Type type)
    {
        lock (_byScheme)
        {
            if (!_byType.TryGetValue(type, out var handler))
            {
                handler = (IDownloadHandler)ComponentFactory.Create(type, _crawler)!;
                _byType[type] = handler;
            }
            return handler;
        }
    }

    public void Dispose()
    {
        foreach (var h in _byType.Values)
        {
            if (h is IDisposable d) d.Dispose();
            else if (h is IAsyncDisposable ad) ad.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        _byType.Clear();
        _byScheme.Clear();
    }
}
