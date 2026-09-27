using ScrapyNet.DownloaderMiddlewares;
using ScrapyNet.Extensions;
using ScrapyNet.SpiderMiddlewares;

namespace ScrapyNet;

/// <summary>Setting names. They match Scrapy's so its documentation applies unchanged.</summary>
public static class SettingKeys
{
    public const string BotName = "BOT_NAME";
    public const string ConcurrentItems = "CONCURRENT_ITEMS";
    public const string ConcurrentRequests = "CONCURRENT_REQUESTS";
    public const string ConcurrentRequestsPerDomain = "CONCURRENT_REQUESTS_PER_DOMAIN";
    public const string ConcurrentRequestsPerIp = "CONCURRENT_REQUESTS_PER_IP";
    public const string CookiesEnabled = "COOKIES_ENABLED";
    public const string CookiesDebug = "COOKIES_DEBUG";
    public const string CookiesPersistFile = "COOKIES_PERSIST_FILE";
    public const string DefaultRequestHeaders = "DEFAULT_REQUEST_HEADERS";
    public const string DepthLimit = "DEPTH_LIMIT";
    public const string DepthPriority = "DEPTH_PRIORITY";
    public const string DepthStatsVerbose = "DEPTH_STATS_VERBOSE";
    public const string DownloadDelay = "DOWNLOAD_DELAY";
    public const string DownloadHandlers = "DOWNLOAD_HANDLERS";
    public const string DownloadTimeout = "DOWNLOAD_TIMEOUT";
    public const string DownloadMaxSize = "DOWNLOAD_MAXSIZE";
    public const string DownloadWarnSize = "DOWNLOAD_WARNSIZE";
    public const string DownloadSlots = "DOWNLOAD_SLOTS";
    public const string DownloadVerifySsl = "DOWNLOAD_VERIFY_SSL";
    public const string DownloadHttp2 = "DOWNLOAD_HTTP2";
    public const string DownloaderMiddlewares = "DOWNLOADER_MIDDLEWARES";
    public const string DomainHeaders = "DOMAIN_HEADERS";
    public const string DupeFilterClass = "DUPEFILTER_CLASS";
    public const string DupeFilterDebug = "DUPEFILTER_DEBUG";
    public const string Extensions = "EXTENSIONS";
    public const string Feeds = "FEEDS";
    public const string FeedExportEncoding = "FEED_EXPORT_ENCODING";
    public const string FeedExportFields = "FEED_EXPORT_FIELDS";
    public const string FeedExportIndent = "FEED_EXPORT_INDENT";
    public const string FeedExportBatchItemCount = "FEED_EXPORT_BATCH_ITEM_COUNT";
    public const string FeedStoreEmpty = "FEED_STORE_EMPTY";
    public const string FilesStore = "FILES_STORE";
    public const string FilesExpires = "FILES_EXPIRES";
    public const string FilesUrlsField = "FILES_URLS_FIELD";
    public const string FilesResultField = "FILES_RESULT_FIELD";
    public const string ImagesStore = "IMAGES_STORE";
    public const string ImagesExpires = "IMAGES_EXPIRES";
    public const string ImagesUrlsField = "IMAGES_URLS_FIELD";
    public const string ImagesResultField = "IMAGES_RESULT_FIELD";
    public const string ImagesMinWidth = "IMAGES_MIN_WIDTH";
    public const string ImagesMinHeight = "IMAGES_MIN_HEIGHT";
    public const string ImagesThumbs = "IMAGES_THUMBS";
    public const string ImagesProcessor = "IMAGES_PROCESSOR";
    public const string MediaAllowRedirects = "MEDIA_ALLOW_REDIRECTS";
    public const string HttpCacheEnabled = "HTTPCACHE_ENABLED";
    public const string HttpCacheDir = "HTTPCACHE_DIR";
    public const string HttpCacheExpirationSecs = "HTTPCACHE_EXPIRATION_SECS";
    public const string HttpCacheIgnoreHttpCodes = "HTTPCACHE_IGNORE_HTTP_CODES";
    public const string HttpCacheIgnoreMissing = "HTTPCACHE_IGNORE_MISSING";
    public const string HttpCacheIgnoreSchemes = "HTTPCACHE_IGNORE_SCHEMES";
    public const string HttpCachePolicy = "HTTPCACHE_POLICY";
    public const string HttpErrorAllowedCodes = "HTTPERROR_ALLOWED_CODES";
    public const string HttpErrorAllowAll = "HTTPERROR_ALLOW_ALL";
    public const string HttpProxyEnabled = "HTTPPROXY_ENABLED";
    public const string HttpUser = "HTTP_USER";
    public const string HttpPass = "HTTP_PASS";
    public const string HttpBearerToken = "HTTP_BEARER_TOKEN";
    public const string HttpAuthDomain = "HTTP_AUTH_DOMAIN";
    public const string ItemPipelines = "ITEM_PIPELINES";
    public const string JobDir = "JOBDIR";
    public const string LogEnabled = "LOG_ENABLED";
    public const string LogLevel = "LOG_LEVEL";
    public const string LogFile = "LOG_FILE";
    public const string LogFileAppend = "LOG_FILE_APPEND";
    public const string LogStdout = "LOG_STDOUT";
    public const string LogShortNames = "LOG_SHORT_NAMES";
    public const string LogStatsInterval = "LOGSTATS_INTERVAL";
    public const string MemUsageEnabled = "MEMUSAGE_ENABLED";
    public const string MemUsageLimitMb = "MEMUSAGE_LIMIT_MB";
    public const string MemUsageWarningMb = "MEMUSAGE_WARNING_MB";
    public const string MemUsageCheckIntervalSeconds = "MEMUSAGE_CHECK_INTERVAL_SECONDS";
    public const string MetaRefreshEnabled = "METAREFRESH_ENABLED";
    public const string MetaRefreshMaxDelay = "METAREFRESH_MAXDELAY";
    public const string ProxyList = "PROXY_LIST";
    public const string ProxyPerDomain = "PROXY_PER_DOMAIN";
    public const string ProxyBanStatusCodes = "PROXY_BAN_STATUS_CODES";
    public const string ProxyBanCooldown = "PROXY_BAN_COOLDOWN";
    public const string RandomizeDownloadDelay = "RANDOMIZE_DOWNLOAD_DELAY";
    public const string RedirectEnabled = "REDIRECT_ENABLED";
    public const string RedirectMaxTimes = "REDIRECT_MAX_TIMES";
    public const string RedirectPriorityAdjust = "REDIRECT_PRIORITY_ADJUST";
    public const string RefererEnabled = "REFERER_ENABLED";
    public const string RefererPolicy = "REFERRER_POLICY";
    public const string RequestFingerprinterIncludeHeaders = "REQUEST_FINGERPRINTER_INCLUDE_HEADERS";
    public const string RetryEnabled = "RETRY_ENABLED";
    public const string RetryTimes = "RETRY_TIMES";
    public const string RetryHttpCodes = "RETRY_HTTP_CODES";
    public const string RetryPriorityAdjust = "RETRY_PRIORITY_ADJUST";
    public const string RetryBackoffBase = "RETRY_BACKOFF_BASE";
    public const string RetryBackoffMax = "RETRY_BACKOFF_MAX";
    public const string RobotsTxtObey = "ROBOTSTXT_OBEY";
    public const string RobotsTxtUserAgent = "ROBOTSTXT_USER_AGENT";
    public const string Scheduler = "SCHEDULER";
    public const string SchedulerDebug = "SCHEDULER_DEBUG";
    public const string SchedulerOrder = "SCHEDULER_ORDER";
    public const string SpiderMiddlewares = "SPIDER_MIDDLEWARES";
    public const string StatsClass = "STATS_CLASS";
    public const string StatsDump = "STATS_DUMP";
    public const string UrlLengthLimit = "URLLENGTH_LIMIT";
    public const string UserAgent = "USER_AGENT";
    public const string UserAgentList = "USER_AGENT_LIST";
    public const string AutoThrottleEnabled = "AUTOTHROTTLE_ENABLED";
    public const string AutoThrottleStartDelay = "AUTOTHROTTLE_START_DELAY";
    public const string AutoThrottleMaxDelay = "AUTOTHROTTLE_MAX_DELAY";
    public const string AutoThrottleTargetConcurrency = "AUTOTHROTTLE_TARGET_CONCURRENCY";
    public const string AutoThrottleDebug = "AUTOTHROTTLE_DEBUG";
    public const string CloseSpiderTimeout = "CLOSESPIDER_TIMEOUT";
    public const string CloseSpiderTimeoutNoItem = "CLOSESPIDER_TIMEOUT_NO_ITEM";
    public const string CloseSpiderItemCount = "CLOSESPIDER_ITEMCOUNT";
    public const string CloseSpiderPageCount = "CLOSESPIDER_PAGECOUNT";
    public const string CloseSpiderErrorCount = "CLOSESPIDER_ERRORCOUNT";
    public const string ScraperSlotMaxActiveSize = "SCRAPER_SLOT_MAX_ACTIVE_SIZE";
}

