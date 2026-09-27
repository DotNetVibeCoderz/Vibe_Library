using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Logging;

using ScrapyNet.Handlers;

namespace ScrapyNet.Engine;

/// <summary>
/// Runs requests through the downloader middlewares and the download handlers, enforcing global,
/// per-domain and per-IP concurrency and download delays. Port of <c>scrapy.core.downloader.Downloader</c>.
/// </summary>
public sealed class Downloader : IDisposable
{
    private readonly Crawler _crawler;
    private readonly ConcurrentDictionary<string, DownloadSlot> _slots = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _ipCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly IReadOnlyDictionary<string, object?> _slotConfig;
    private readonly int _perDomain;
    private readonly int _perIp;
    private readonly double _delay;
    private readonly bool _randomize;
    private int _active;

    public Downloader(Crawler crawler)
    {
        _crawler = crawler;
        var s = crawler.Settings;
        TotalConcurrency = Math.Max(1, s.GetInt(SettingKeys.ConcurrentRequests, 16));
        _perDomain = Math.Max(1, s.GetInt(SettingKeys.ConcurrentRequestsPerDomain, 8));
        _perIp = s.GetInt(SettingKeys.ConcurrentRequestsPerIp);
        _delay = s.GetDouble(SettingKeys.DownloadDelay);
        _randomize = s.GetBool(SettingKeys.RandomizeDownloadDelay, true);
        _slotConfig = s.GetDict(SettingKeys.DownloadSlots);
        Handlers = new DownloadHandlerRegistry(crawler);
        Middleware = new DownloaderMiddlewareManager(crawler);
    }

    public int TotalConcurrency { get; }

    /// <summary>Requests inside the downloader (middlewares, slot queues and transfers).</summary>
    public int ActiveCount => Volatile.Read(ref _active);

    public bool NeedsBackout => ActiveCount >= TotalConcurrency;

    public IReadOnlyDictionary<string, DownloadSlot> Slots => _slots;

    public DownloadHandlerRegistry Handlers { get; }

    public DownloaderMiddlewareManager Middleware { get; }

    /// <summary>
    /// Downloads a request through the middleware chain. Returns a <see cref="Response"/>, or a
    /// <see cref="Request"/> that a middleware wants scheduled instead (redirects, retries).
    /// </summary>
    public async Task<object> FetchAsync(Request request, Spider spider, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _active);
        try
        {
            return await Middleware.DownloadAsync(request, spider, DownloadAsync, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Decrement(ref _active);
        }
    }

    private async Task<Response> DownloadAsync(Request request, Spider spider, CancellationToken cancellationToken)
    {
        var slot = await GetSlotAsync(request, cancellationToken).ConfigureAwait(false);
        request.Meta[MetaKeys.DownloadSlot] = slot.Key;
        using var lease = await slot.AcquireAsync(cancellationToken).ConfigureAwait(false);
        await _crawler.Signals.SendAsync(Signals.RequestReachedDownloader, new RequestEventArgs(request, spider)).ConfigureAwait(false);
        try
        {
            var handler = Handlers.GetHandler(request.Uri.Scheme);
            var response = await handler.DownloadAsync(request, spider, cancellationToken).ConfigureAwait(false);
            if (!response.HasRequest) response.Request = request;
            await _crawler.Signals.SendAsync(Signals.ResponseDownloaded, new ResponseEventArgs(response, request, spider)).ConfigureAwait(false);
            return response;
        }
        finally
        {
            await _crawler.Signals.SendAsync(Signals.RequestLeftDownloader, new RequestEventArgs(request, spider)).ConfigureAwait(false);
        }
    }

    /// <summary>The slot a request downloads in: <c>meta["download_slot"]</c>, else its host (or IP with <c>CONCURRENT_REQUESTS_PER_IP</c>).</summary>
    public async ValueTask<DownloadSlot> GetSlotAsync(Request request, CancellationToken cancellationToken = default)
    {
        var key = GetSlotKey(request);
        if (_perIp > 0 && !request.Meta.ContainsKey(MetaKeys.DownloadSlot))
            key = await ResolveIpAsync(key, cancellationToken).ConfigureAwait(false);
        return GetOrCreateSlot(key);
    }

