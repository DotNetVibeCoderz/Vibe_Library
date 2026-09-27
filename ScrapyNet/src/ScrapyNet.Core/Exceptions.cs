namespace ScrapyNet;

/// <summary>Thrown by a downloader middleware to drop a request (Scrapy's <c>IgnoreRequest</c>).</summary>
public class IgnoreRequestException : Exception
{
    public IgnoreRequestException() : base("Request ignored") { }
    public IgnoreRequestException(string message) : base(message) { }
    public IgnoreRequestException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>Thrown by an item pipeline to stop processing an item (Scrapy's <c>DropItem</c>).</summary>
public class DropItemException : Exception
{
    public DropItemException() : base("Item dropped") { }
    public DropItemException(string message) : base(message) { }
    public DropItemException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>Thrown from a spider callback to close the spider with a reason (Scrapy's <c>CloseSpider</c>).</summary>
public class CloseSpiderException : Exception
{
    public CloseSpiderException(string reason = "cancelled") : base(reason) => Reason = reason;
    public string Reason { get; }
}

/// <summary>Thrown from a <c>spider_idle</c> handler to keep the spider open.</summary>
public class DontCloseSpiderException : Exception
{
    public DontCloseSpiderException() : base("Spider kept open") { }
}

/// <summary>A component (middleware, pipeline, extension) throws this from its constructor to disable itself.</summary>
public class NotConfiguredException : Exception
{
    public NotConfiguredException() : base("Component not configured") { }
    public NotConfiguredException(string message) : base(message) { }
}

/// <summary>Non-2xx response filtered by the HTTP error middleware; reaches errbacks as a <see cref="Failure"/>.</summary>
public class HttpErrorException(Response response, string? message = null)
    : Exception(message ?? $"Ignoring non-200 response ({response.Status}) {response.Url}")
{
    public Response Response { get; } = response;
}

/// <summary>Download exceeded <c>DOWNLOAD_TIMEOUT</c>.</summary>
public class DownloadTimeoutException(string message, Exception? inner = null) : TimeoutException(message, inner);

/// <summary>Response body exceeded <c>DOWNLOAD_MAXSIZE</c>.</summary>
public class DownloadSizeExceededException(string message) : IOException(message);

/// <summary>Invalid command-line usage.</summary>
public class UsageException(string message) : Exception(message);

/// <summary>
/// A failed request, as handed to an errback: the exception, the request that failed, and the
/// response when the failure came from one (e.g. an <see cref="HttpErrorException"/>).
/// Named after Twisted's <c>Failure</c>, which Scrapy errbacks receive.
/// </summary>
public sealed class Failure(Exception exception, Request request)
{
    public Exception Exception { get; } = exception;

    public Request Request { get; } = request;

    public Response? Response => Exception is HttpErrorException http ? http.Response : null;

    public string Message => Exception.Message;

    /// <summary>Scrapy's <c>failure.check(SomeError)</c>.</summary>
    public bool Is<TException>() where TException : Exception =>
        Exception is TException || Exception.InnerException is TException;

    public override string ToString() => $"{Exception.GetType().Name}: {Exception.Message} ({Request})";
}

/// <summary>Well-known <see cref="Request.Meta"/> keys understood by built-in components.</summary>
public static class MetaKeys
{
    public const string Depth = "depth";
    public const string Proxy = "proxy";
    public const string DownloadTimeout = "download_timeout";
    public const string DownloadMaxSize = "download_maxsize";
    public const string DownloadWarnSize = "download_warnsize";
    public const string DownloadLatency = "download_latency";
    public const string DownloadSlot = "download_slot";
    public const string DontRetry = "dont_retry";
    public const string MaxRetryTimes = "max_retry_times";
    public const string RetryTimes = "retry_times";
    public const string DontRedirect = "dont_redirect";
    public const string RedirectUrls = "redirect_urls";
    public const string RedirectTimes = "redirect_times";
    public const string RedirectReasons = "redirect_reasons";
    public const string HandleHttpStatusList = "handle_httpstatus_list";
    public const string HandleHttpStatusAll = "handle_httpstatus_all";
    public const string DontMergeCookies = "dont_merge_cookies";
    public const string CookieJar = "cookiejar";
    public const string DontCache = "dont_cache";
    public const string DontObeyRobotsTxt = "dont_obey_robotstxt";
    public const string AutoThrottleDontAdjustDelay = "autothrottle_dont_adjust_delay";
    public const string Referer = "referer";
    public const string Rule = "rule";
    public const string ProxyName = "_proxy_name";
}
