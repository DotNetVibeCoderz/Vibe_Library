using System.Collections;
using System.Globalization;
using Microsoft.Extensions.Logging;

namespace ScrapyNet.DownloaderMiddlewares;

/// <summary>
/// Applies proxies: <c>meta["proxy"]</c> set by the spider wins; otherwise the standard
/// <c>http_proxy</c>/<c>https_proxy</c>/<c>no_proxy</c> environment variables. Port of Scrapy's
/// <c>HttpProxyMiddleware</c>. Credentials in the proxy URL (<c>http://user:pass@host:port</c>) are
/// handled by the download handler.
/// </summary>
public sealed class HttpProxyMiddleware : DownloaderMiddleware
{
    private readonly Dictionary<string, string> _proxies = new(StringComparer.OrdinalIgnoreCase);
    private readonly string[] _noProxy;

    public HttpProxyMiddleware(Settings settings)
    {
        if (!settings.GetBool(SettingKeys.HttpProxyEnabled, true)) throw new NotConfiguredException();
        foreach (var scheme in new[] { "http", "https" })
        {
            var value = Environment.GetEnvironmentVariable($"{scheme}_proxy") ?? Environment.GetEnvironmentVariable($"{scheme.ToUpperInvariant()}_PROXY");
            if (!string.IsNullOrWhiteSpace(value)) _proxies[scheme] = value.Contains("://") ? value : "http://" + value;
        }
        _noProxy = (Environment.GetEnvironmentVariable("no_proxy") ?? Environment.GetEnvironmentVariable("NO_PROXY") ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public override ValueTask<DownloadResult> ProcessRequestAsync(Request request, Spider spider)
    {
        if (request.Meta.ContainsKey(MetaKeys.Proxy)) return DownloadResult.Continue;
        if (_proxies.TryGetValue(request.Uri.Scheme, out var proxy) && !IsBypassed(request.Uri.Host))
            request.Meta[MetaKeys.Proxy] = proxy;
        return DownloadResult.Continue;
    }

    private bool IsBypassed(string host) => _noProxy.Any(p => p == "*" || UrlUtils.HostMatchesAny(host.ToLowerInvariant(), [p]));
}

/// <summary>Health and usage counters of one proxy in a <see cref="ProxyRotationMiddleware"/>.</summary>
public sealed class ProxyState(string url)
{
    public string Url { get; } = url;

    /// <summary>The proxy URL without credentials, safe to log.</summary>
    public string Name { get; } = RedactCredentials(url);

    public long Requests;
    public long Successes;
    public long Failures;
    public DateTimeOffset? BannedUntil { get; set; }

    public bool IsAvailable(DateTimeOffset now) => BannedUntil is null || BannedUntil <= now;

    internal static string RedactCredentials(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.UserInfo) ? $"{uri.Scheme}://{uri.Host}:{uri.Port}" : url;
}

/// <summary>
/// Rotates requests across a pool of proxies (<c>PROXY_LIST</c>), with optional fixed proxies per
/// domain (<c>PROXY_PER_DOMAIN</c>). A proxy answering with a ban status (<c>PROXY_BAN_STATUS_CODES</c>,
/// default 403/407/429) or failing to connect is benched for <c>PROXY_BAN_COOLDOWN</c> seconds and the
/// request is retried through another proxy.
/// </summary>
/// <remarks>
/// Not enabled by default. Register it before <see cref="HttpProxyMiddleware"/>:
/// <c>settings.DownloaderMiddlewares.Add&lt;ProxyRotationMiddleware&gt;(740)</c>.
/// </remarks>
public sealed class ProxyRotationMiddleware : DownloaderMiddleware
{
    private readonly List<ProxyState> _pool;
    private readonly Dictionary<string, List<string>> _perDomain;
    private readonly HashSet<int> _banCodes;
    private readonly TimeSpan _cooldown;
    private readonly IStatsCollector _stats;
    private readonly ILogger _logger;
    private int _next;

    public ProxyRotationMiddleware(Settings settings, IStatsCollector stats, ILogger logger)
    {
        _pool = [.. settings.GetList(SettingKeys.ProxyList).Select(u => new ProxyState(u))];
        _perDomain = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (domain, value) in settings.GetDict(SettingKeys.ProxyPerDomain))
            _perDomain[domain] = value is IEnumerable e and not string ? [.. e.Cast<object>().Select(x => x.ToString()!)] : [value!.ToString()!];
        if (_pool.Count == 0 && _perDomain.Count == 0) throw new NotConfiguredException("PROXY_LIST and PROXY_PER_DOMAIN are empty");
        _banCodes = [.. settings.GetIntList(SettingKeys.ProxyBanStatusCodes, [403, 407, 429])];
        _cooldown = TimeSpan.FromSeconds(settings.GetDouble(SettingKeys.ProxyBanCooldown, 300));
        _stats = stats;
        _logger = logger;
    }

    /// <summary>Snapshot of the pool, for dashboards.</summary>
    public IReadOnlyList<ProxyState> Proxies => _pool;

    public override ValueTask<DownloadResult> ProcessRequestAsync(Request request, Spider spider)
    {
        if (request.Meta.ContainsKey(MetaKeys.Proxy) && !request.Meta.ContainsKey(MetaKeys.ProxyName)) return DownloadResult.Continue;

        var host = request.Uri.Host.ToLowerInvariant();
        foreach (var (domain, proxies) in _perDomain)
        {
            if (!UrlUtils.HostMatchesAny(host, [domain])) continue;
            var fixedProxy = proxies[Random.Shared.Next(proxies.Count)];
            request.Meta[MetaKeys.Proxy] = fixedProxy;
            request.Meta[MetaKeys.ProxyName] = ProxyState.RedactCredentials(fixedProxy);
            return DownloadResult.Continue;
        }

        var state = Pick();
        if (state is null)
        {
            _stats.Inc("proxies/exhausted");
            throw new IgnoreRequestException("No healthy proxy available");
        }
        Interlocked.Increment(ref state.Requests);
        request.Meta[MetaKeys.Proxy] = state.Url;
        request.Meta[MetaKeys.ProxyName] = state.Name;
        _stats.Inc($"proxies/used/{state.Name}");
        return DownloadResult.Continue;
    }

    public override ValueTask<DownloadResult> ProcessResponseAsync(Request request, Response response, Spider spider)
    {
        var state = Find(request);
        if (state is null) return (DownloadResult)response;
        if (_banCodes.Contains(response.Status))
        {
            Ban(state, $"status {response.Status}");
            return Reroute(request) is { } retry ? (DownloadResult)retry : (DownloadResult)response;
        }
        Interlocked.Increment(ref state.Successes);
        return (DownloadResult)response;
    }

    public override ValueTask<DownloadResult> ProcessExceptionAsync(Request request, Exception exception, Spider spider)
    {
        var state = Find(request);
        if (state is null || !RetryMiddleware.IsRetryable(exception)) return DownloadResult.Continue;
        Ban(state, exception.GetType().Name);
        return Reroute(request) is { } retry ? (DownloadResult)retry : DownloadResult.Continue;
    }

    private ProxyState? Pick()
    {
        if (_pool.Count == 0) return null;
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < _pool.Count; i++)
        {
            var state = _pool[(int)((uint)Interlocked.Increment(ref _next) % (uint)_pool.Count)];
            if (state.IsAvailable(now)) return state;
        }
        return null;
    }

    private ProxyState? Find(Request request) =>
        request.Meta.GetValueOrDefault(MetaKeys.Proxy) is string url ? _pool.FirstOrDefault(p => p.Url == url) : null;

    private void Ban(ProxyState state, string reason)
    {
        Interlocked.Increment(ref state.Failures);
        state.BannedUntil = DateTimeOffset.UtcNow + _cooldown;
        _stats.Inc("proxies/banned");
        _logger.LogInformation("Proxy {Proxy} benched for {Cooldown}s ({Reason})", state.Name, _cooldown.TotalSeconds, reason);
    }

    private Request? Reroute(Request request)
    {
        var attempts = request.GetMeta<int>("proxy_retry_times") + 1;
        if (attempts > Math.Max(1, _pool.Count)) return null;
        var retry = request with { DontFilter = true };
        retry.Meta.Remove(MetaKeys.Proxy);
        retry.Meta.Remove(MetaKeys.ProxyName);
        retry.Meta["proxy_retry_times"] = attempts;
        _stats.Inc("proxies/rerouted");
        return retry;
    }
}