/// <summary>Scrapy.Net's default settings, applied at <see cref="SettingPriority.Default"/>.</summary>
public static class DefaultSettings
{
    public const string DefaultUserAgent = "Scrapy.Net/0.1 (+https://github.com/DotNetVibeCoderz/Vibe_Library/tree/main/ScrapyNet)";

    /// <summary>Scrapy's <c>RETRY_HTTP_CODES</c>.</summary>
    public static readonly IReadOnlyList<int> RetryHttpCodes = [500, 502, 503, 504, 522, 524, 408, 429];

    public static void ApplyTo(Settings s)
    {
        const SettingPriority p = SettingPriority.Default;
        s.Set(SettingKeys.BotName, "scrapynet", p);
        s.Set(SettingKeys.ConcurrentItems, 100, p);
        s.Set(SettingKeys.ConcurrentRequests, 16, p);
        s.Set(SettingKeys.ConcurrentRequestsPerDomain, 8, p);
        s.Set(SettingKeys.ConcurrentRequestsPerIp, 0, p);
        s.Set(SettingKeys.CookiesEnabled, true, p);
        s.Set(SettingKeys.CookiesDebug, false, p);
        s.Set(SettingKeys.DefaultRequestHeaders, new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Accept"] = "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8",
            ["Accept-Language"] = "en",
        }, p);
        s.Set(SettingKeys.DepthLimit, 0, p);
        s.Set(SettingKeys.DepthPriority, 0, p);
        s.Set(SettingKeys.DepthStatsVerbose, false, p);
        s.Set(SettingKeys.DownloadDelay, 0.0, p);
        s.Set(SettingKeys.DownloadTimeout, 180, p);
        s.Set(SettingKeys.DownloadMaxSize, 1024L * 1024 * 1024, p);
        s.Set(SettingKeys.DownloadWarnSize, 32L * 1024 * 1024, p);
        s.Set(SettingKeys.DownloadVerifySsl, true, p);
        s.Set(SettingKeys.DownloadHttp2, false, p);
        s.Set(SettingKeys.DupeFilterDebug, false, p);
        s.Set(SettingKeys.FeedExportEncoding, "utf-8", p);
        s.Set(SettingKeys.FeedExportIndent, 0, p);
        s.Set(SettingKeys.FeedExportBatchItemCount, 0, p);
        s.Set(SettingKeys.FeedStoreEmpty, true, p);
        s.Set(SettingKeys.FilesExpires, 90, p);
        s.Set(SettingKeys.ImagesExpires, 90, p);
        s.Set(SettingKeys.ImagesMinWidth, 0, p);
        s.Set(SettingKeys.ImagesMinHeight, 0, p);
        s.Set(SettingKeys.MediaAllowRedirects, false, p);
        s.Set(SettingKeys.HttpCacheEnabled, false, p);
        s.Set(SettingKeys.HttpCacheDir, "httpcache", p);
        s.Set(SettingKeys.HttpCacheExpirationSecs, 0, p);
        s.Set(SettingKeys.HttpCacheIgnoreHttpCodes, new List<int>(), p);
        s.Set(SettingKeys.HttpCacheIgnoreMissing, false, p);
        s.Set(SettingKeys.HttpCacheIgnoreSchemes, "file", p);
        s.Set(SettingKeys.HttpCachePolicy, "dummy", p);
        s.Set(SettingKeys.HttpErrorAllowAll, false, p);
        s.Set(SettingKeys.HttpProxyEnabled, true, p);
        s.Set(SettingKeys.LogEnabled, true, p);
        s.Set(SettingKeys.LogLevel, "INFO", p);
        s.Set(SettingKeys.LogFileAppend, true, p);
        s.Set(SettingKeys.LogShortNames, false, p);
        s.Set(SettingKeys.LogStatsInterval, 60.0, p);
        s.Set(SettingKeys.MemUsageEnabled, false, p);
        s.Set(SettingKeys.MemUsageLimitMb, 0, p);
        s.Set(SettingKeys.MemUsageWarningMb, 0, p);
        s.Set(SettingKeys.MemUsageCheckIntervalSeconds, 60.0, p);
        s.Set(SettingKeys.MetaRefreshEnabled, true, p);
        s.Set(SettingKeys.MetaRefreshMaxDelay, 100, p);
        s.Set(SettingKeys.ProxyBanStatusCodes, new List<int> { 403, 407, 429 }, p);
        s.Set(SettingKeys.ProxyBanCooldown, 300.0, p);
        s.Set(SettingKeys.RandomizeDownloadDelay, true, p);
        s.Set(SettingKeys.RedirectEnabled, true, p);
        s.Set(SettingKeys.RedirectMaxTimes, 20, p);
        s.Set(SettingKeys.RedirectPriorityAdjust, 2, p);
        s.Set(SettingKeys.RefererEnabled, true, p);
        s.Set(SettingKeys.RefererPolicy, "no-referrer-when-downgrade", p);
        s.Set(SettingKeys.RetryEnabled, true, p);
        s.Set(SettingKeys.RetryTimes, 2, p);
        s.Set(SettingKeys.RetryHttpCodes, RetryHttpCodes.ToList(), p);
        s.Set(SettingKeys.RetryPriorityAdjust, -1, p);
        s.Set(SettingKeys.RetryBackoffBase, 0.0, p);
        s.Set(SettingKeys.RetryBackoffMax, 60.0, p);
        s.Set(SettingKeys.RobotsTxtObey, false, p);
        s.Set(SettingKeys.SchedulerDebug, false, p);
        s.Set(SettingKeys.SchedulerOrder, "lifo", p);
        s.Set(SettingKeys.StatsDump, true, p);
        s.Set(SettingKeys.UrlLengthLimit, 2083, p);
        s.Set(SettingKeys.UserAgent, DefaultUserAgent, p);
        s.Set(SettingKeys.AutoThrottleEnabled, false, p);
        s.Set(SettingKeys.AutoThrottleStartDelay, 5.0, p);
        s.Set(SettingKeys.AutoThrottleMaxDelay, 60.0, p);
        s.Set(SettingKeys.AutoThrottleTargetConcurrency, 1.0, p);
        s.Set(SettingKeys.AutoThrottleDebug, false, p);
        s.Set(SettingKeys.CloseSpiderTimeout, 0.0, p);
        s.Set(SettingKeys.CloseSpiderTimeoutNoItem, 0.0, p);
        s.Set(SettingKeys.CloseSpiderItemCount, 0, p);
        s.Set(SettingKeys.CloseSpiderPageCount, 0, p);
        s.Set(SettingKeys.CloseSpiderErrorCount, 0, p);
        s.Set(SettingKeys.ScraperSlotMaxActiveSize, 5_000_000L, p);

