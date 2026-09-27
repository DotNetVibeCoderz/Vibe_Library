# 6. Settings and middlewares

## Settings

Settings use Scrapy's names, so Scrapy's documentation applies. Every value carries a priority, and a
value only replaces one of equal or lower priority:

| Priority | Set by |
|---|---|
| `Default` (0) | Scrapy.Net defaults |
| `Command` (10) | The command being run |
| `Project` (20) | `scrapy.json`, your code |
| `Spider` (30) | `Spider.ConfigureSettings` |
| `Cmdline` (40) | `-s KEY=VALUE` |

```csharp
var settings = new Settings();                                        // defaults loaded
settings.Set(SettingKeys.ConcurrentRequests, 32);
settings[SettingKeys.UserAgent] = "MyBot/1.0 (+https://example.com/bot)";
settings.LoadProjectFiles(".");                                       // scrapy.json + scrapy.{SCRAPYNET_ENV}.json
settings.LoadEnvironmentVariables();                                  // SCRAPYNET_SETTINGS_KEY=value
settings.GetInt(SettingKeys.ConcurrentRequests);                      // typed getters
```

`scrapy.json` (comments and trailing commas allowed):

```json
{
  "BOT_NAME": "bookshop",
  "ROBOTSTXT_OBEY": true,
  "DOWNLOAD_DELAY": 1,
  "ITEM_PIPELINES": { "Bookshop.PricePipeline": 300 },
  "DOWNLOADER_MIDDLEWARES": { "RetryMiddleware": null },
  "FEEDS": { "output/items.jsonl": { "format": "jsonlines", "overwrite": true } }
}
```

Environment files keep dev and prod apart: `scrapy.dev.json`, `scrapy.prod.json`, selected with
`SCRAPYNET_ENV` or `--env`. Settings freeze when a crawler starts.

### Reference

| Setting | Default | Meaning |
|---|---|---|
| `BOT_NAME` | `scrapynet` | Project name |
| `USER_AGENT` | `Scrapy.Net/0.1 (+…)` | Default User-Agent |
| `CONCURRENT_REQUESTS` | 16 | Requests in flight overall |
| `CONCURRENT_REQUESTS_PER_DOMAIN` | 8 | Per slot (host) |
| `CONCURRENT_REQUESTS_PER_IP` | 0 | If > 0, slots are per IP and this is the limit |
| `CONCURRENT_ITEMS` | 100 | Items in pipelines at once |
| `DOWNLOAD_DELAY` | 0 | Seconds between downloads in a slot |
| `RANDOMIZE_DOWNLOAD_DELAY` | true | Delay × 0.5–1.5 |
| `DOWNLOAD_SLOTS` | {} | Per-slot `{concurrency, delay, randomize_delay}` |
| `DOWNLOAD_TIMEOUT` | 180 | Seconds |
| `DOWNLOAD_MAXSIZE` / `DOWNLOAD_WARNSIZE` | 1 GiB / 32 MiB | Response size limits |
| `DOWNLOAD_VERIFY_SSL` | true | Certificate validation |
| `DOWNLOAD_HTTP2` | false | Prefer HTTP/2 |
| `DEFAULT_REQUEST_HEADERS` | Accept, Accept-Language | Added when missing |
| `DOMAIN_HEADERS` | {} | Headers per host (API keys that must not leak) |
| `COOKIES_ENABLED` / `COOKIES_DEBUG` / `COOKIES_PERSIST_FILE` | true / false / – | Cookie handling |
| `DEPTH_LIMIT` / `DEPTH_PRIORITY` / `DEPTH_STATS_VERBOSE` | 0 / 0 / false | Link depth control |
| `SCHEDULER_ORDER` | `lifo` | `lifo` (depth-first) or `fifo` (breadth-first) within a priority |
| `JOBDIR` | – | Pause/resume folder |
| `RETRY_ENABLED` / `RETRY_TIMES` / `RETRY_HTTP_CODES` | true / 2 / 500,502,503,504,522,524,408,429 | Retries |
| `RETRY_PRIORITY_ADJUST` / `RETRY_BACKOFF_BASE` / `RETRY_BACKOFF_MAX` | -1 / 0 / 60 | Retry scheduling |
| `REDIRECT_ENABLED` / `REDIRECT_MAX_TIMES` | true / 20 | Redirects |
| `METAREFRESH_ENABLED` / `METAREFRESH_MAXDELAY` | true / 100 | `<meta http-equiv="refresh">` |
| `ROBOTSTXT_OBEY` / `ROBOTSTXT_USER_AGENT` | false / – | robots.txt |
| `HTTPERROR_ALLOWED_CODES` / `HTTPERROR_ALLOW_ALL` | [] / false | Non-2xx passed to callbacks |
| `HTTPPROXY_ENABLED` | true | Honour `http_proxy`/`https_proxy`/`no_proxy` |
| `PROXY_LIST` / `PROXY_PER_DOMAIN` / `PROXY_BAN_STATUS_CODES` / `PROXY_BAN_COOLDOWN` | – / – / 403,407,429 / 300 | Proxy rotation |
| `HTTP_USER` / `HTTP_PASS` / `HTTP_BEARER_TOKEN` / `HTTP_AUTH_DOMAIN` | – | Authentication, scoped to one domain |
| `HTTPCACHE_ENABLED` / `HTTPCACHE_DIR` / `HTTPCACHE_EXPIRATION_SECS` / `HTTPCACHE_POLICY` | false / httpcache / 0 / dummy | HTTP cache |
| `AUTOTHROTTLE_ENABLED` / `_START_DELAY` / `_MAX_DELAY` / `_TARGET_CONCURRENCY` / `_DEBUG` | false / 5 / 60 / 1.0 / false | AutoThrottle |
| `CLOSESPIDER_TIMEOUT` / `_ITEMCOUNT` / `_PAGECOUNT` / `_ERRORCOUNT` / `_TIMEOUT_NO_ITEM` | 0 | Stop conditions |
| `FEEDS` and `FEED_EXPORT_*` | – | Feed exports |
| `FILES_STORE`, `IMAGES_STORE`, `FILES_EXPIRES`, `IMAGES_MIN_WIDTH`, … | – | Media pipelines |
| `LOG_ENABLED` / `LOG_LEVEL` / `LOG_FILE` / `LOGSTATS_INTERVAL` | true / INFO / – / 60 | Logging |
| `MEMUSAGE_ENABLED` / `MEMUSAGE_LIMIT_MB` / `MEMUSAGE_WARNING_MB` | false / 0 / 0 | Memory guard |
| `STATS_DUMP` | true | Log stats when the spider closes |
| `URLLENGTH_LIMIT` | 2083 | Longer URLs are dropped |
| `SCHEDULER`, `DUPEFILTER_CLASS`, `STATS_CLASS` | – | Replace core components |

