using System.Collections;
using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace ScrapyNet.SpiderMiddlewares;

/// <summary>
/// Filters out non-2xx responses before they reach callbacks, unless the spider asked for them via
/// <see cref="Spider.HandleHttpStatusList"/>, <c>meta["handle_httpstatus_list"]</c>,
/// <c>meta["handle_httpstatus_all"]</c>, <c>HTTPERROR_ALLOWED_CODES</c> or <c>HTTPERROR_ALLOW_ALL</c>.
/// Filtered responses reach errbacks as <see cref="HttpErrorException"/>. Port of Scrapy's <c>HttpErrorMiddleware</c>.
/// </summary>
public sealed class HttpErrorMiddleware(Settings settings, IStatsCollector stats, ILogger logger) : SpiderMiddleware
{
    private readonly bool _allowAll = settings.GetBool(SettingKeys.HttpErrorAllowAll);
    private readonly HashSet<int> _allowed = [.. settings.GetIntList(SettingKeys.HttpErrorAllowedCodes)];

    public override ValueTask ProcessSpiderInputAsync(Response response, Spider spider)
    {
        if (response.IsSuccess || _allowAll) return ValueTask.CompletedTask;
        var meta = response.Request.Meta;
        if (meta.TryGetValue(MetaKeys.HandleHttpStatusAll, out var all) && all is true) return ValueTask.CompletedTask;
        if (meta.TryGetValue(MetaKeys.HandleHttpStatusList, out var list) && list is IEnumerable e and not string)
        {
            if (e.Cast<object>().Any(x => Convert.ToInt32(x, CultureInfo.InvariantCulture) == response.Status)) return ValueTask.CompletedTask;
        }
        else
        {
            if (_allowed.Contains(response.Status)) return ValueTask.CompletedTask;
            if (spider.HandleHttpStatusList?.Contains(response.Status) == true) return ValueTask.CompletedTask;
        }
        throw new HttpErrorException(response);
    }

    public override IAsyncEnumerable<object>? ProcessSpiderException(Response response, Exception exception, Spider spider)
    {
        if (exception is not HttpErrorException http) return null;
        stats.Inc("httperror/response_ignored_count");
        stats.Inc($"httperror/response_ignored_status_count/{http.Response.Status}");
        logger.LogInformation("Ignoring response {Response}: HTTP status code is not handled or not allowed", http.Response);
        return AsyncEnumerable.Empty<object>();
    }
}

/// <summary>Drops requests to domains outside <see cref="Spider.AllowedDomains"/> (Scrapy's <c>OffsiteMiddleware</c>).</summary>
public sealed class OffsiteMiddleware(IStatsCollector stats, ILogger logger) : SpiderMiddleware
{
    private readonly HashSet<string> _seenDomains = new(StringComparer.OrdinalIgnoreCase);

    public override async IAsyncEnumerable<object> ProcessSpiderOutput(Response response, IAsyncEnumerable<object> result, Spider spider)
    {
        var domains = spider.AllowedDomains;
        await foreach (var x in result.ConfigureAwait(false))
        {
            if (x is Request request && domains.Count > 0 && !request.DontFilter && request.Uri.Scheme is "http" or "https"
                && !UrlUtils.HostMatchesAny(request.Uri.Host.ToLowerInvariant(), domains))
            {
                var host = request.Uri.Host;
                bool first;
                lock (_seenDomains) first = _seenDomains.Add(host);
                if (first)
                {
                    logger.LogDebug("Filtered offsite request to '{Host}': {Request}", host, request);
                    stats.Inc("offsite/domains");
                }
                stats.Inc("offsite/filtered");
                continue;
            }
            yield return x;
        }
    }
}

/// <summary>Sets the <c>Referer</c> header on follow-up requests (Scrapy's <c>RefererMiddleware</c>).</summary>
public sealed class RefererMiddleware : SpiderMiddleware
{
    private readonly string _policy;

    public RefererMiddleware(Settings settings)
    {
        if (!settings.GetBool(SettingKeys.RefererEnabled, true)) throw new NotConfiguredException();
        _policy = (settings.GetString(SettingKeys.RefererPolicy) ?? "no-referrer-when-downgrade").ToLowerInvariant();
    }

    public override async IAsyncEnumerable<object> ProcessSpiderOutput(Response response, IAsyncEnumerable<object> result, Spider spider)
    {
        await foreach (var x in result.ConfigureAwait(false))
        {
            if (x is Request request && !request.Headers.Contains("Referer"))
            {
                var referer = Referer(response.Uri, request.Uri);
                if (referer is not null) request.Headers.Set("Referer", referer);
            }
            yield return x;
        }
    }

