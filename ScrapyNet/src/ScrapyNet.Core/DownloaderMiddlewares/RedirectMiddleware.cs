using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace ScrapyNet.DownloaderMiddlewares;

/// <summary>
/// Follows 301/302/303/307/308 redirects by scheduling a new request, so redirected URLs pass the dupe
/// filter and show up in stats. Tracks <c>meta["redirect_urls"]</c> and stops after
/// <c>REDIRECT_MAX_TIMES</c>. Port of Scrapy's <c>RedirectMiddleware</c>.
/// </summary>
public class RedirectMiddleware : DownloaderMiddleware
{
    private readonly int _maxTimes;
    private readonly int _priorityAdjust;
    private readonly IStatsCollector _stats;
    private readonly ILogger _logger;

    public RedirectMiddleware(Settings settings, IStatsCollector stats, ILogger logger)
    {
        if (!settings.GetBool(SettingKeys.RedirectEnabled, true)) throw new NotConfiguredException();
        _maxTimes = settings.GetInt(SettingKeys.RedirectMaxTimes, 20);
        _priorityAdjust = settings.GetInt(SettingKeys.RedirectPriorityAdjust, 2);
        _stats = stats;
        _logger = logger;
    }

    public override ValueTask<DownloadResult> ProcessResponseAsync(Request request, Response response, Spider spider)
    {
        if (request.GetMeta(MetaKeys.DontRedirect, false)) return (DownloadResult)response;
        if (response.Status is not (301 or 302 or 303 or 307 or 308)) return (DownloadResult)response;
        if (IsHandledStatus(request, response, spider)) return (DownloadResult)response;

        var location = response.Headers.Get("Location");
        if (string.IsNullOrWhiteSpace(location)) return (DownloadResult)response;
        var target = UrlUtils.Join(request.Url, location);
        if (!UrlUtils.HasScheme(target) || !target.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return (DownloadResult)response;

        Request redirected;
        if (response.Status is 302 or 303 && request.Method != "HEAD" || response.Status is 301 && request.Method == "POST")
        {
            // Browsers turn these into GET and drop the body.
            redirected = request with { Url = target, Method = "GET", Body = [] };
            redirected.Headers.Remove("Content-Type");
            redirected.Headers.Remove("Content-Length");
        }
        else
        {
            redirected = request with { Url = target };
        }
        return Redirect(redirected, request, response.Status.ToString(CultureInfo.InvariantCulture), response.Status);
    }

    /// <summary>Statuses the spider asked to handle itself are not redirected.</summary>
    protected static bool IsHandledStatus(Request request, Response response, Spider spider)
    {
        if (request.GetMeta(MetaKeys.HandleHttpStatusAll, false)) return true;
        if (request.Meta.TryGetValue(MetaKeys.HandleHttpStatusList, out var list) && list is System.Collections.IEnumerable e &&
            e.Cast<object>().Any(x => Convert.ToInt32(x, CultureInfo.InvariantCulture) == response.Status)) return true;
        return spider.HandleHttpStatusList?.Contains(response.Status) == true;
    }

    protected DownloadResult Redirect(Request redirected, Request source, string reason, int status)
    {
        var times = source.GetMeta<int>(MetaKeys.RedirectTimes) + 1;
        var ttl = source.Meta.TryGetValue("redirect_ttl", out var t) && t is not null ? Convert.ToInt32(t, CultureInfo.InvariantCulture) : _maxTimes;
        if (times > _maxTimes || ttl <= 0)
        {
            _logger.LogDebug("Discarding {Request}: max redirections reached", source);
            throw new IgnoreRequestException("max redirections reached");
        }

        var urls = source.Meta.TryGetValue(MetaKeys.RedirectUrls, out var u) && u is List<string> existing ? new List<string>(existing) : [];
        urls.Add(source.Url);
        var reasons = source.Meta.TryGetValue(MetaKeys.RedirectReasons, out var r) && r is List<string> er ? new List<string>(er) : [];
        reasons.Add(reason);
        redirected.Meta[MetaKeys.RedirectTimes] = times;
        redirected.Meta["redirect_ttl"] = ttl - 1;
        redirected.Meta[MetaKeys.RedirectUrls] = urls;
        redirected.Meta[MetaKeys.RedirectReasons] = reasons;

        // Authorization and cookies set for one host must not follow the user to another.
        if (!string.Equals(redirected.Uri.Host, source.Uri.Host, StringComparison.OrdinalIgnoreCase))
        {
            redirected.Headers.Remove("Authorization");
            redirected.Headers.Remove("Cookie");
        }

        var final = redirected with { DontFilter = source.DontFilter, Priority = source.Priority + _priorityAdjust };
        _logger.LogDebug("Redirecting ({Reason}) to {Target} from {Source}", reason, final, source);
        _stats.Inc("downloader/redirect_count");
        if (status > 0) _stats.Inc($"downloader/response_status_count/redirect_{status}");
        return final;
    }
}

/// <summary>Follows <c>&lt;meta http-equiv="refresh"&gt;</c> redirects in HTML (Scrapy's <c>MetaRefreshMiddleware</c>).</summary>
public sealed class MetaRefreshMiddleware : RedirectMiddleware
{
    private static readonly Regex MetaRefresh = new(
        @"<meta[^>]+http-equiv\s*=\s*[""']?refresh[""']?[^>]*content\s*=\s*[""']?\s*(?<delay>\d+(?:\.\d+)?)\s*[;,]?\s*(?:url\s*=\s*)?(?<url>[^""'>]*)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    private readonly double _maxDelay;

    public MetaRefreshMiddleware(Settings settings, IStatsCollector stats, ILogger logger) : base(settings, stats, logger)
    {
        if (!settings.GetBool(SettingKeys.MetaRefreshEnabled, true)) throw new NotConfiguredException();
        _maxDelay = settings.GetDouble(SettingKeys.MetaRefreshMaxDelay, 100);
    }

    public override ValueTask<DownloadResult> ProcessResponseAsync(Request request, Response response, Spider spider)
    {
        if (request.GetMeta(MetaKeys.DontRedirect, false) || request.Method == "HEAD" || response is not HtmlResponse html)
            return (DownloadResult)response;

        // Only the <head> matters; scanning a bounded prefix keeps this cheap on large pages.
        var text = html.Text;
        var head = text.Length > 8192 ? text[..8192] : text;
        var match = MetaRefresh.Match(head);
        if (!match.Success) return (DownloadResult)response;
        var delay = double.Parse(match.Groups["delay"].Value, CultureInfo.InvariantCulture);
        var url = match.Groups["url"].Value.Trim().Trim('\'', '"');
        if (url.Length == 0 || delay >= _maxDelay) return (DownloadResult)response;
        var target = html.UrlJoin(url);
        if (target == request.Url) return (DownloadResult)response;
        return Redirect(request with { Url = target, Method = "GET", Body = [] }, request, "meta refresh", 0);
    }
}