`LOG_LEVEL` defaults to `INFO` (Scrapy's is `DEBUG`); set `DEBUG` to see every crawled URL.

## Downloader middlewares

Enabled by default, in order:

| Order | Middleware | Does |
|---|---|---|
| 50 | `OffsiteDownloaderMiddleware` | Drops requests outside `AllowedDomains` (start requests too) |
| 100 | `RobotsTxtMiddleware` | Fetches and obeys robots.txt (`ROBOTSTXT_OBEY`) |
| 300 | `HttpAuthMiddleware` | Basic or Bearer auth for one domain |
| 350 | `DownloadTimeoutMiddleware` | Per-request timeout meta |
| 400 | `DefaultHeadersMiddleware` | `DEFAULT_REQUEST_HEADERS` |
| 450 | `DomainHeadersMiddleware` | `DOMAIN_HEADERS` |
| 500 | `UserAgentMiddleware` | `USER_AGENT` |
| 550 | `RetryMiddleware` | Retries transient failures and retryable statuses |
| 580 | `MetaRefreshMiddleware` | Follows meta refresh |
| 600 | `RedirectMiddleware` | Follows 3xx through the scheduler |
| 700 | `CookiesMiddleware` | Cookie jars (`meta["cookiejar"]` for several sessions) |
| 750 | `HttpProxyMiddleware` | `meta["proxy"]` and proxy environment variables |
| 850 | `DownloaderStatsMiddleware` | `downloader/*` stats |
| 900 | `HttpCacheMiddleware` | Disk cache (`HTTPCACHE_ENABLED`) |

Available, not enabled: `RandomUserAgentMiddleware` (add at 400), `ProxyRotationMiddleware` (740),
`IncrementalRecrawlMiddleware` (AI package, 950).

### Writing one

```csharp
public sealed class ApiKeyMiddleware(Settings settings) : DownloaderMiddleware
{
    private readonly string _key = settings.GetString("MY_API_KEY") ?? throw new NotConfiguredException("MY_API_KEY missing");

    public override ValueTask<DownloadResult> ProcessRequestAsync(Request request, Spider spider)
    {
        request.Headers.SetDefault("X-Api-Key", _key);
        return DownloadResult.Continue;                   // or return a Response to skip the download,
    }                                                    // or a Request to reschedule

    public override async ValueTask<DownloadResult> ProcessResponseAsync(Request request, Response response, Spider spider)
    {
        if (response.Status == 401) throw new IgnoreRequestException("API key rejected");
        return response;
    }
}

settings.DownloaderMiddlewares.Add<ApiKeyMiddleware>(543);
settings.DownloaderMiddlewares.Disable<RetryMiddleware>();
```

`ProcessRequestAsync` runs in ascending order, `ProcessResponseAsync` and `ProcessExceptionAsync` in
descending order. Returning a `Request` from any of them sends that request back to the scheduler.

## Spider middlewares

| Order | Middleware | Does |
|---|---|---|
| 50 | `HttpErrorMiddleware` | Filters non-2xx responses (errbacks receive `HttpErrorException`) |
| 500 | `OffsiteMiddleware` | Drops offsite requests yielded by callbacks |
| 700 | `RefererMiddleware` | Sets `Referer` per `REFERRER_POLICY` |
| 800 | `UrlLengthMiddleware` | Drops URLs over `URLLENGTH_LIMIT` |
| 900 | `DepthMiddleware` | Tracks `depth`, applies `DEPTH_LIMIT` and `DEPTH_PRIORITY` |

```csharp
public sealed class DropPromoLinks : SpiderMiddleware
{
    public override async IAsyncEnumerable<object> ProcessSpiderOutput(Response response, IAsyncEnumerable<object> result, Spider spider)
    {
        await foreach (var x in result)
            if (x is not Request r || !r.Url.Contains("/promo/")) yield return x;
    }
}
```

## Extensions

Any class registered in `settings.Extensions` is created with the crawler and usually connects to
signals in its constructor. Built in: `CoreStats`, `LogStats`, `MemoryUsage`, `CloseSpiderExtension`,
`FeedExporter`, `SpiderStateExtension`, `AutoThrottle`; the platform package adds
`RawContentExtension` and `LoginRedirectDetector`.
