using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace ScrapyNet.DownloaderMiddlewares;

/// <summary>Adds <c>DEFAULT_REQUEST_HEADERS</c> to requests that don't set them.</summary>
public sealed class DefaultHeadersMiddleware : DownloaderMiddleware
{
    private readonly KeyValuePair<string, string>[] _headers;

    public DefaultHeadersMiddleware(Settings settings)
    {
        _headers = [.. settings.GetDict(SettingKeys.DefaultRequestHeaders)
            .Where(kv => kv.Value is not null)
            .Select(kv => new KeyValuePair<string, string>(kv.Key, Convert.ToString(kv.Value, CultureInfo.InvariantCulture)!))];
        if (_headers.Length == 0) throw new NotConfiguredException();
    }

    public override ValueTask<DownloadResult> ProcessRequestAsync(Request request, Spider spider)
    {
        foreach (var (name, value) in _headers) request.Headers.SetDefault(name, value);
        return DownloadResult.Continue;
    }
}

/// <summary>
/// Adds headers per domain from <c>DOMAIN_HEADERS</c> — the place for API keys and tokens that must
/// only ever be sent to one host: <c>{"api.example.com": {"X-Api-Key": "..."}}</c>.
/// </summary>
public sealed class DomainHeadersMiddleware : DownloaderMiddleware
{
    private readonly (string Domain, KeyValuePair<string, string>[] Headers)[] _rules;

    public DomainHeadersMiddleware(Settings settings)
    {
        _rules = [.. settings.GetDict(SettingKeys.DomainHeaders).Select(kv => (kv.Key.ToLowerInvariant(), ToHeaders(kv.Value)))];
        if (_rules.Length == 0) throw new NotConfiguredException();
    }

    private static KeyValuePair<string, string>[] ToHeaders(object? value) => value switch
    {
        IDictionary<string, object?> d => [.. d.Where(x => x.Value is not null).Select(x => new KeyValuePair<string, string>(x.Key, x.Value!.ToString()!))],
        IDictionary<string, string> ds => [.. ds],
        _ => throw new ArgumentException("DOMAIN_HEADERS values must be header dictionaries."),
    };

    public override ValueTask<DownloadResult> ProcessRequestAsync(Request request, Spider spider)
    {
        var host = request.Uri.Host.ToLowerInvariant();
        foreach (var (domain, headers) in _rules)
            if (UrlUtils.HostMatchesAny(host, [domain]))
                foreach (var (name, value) in headers) request.Headers.SetDefault(name, value);
        return DownloadResult.Continue;
    }
}

/// <summary>Sets the <c>User-Agent</c> header from <c>USER_AGENT</c> (or the spider's <c>user_agent</c> argument).</summary>
public sealed class UserAgentMiddleware(Settings settings) : DownloaderMiddleware
{
    private string? _userAgent = settings.GetString(SettingKeys.UserAgent);

    public override ValueTask OpenSpiderAsync(Spider spider)
    {
        if (spider.Arguments.TryGetValue("user_agent", out var ua)) _userAgent = ua;
        return ValueTask.CompletedTask;
    }

    public override ValueTask<DownloadResult> ProcessRequestAsync(Request request, Spider spider)
    {
        if (!string.IsNullOrEmpty(_userAgent)) request.Headers.SetDefault("User-Agent", _userAgent);
        return DownloadResult.Continue;
    }
}

/// <summary>
/// Picks a random User-Agent per request from <c>USER_AGENT_LIST</c> (or a built-in list of current
/// desktop and mobile browsers). Not enabled by default; register it before <see cref="UserAgentMiddleware"/>:
/// <c>settings.DownloaderMiddlewares.Add&lt;RandomUserAgentMiddleware&gt;(400)</c>.
/// </summary>
public sealed class RandomUserAgentMiddleware : DownloaderMiddleware
{
    public static readonly IReadOnlyList<string> DefaultUserAgents =
    [
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36",
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36 Edg/128.0.0.0",
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:130.0) Gecko/20100101 Firefox/130.0",
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 14_6) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.6 Safari/605.1.15",
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 14_6) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36",
        "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36",
        "Mozilla/5.0 (X11; Ubuntu; Linux x86_64; rv:130.0) Gecko/20100101 Firefox/130.0",
        "Mozilla/5.0 (iPhone; CPU iPhone OS 17_6 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.6 Mobile/15E148 Safari/604.1",
        "Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Mobile Safari/537.36",
    ];

    private readonly IReadOnlyList<string> _agents;

    public RandomUserAgentMiddleware(Settings settings)
    {
        var configured = settings.GetList(SettingKeys.UserAgentList);
        _agents = configured.Count > 0 ? configured : DefaultUserAgents;
    }

    public override ValueTask<DownloadResult> ProcessRequestAsync(Request request, Spider spider)
    {
        if (!request.Headers.Contains("User-Agent"))
            request.Headers.Set("User-Agent", _agents[Random.Shared.Next(_agents.Count)]);
        return DownloadResult.Continue;
    }
}

/// <summary>Copies <c>DOWNLOAD_TIMEOUT</c> into <c>meta["download_timeout"]</c> when a request doesn't set its own.</summary>
public sealed class DownloadTimeoutMiddleware(Settings settings) : DownloaderMiddleware
{
    private readonly double _timeout = settings.GetDouble(SettingKeys.DownloadTimeout, 180);