    /// <summary>The Referer value per the configured policy, or <c>null</c> to send none.</summary>
    public string? Referer(Uri from, Uri to)
    {
        if (from.Scheme is not ("http" or "https")) return null;
        var stripped = new UriBuilder(from) { UserName = "", Password = "", Fragment = "" }.Uri.AbsoluteUri;
        var origin = from.GetLeftPart(UriPartial.Authority) + "/";
        var sameOrigin = string.Equals(from.GetLeftPart(UriPartial.Authority), to.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase);
        var downgrade = from.Scheme == "https" && to.Scheme == "http";
        return _policy switch
        {
            "no-referrer" => null,
            "unsafe-url" => stripped,
            "origin" => origin,
            "same-origin" => sameOrigin ? stripped : null,
            "strict-origin" => downgrade ? null : origin,
            "origin-when-cross-origin" => sameOrigin ? stripped : origin,
            "strict-origin-when-cross-origin" => sameOrigin ? stripped : downgrade ? null : origin,
            _ => downgrade ? null : stripped,
        };
    }
}

/// <summary>Drops requests with URLs longer than <c>URLLENGTH_LIMIT</c> (Scrapy's <c>UrlLengthMiddleware</c>).</summary>
public sealed class UrlLengthMiddleware : SpiderMiddleware
{
    private readonly int _limit;
    private readonly IStatsCollector _stats;
    private readonly ILogger _logger;

    public UrlLengthMiddleware(Settings settings, IStatsCollector stats, ILogger logger)
    {
        _limit = settings.GetInt(SettingKeys.UrlLengthLimit, 2083);
        if (_limit <= 0) throw new NotConfiguredException();
        _stats = stats;
        _logger = logger;
    }

    public override async IAsyncEnumerable<object> ProcessSpiderOutput(Response response, IAsyncEnumerable<object> result, Spider spider)
    {
        await foreach (var x in result.ConfigureAwait(false))
        {
            if (x is Request request && request.Url.Length > _limit)
            {
                _logger.LogInformation("Ignoring link (url length > {Limit}): {Url}", _limit, request.Url);
                _stats.Inc("urllength/request_ignored_count");
                continue;
            }
            yield return x;
        }
    }
}

/// <summary>
/// Tracks crawl depth in <c>meta["depth"]</c>, drops requests beyond <c>DEPTH_LIMIT</c>, and adjusts
/// priority by depth (<c>DEPTH_PRIORITY</c>: positive = breadth-first, negative = depth-first).
/// Port of Scrapy's <c>DepthMiddleware</c>.
/// </summary>
public sealed class DepthMiddleware(Settings settings, IStatsCollector stats, ILogger logger) : SpiderMiddleware
{
    private readonly int _maxDepth = settings.GetInt(SettingKeys.DepthLimit);
    private readonly int _priority = settings.GetInt(SettingKeys.DepthPriority);
    private readonly bool _verbose = settings.GetBool(SettingKeys.DepthStatsVerbose);

    public override async IAsyncEnumerable<Request> ProcessStartRequests(IAsyncEnumerable<Request> startRequests, Spider spider)
    {
        await foreach (var r in startRequests.ConfigureAwait(false))
        {
            r.Meta.TryAdd(MetaKeys.Depth, 0);
            yield return r;
        }
    }

    public override async IAsyncEnumerable<object> ProcessSpiderOutput(Response response, IAsyncEnumerable<object> result, Spider spider)
    {
        var parentDepth = response.Request.Depth;
        if (_verbose && parentDepth == 0) stats.Inc("request_depth_count/0");
        await foreach (var x in result.ConfigureAwait(false))
        {
            if (x is Request request)
            {
                var depth = parentDepth + 1;
                request.Meta[MetaKeys.Depth] = depth;
                if (_maxDepth > 0 && depth > _maxDepth)
                {
                    logger.LogDebug("Ignoring link (depth > {Max}): {Url}", _maxDepth, request.Url);
                    continue;
                }
                if (_verbose) stats.Inc($"request_depth_count/{depth}");
                stats.Max("request_depth_max", depth);
                if (_priority != 0)
                {
                    yield return request with { Priority = request.Priority - depth * _priority };
                    continue;
                }
            }
            yield return x;
        }
    }
}