        // Orders follow Scrapy's DOWNLOADER_MIDDLEWARES_BASE so custom middlewares slot in at the
        // same numbers Scrapy's documentation recommends.
        s.DownloaderMiddlewares
            .Add<OffsiteDownloaderMiddleware>(50)
            .Add<RobotsTxtMiddleware>(100)
            .Add<HttpAuthMiddleware>(300)
            .Add<DownloadTimeoutMiddleware>(350)
            .Add<DefaultHeadersMiddleware>(400)
            .Add<DomainHeadersMiddleware>(450)
            .Add<UserAgentMiddleware>(500)
            .Add<RetryMiddleware>(550)
            .Add<MetaRefreshMiddleware>(580)
            .Add<RedirectMiddleware>(600)
            .Add<CookiesMiddleware>(700)
            .Add<HttpProxyMiddleware>(750)
            .Add<DownloaderStatsMiddleware>(850)
            .Add<HttpCacheMiddleware>(900);

        s.SpiderMiddlewares
            .Add<HttpErrorMiddleware>(50)
            .Add<OffsiteMiddleware>(500)
            .Add<RefererMiddleware>(700)
            .Add<UrlLengthMiddleware>(800)
            .Add<DepthMiddleware>(900);

        s.Extensions
            .Add<CoreStats>(0)
            .Add<LogStats>(0)
            .Add<MemoryUsage>(0)
            .Add<CloseSpiderExtension>(0)
            .Add<FeedExporter>(0)
            .Add<SpiderStateExtension>(0)
            .Add<AutoThrottle>(0);

        _ = s.ItemPipelines;
        _ = s.Feeds;
    }
}
