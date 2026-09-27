using System.Globalization;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace ScrapyNet.DownloaderMiddlewares;

/// <summary>
/// Retries requests that failed with a transient error — timeouts, connection failures, DNS errors —
/// or returned one of <c>RETRY_HTTP_CODES</c> (500, 502, 503, 504, 522, 524, 408, 429 by default), up to
/// <c>RETRY_TIMES</c> extra attempts. Port of Scrapy's <c>RetryMiddleware</c>.
/// </summary>
/// <remarks>
/// Retries go back through the scheduler with <c>RETRY_PRIORITY_ADJUST</c> applied to their priority.
/// An optional exponential backoff (<c>RETRY_BACKOFF_BASE</c> seconds, doubled per attempt, capped at
/// <c>RETRY_BACKOFF_MAX</c>) waits before re-queuing, which Scrapy itself does not do but most sites
/// returning 429 expect. Per request: <c>meta["dont_retry"]</c>, <c>meta["max_retry_times"]</c>.
/// </remarks>
public sealed class RetryMiddleware : DownloaderMiddleware
{
    private readonly int _maxRetryTimes;
    private readonly HashSet<int> _retryCodes;
    private readonly int _priorityAdjust;
    private readonly double _backoffBase;
    private readonly double _backoffMax;
    private readonly IStatsCollector _stats;
    private readonly ILogger _logger;

    public RetryMiddleware(Settings settings, IStatsCollector stats, ILogger logger)
    {
        if (!settings.GetBool(SettingKeys.RetryEnabled, true)) throw new NotConfiguredException();
        _maxRetryTimes = settings.GetInt(SettingKeys.RetryTimes, 2);
        _retryCodes = [.. settings.GetIntList(SettingKeys.RetryHttpCodes, DefaultSettings.RetryHttpCodes)];
        _priorityAdjust = settings.GetInt(SettingKeys.RetryPriorityAdjust, -1);
        _backoffBase = settings.GetDouble(SettingKeys.RetryBackoffBase);
        _backoffMax = settings.GetDouble(SettingKeys.RetryBackoffMax, 60);
        _stats = stats;
        _logger = logger;
    }

    public override async ValueTask<DownloadResult> ProcessResponseAsync(Request request, Response response, Spider spider)
    {
        if (request.GetMeta(MetaKeys.DontRetry, false)) return response;
        if (!_retryCodes.Contains(response.Status)) return response;
        var retry = await RetryAsync(request, $"{response.Status} {ReasonPhrase(response.Status)}").ConfigureAwait(false);
        return retry is null ? (DownloadResult)response : retry;
    }

    public override async ValueTask<DownloadResult> ProcessExceptionAsync(Request request, Exception exception, Spider spider)
    {
        if (!IsRetryable(exception) || request.GetMeta(MetaKeys.DontRetry, false)) return DownloadResult.Continue;
        var retry = await RetryAsync(request, exception.GetType().FullName ?? exception.GetType().Name).ConfigureAwait(false);
        return retry is null ? DownloadResult.Continue : retry;
    }

    private async ValueTask<Request?> RetryAsync(Request request, string reason)
    {
        var retry = GetRetryRequest(request, reason, _maxRetryTimes, _priorityAdjust, _stats, _logger);
        if (retry is not null && _backoffBase > 0)
        {
            var attempt = retry.GetMeta<int>(MetaKeys.RetryTimes);
            var delay = Math.Min(_backoffMax, _backoffBase * Math.Pow(2, attempt - 1));
            await Task.Delay(TimeSpan.FromSeconds(delay)).ConfigureAwait(false);
        }
        return retry;
    }

    /// <summary>Exceptions worth retrying: network-level failures and timeouts.</summary>
    public static bool IsRetryable(Exception exception) => exception switch
    {
        IgnoreRequestException or DownloadSizeExceededException => false,
        TimeoutException or HttpRequestException or SocketException or IOException => true,
        TaskCanceledException => true,
        _ => exception.InnerException is not null && IsRetryable(exception.InnerException),
    };

    /// <summary>
    /// Builds the retry of <paramref name="request"/>, or returns <c>null</c> when retries are exhausted.
    /// Usable from spider callbacks too (Scrapy's <c>get_retry_request</c>), e.g. to retry a page whose
    /// content is incomplete even though it returned 200.
    /// </summary>
    public static Request? GetRetryRequest(Request request, string reason, int maxRetryTimes = 2, int priorityAdjust = -1,
        IStatsCollector? stats = null, ILogger? logger = null)
    {
        var retries = request.GetMeta<int>(MetaKeys.RetryTimes) + 1;
        var max = request.Meta.TryGetValue(MetaKeys.MaxRetryTimes, out var m) && m is not null ? Convert.ToInt32(m, CultureInfo.InvariantCulture) : maxRetryTimes;
        if (retries <= max)
        {
            logger?.LogDebug("Retrying {Request} (failed {Times} times): {Reason}", request, retries, reason);
            var retry = request with { DontFilter = true, Priority = request.Priority + priorityAdjust };
            retry.Meta[MetaKeys.RetryTimes] = retries;
            stats?.Inc("retry/count");
            stats?.Inc($"retry/reason_count/{reason}");
            return retry;
        }
        stats?.Inc("retry/max_reached");
        logger?.LogError("Gave up retrying {Request} (failed {Times} times): {Reason}", request, retries, reason);
        return null;
    }

    private static string ReasonPhrase(int status) => status switch
    {
        408 => "Request Timeout",
        429 => "Too Many Requests",
        500 => "Internal Server Error",
        502 => "Bad Gateway",
        503 => "Service Unavailable",
        504 => "Gateway Timeout",
        522 => "Connection Timed Out",
        524 => "A Timeout Occurred",
        _ => "",
    };
}