    public override ValueTask<DownloadResult> ProcessRequestAsync(Request request, Spider spider)
    {
        if (_timeout > 0) request.Meta.TryAdd(MetaKeys.DownloadTimeout, _timeout);
        return DownloadResult.Continue;
    }
}

/// <summary>
/// HTTP authentication from <c>HTTP_USER</c>/<c>HTTP_PASS</c> (Basic) or <c>HTTP_BEARER_TOKEN</c>, sent only to
/// <c>HTTP_AUTH_DOMAIN</c> (defaulting to the first start URL's domain) so credentials never leak to other hosts.
/// </summary>
public sealed class HttpAuthMiddleware : DownloaderMiddleware
{
    private readonly string? _header;
    private string? _domain;

    public HttpAuthMiddleware(Settings settings)
    {
        var user = settings.GetString(SettingKeys.HttpUser);
        var pass = settings.GetString(SettingKeys.HttpPass);
        var token = settings.GetString(SettingKeys.HttpBearerToken);
        if (!string.IsNullOrEmpty(token)) _header = "Bearer " + token;
        else if (!string.IsNullOrEmpty(user) || !string.IsNullOrEmpty(pass))
            _header = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{pass}"));
        if (_header is null) throw new NotConfiguredException();
        _domain = settings.GetString(SettingKeys.HttpAuthDomain);
    }

    public override ValueTask OpenSpiderAsync(Spider spider)
    {
        if (string.IsNullOrEmpty(_domain) && spider.StartUrls.Count > 0) _domain = UrlUtils.GetHost(spider.StartUrls[0]);
        return ValueTask.CompletedTask;
    }

    public override ValueTask<DownloadResult> ProcessRequestAsync(Request request, Spider spider)
    {
        if (_header is not null && !request.Headers.Contains("Authorization") &&
            (string.IsNullOrEmpty(_domain) || UrlUtils.HostMatchesAny(request.Uri.Host.ToLowerInvariant(), [_domain])))
            request.Headers.Set("Authorization", _header);
        return DownloadResult.Continue;
    }
}

/// <summary>Counts requests, responses, bytes, status codes and exceptions into <c>downloader/*</c> stats.</summary>
public sealed class DownloaderStatsMiddleware(IStatsCollector stats) : DownloaderMiddleware
{
    public override ValueTask<DownloadResult> ProcessRequestAsync(Request request, Spider spider)
    {
        stats.Inc("downloader/request_count");
        stats.Inc($"downloader/request_method_count/{request.Method}");
        stats.Inc("downloader/request_bytes", EstimateRequestSize(request));
        return DownloadResult.Continue;
    }

    public override ValueTask<DownloadResult> ProcessResponseAsync(Request request, Response response, Spider spider)
    {
        stats.Inc("downloader/response_count");
        stats.Inc($"downloader/response_status_count/{response.Status}");
        stats.Inc("downloader/response_bytes", response.Body.Length + EstimateHeadersSize(response.Headers));
        return (DownloadResult)response;
    }

    public override ValueTask<DownloadResult> ProcessExceptionAsync(Request request, Exception exception, Spider spider)
    {
        if (exception is IgnoreRequestException) return DownloadResult.Continue;
        stats.Inc("downloader/exception_count");
        stats.Inc($"downloader/exception_type_count/{exception.GetType().FullName}");
        return DownloadResult.Continue;
    }

    private static long EstimateRequestSize(Request request) =>
        request.Method.Length + request.Url.Length + 12 + EstimateHeadersSize(request.Headers) + request.Body.Length;

    private static long EstimateHeadersSize(Headers headers)
    {
        long size = 0;
        foreach (var (name, values) in headers)
            foreach (var v in values) size += name.Length + v.Length + 4;
        return size;
    }
}

/// <summary>
/// Drops requests to domains outside the spider's <see cref="Spider.AllowedDomains"/>, including start
/// requests (the spider-middleware version only sees callback output). <c>dont_filter</c> requests pass.
/// </summary>
public sealed class OffsiteDownloaderMiddleware(IStatsCollector stats, ILogger logger) : DownloaderMiddleware
{
    private IReadOnlyList<string> _domains = [];
    private readonly HashSet<string> _logged = new(StringComparer.OrdinalIgnoreCase);

    public override ValueTask OpenSpiderAsync(Spider spider)
    {
        _domains = spider.AllowedDomains;
        return ValueTask.CompletedTask;
    }

    public override ValueTask<DownloadResult> ProcessRequestAsync(Request request, Spider spider)
    {
        if (_domains.Count == 0 || request.DontFilter || request.Meta.ContainsKey("allow_offsite")) return DownloadResult.Continue;
        if (request.Uri.Scheme is not ("http" or "https")) return DownloadResult.Continue;
        var host = request.Uri.Host.ToLowerInvariant();
        if (UrlUtils.HostMatchesAny(host, _domains)) return DownloadResult.Continue;
        lock (_logged)
        {
            if (_logged.Add(host)) logger.LogDebug("Filtered offsite request to '{Host}': {Request}", host, request);
        }
        stats.Inc("offsite/domains", _logged.Count == 1 ? 1 : 0);
        stats.Inc("offsite/filtered");
        throw new IgnoreRequestException($"Filtered offsite request to {host}");
    }
}