    public DownloadSlot GetOrCreateSlot(string key) => _slots.GetOrAdd(key, k =>
    {
        var concurrency = _perIp > 0 ? _perIp : _perDomain;
        var delay = _delay;
        var randomize = _randomize;
        if (_slotConfig.TryGetValue(k, out var raw) && raw is IDictionary<string, object?> custom)
        {
            if (custom.TryGetValue("concurrency", out var c) && c is not null) concurrency = Convert.ToInt32(c, CultureInfo.InvariantCulture);
            if (custom.TryGetValue("delay", out var d) && d is not null) delay = Convert.ToDouble(d, CultureInfo.InvariantCulture);
            if (custom.TryGetValue("randomize_delay", out var r) && r is not null) randomize = Convert.ToBoolean(r, CultureInfo.InvariantCulture);
        }
        return new DownloadSlot(k, concurrency, delay, randomize);
    });

    public static string GetSlotKey(Request request) =>
        request.Meta.TryGetValue(MetaKeys.DownloadSlot, out var s) && s is string slot ? slot : request.Uri.Host.ToLowerInvariant();

    private async ValueTask<string> ResolveIpAsync(string host, CancellationToken ct)
    {
        if (_ipCache.TryGetValue(host, out var ip)) return ip;
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host, ct).ConfigureAwait(false);
            ip = addresses.Length > 0 ? addresses[0].ToString() : host;
        }
        catch (System.Net.Sockets.SocketException)
        {
            ip = host;
        }
        _ipCache[host] = ip;
        return ip;
    }

    public void Dispose() => Handlers.Dispose();
}

/// <summary>Chains downloader middlewares around the actual download (Scrapy's <c>DownloaderMiddlewareManager</c>).</summary>
public sealed class DownloaderMiddlewareManager
{
    private readonly DownloaderMiddleware[] _middlewares;

    public DownloaderMiddlewareManager(Crawler crawler)
    {
        var list = new List<DownloaderMiddleware>();
        foreach (var entry in crawler.Settings.DownloaderMiddlewares.GetEnabled())
        {
            var mw = ComponentFactory.Create<DownloaderMiddleware>(entry, crawler);
            if (mw is not null) list.Add(mw);
        }
        _middlewares = [.. list];
        crawler.LoggerFactory.CreateLogger("scrapynet.middleware").LogInformation("Enabled downloader middlewares:\n[{Middlewares}]",
            string.Join(",\n ", _middlewares.Select(m => $"'{m.GetType().FullName}'")));
    }

    public IReadOnlyList<DownloaderMiddleware> Middlewares => _middlewares;

    public async ValueTask OpenSpiderAsync(Spider spider)
    {
        foreach (var mw in _middlewares) await mw.OpenSpiderAsync(spider).ConfigureAwait(false);
    }

    public async ValueTask CloseSpiderAsync(Spider spider, string reason)
    {
        foreach (var mw in _middlewares) await mw.CloseSpiderAsync(spider, reason).ConfigureAwait(false);
    }

    public async Task<object> DownloadAsync(Request request, Spider spider,
        Func<Request, Spider, CancellationToken, Task<Response>> download, CancellationToken cancellationToken)
    {
        Response? response = null;
        try
        {
            foreach (var mw in _middlewares)
            {
                var result = await mw.ProcessRequestAsync(request, spider).ConfigureAwait(false);
                if (result.Request is not null) return result.Request;
                if (result.Response is not null)
                {
                    response = result.Response;
                    break;
                }
            }
            response ??= await download(request, spider, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (!(ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
        {
            var handled = false;
            for (var i = _middlewares.Length - 1; i >= 0; i--)
            {
                var result = await _middlewares[i].ProcessExceptionAsync(request, ex, spider).ConfigureAwait(false);
                if (result.Request is not null) return result.Request;
                if (result.Response is not null)
                {
                    response = result.Response;
                    handled = true;
                    break;
                }
            }
            if (!handled) ExceptionDispatchInfo.Capture(ex).Throw();
        }

        if (!response!.HasRequest) response.Request = request;
        for (var i = _middlewares.Length - 1; i >= 0; i--)
        {
            var result = await _middlewares[i].ProcessResponseAsync(request, response, spider).ConfigureAwait(false);
            if (result.Request is not null) return result.Request;
            response = result.Response ?? throw new InvalidOperationException(
                $"{_middlewares[i].GetType().Name}.ProcessResponseAsync must return a Response or a Request.");
            if (!response.HasRequest) response.Request = request;
        }
        return response;
    }
}
