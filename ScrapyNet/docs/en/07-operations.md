# 7. Operating crawls

## Retries, redirects and errors

![Retries, redirects and errors](../images/gallery-retries.png)

`RetryMiddleware` retries timeouts, connection failures, DNS errors and `RETRY_HTTP_CODES` up to
`RETRY_TIMES` extra attempts, with `RETRY_PRIORITY_ADJUST`. Scrapy.Net adds an optional exponential
backoff (`RETRY_BACKOFF_BASE`, capped by `RETRY_BACKOFF_MAX`) that sites answering 429 appreciate.
Per request: `meta["dont_retry"]`, `meta["max_retry_times"]`. From a callback, retry a page that
loaded but is incomplete:

```csharp
if (response.Css(".price").Count == 0 &&
    RetryMiddleware.GetRetryRequest(response.Request, "missing prices") is { } retry)
    yield return retry;
```

Redirects (301/302/303/307/308 and meta refresh) are scheduled as new requests, so they pass the
duplicate filter and appear in stats; `meta["redirect_urls"]` keeps the chain. Authorization and
cookie headers are removed when a redirect changes host.

## Proxies

```csharp
// one request
yield return new Request(url) { Meta = { ["proxy"] = "http://user:pass@proxy.example:8080" } };

// rotation with ban detection
settings.Set(SettingKeys.ProxyList, new List<string> { "http://p1:8080", "http://p2:8080", "http://p3:8080" });
settings.Set(SettingKeys.ProxyPerDomain, new Dictionary<string, object?> { ["api.example.com"] = "http://static-ip:8080" });
settings.DownloaderMiddlewares.Add<ProxyRotationMiddleware>(740);
```

A proxy answering a ban status (`PROXY_BAN_STATUS_CODES`) or failing to connect is benched for
`PROXY_BAN_COOLDOWN` seconds and the request is rerouted. Stats: `proxies/used/...`, `proxies/banned`,
`proxies/rerouted`. Proxy credentials are never logged.

## AutoThrottle

![AutoThrottle](../images/gallery-autothrottle.png)

```csharp
settings.Set(SettingKeys.AutoThrottleEnabled, true);
settings.Set(SettingKeys.AutoThrottleTargetConcurrency, 2.0);
```

Each response's latency moves the slot delay toward `latency / AUTOTHROTTLE_TARGET_CONCURRENCY`,
bounded by `DOWNLOAD_DELAY` and `AUTOTHROTTLE_MAX_DELAY`. Error responses never shorten the delay.
`AutoThrottle.History` exposes the adjustments for charts.

## HTTP cache

```csharp
settings.Set(SettingKeys.HttpCacheEnabled, true);           // turn on while developing selectors
settings.Set(SettingKeys.HttpCacheExpirationSecs, 3600);
```

Responses are stored under `HTTPCACHE_DIR/<spider>/<fp[0:2]>/<fp>/`. The `dummy` policy caches
everything; `rfc2616` respects `Cache-Control`, `Expires` and `Last-Modified`. Replayed responses
carry the `cached` flag. `HTTPCACHE_IGNORE_MISSING` makes offline replays fail fast instead of
downloading.

## Pause and resume

```bash
dotnet run -- crawl books -s JOBDIR=crawls/books-1
# Ctrl+C once, a crash or a kill: the queue is already on disk
dotnet run -- crawl books -s JOBDIR=crawls/books-1     # continues
```

`JOBDIR` holds `requests.queue.log` (the disk queue), `requests.seen` (fingerprints) and `spider.state`
(`Spider.State`). With `JOBDIR`, each request is written to the queue file as it is scheduled and read
back when it is dequeued; memory keeps only its position, so a frontier of millions of URLs costs a few
dozen bytes per pending request. Both files are flushed every second, so even a crash or `kill -9`
loses at most the last second of scheduling. Popped entries are reclaimed by rewriting the file once
more than half of it is dead. Stats: `scheduler/enqueued/disk`, `scheduler/dequeued/disk`,
`scheduler/compactions`.

Only requests whose callbacks are named spider methods go to disk; others stay in memory, are counted
in `scheduler/unserializable`, and are lost if the crawl stops early. Meta values must be
JSON-serializable. A `requests.queue` left by Scrapy.Net 0.1 is read once and moved into the journal.

To share one queue between several processes or machines, see
[Distributed crawling with Redis](16-distributed-crawling.md).

## HTTP/2 and HTTP/3

```csharp
settings.Set(SettingKeys.DownloadHttpVersion, "3");          // QUIC where the server offers it
settings.Set(SettingKeys.DownloadHttpVersionPolicy, "exact"); // or "or-lower" (default)
```

With the default `or-lower` policy, the first request to a server goes over TCP and later ones switch
to HTTP/3 once the server advertises it in `Alt-Svc`; `exact` connects over QUIC straight away and fails
if the server doesn't speak it. `response.Protocol` says what was used (`HTTP/3.0`). QUIC needs
`libmsquic` on Linux and Windows 11 / Server 2022 or later; without it Scrapy.Net logs a warning once
and uses HTTP/2. Requests through a proxy stay on HTTP/1.1 or HTTP/2.

## Stopping conditions

```csharp
settings.Set(SettingKeys.CloseSpiderItemCount, 1000);
settings.Set(SettingKeys.CloseSpiderTimeout, 600);          // seconds
settings.Set(SettingKeys.CloseSpiderTimeoutNoItem, 120);    // stuck crawls
settings.Set(SettingKeys.CloseSpiderErrorCount, 50);
```

From code: `await crawler.StopAsync()` (graceful), `crawler.Kill()` (immediate), `crawler.Pause()`
and `crawler.Resume()`; or cancel the token passed to `CrawlAsync`.

## Signals

```csharp
var crawler = new Crawler(new BooksSpider(), settings);
crawler.Signals.Connect(Signals.ItemScraped, e => Console.WriteLine($"item from {e.Response?.Url}"));
crawler.Signals.Connect(Signals.SpiderClosed, e => Console.WriteLine($"closed: {e.Reason}"));
crawler.Signals.Connect(Signals.SpiderIdle, e =>
{
    if (queue.TryDequeue(out var url)) { _ = crawler.Engine.CrawlAsync(new Request(url)); throw new DontCloseSpiderException(); }
});   // keep the spider alive while external work remains
await crawler.CrawlAsync();
```

Available: `EngineStarted`, `EngineStopped`, `SpiderOpened`, `SpiderIdle`, `SpiderClosed`,
`SpiderError`, `RequestScheduled`, `RequestDropped`, `RequestReachedDownloader`,
`RequestLeftDownloader`, `ResponseReceived`, `ResponseDownloaded`, `ItemScraped`, `ItemDropped`,
`ItemError`, `FeedSlotClosed`, `FeedExporterClosed`. Handlers may be sync or async; exceptions are
logged and don't stop other handlers.

## Stats

`result.Stats` (or `crawler.Stats.GetStats()`) uses Scrapy's keys:

```
downloader/request_count, downloader/response_count, downloader/response_status_count/200,
downloader/response_bytes, downloader/exception_type_count/..., item_scraped_count,
item_dropped_count, response_received_count, retry/count, retry/reason_count/...,
dupefilter/filtered, scheduler/enqueued, request_depth_max, httpcache/hit,
log_count/ERROR, start_time, finish_time, finish_reason, elapsed_time_seconds
```

Counters are updated with `Interlocked` on pre-allocated cells, so stats cost nothing measurable.

## Logging

Scrapy.Net logs through `Microsoft.Extensions.Logging`. By default it writes Scrapy-style lines to the
console (`2026-01-01 12:00:00 [scrapynet.core.engine] INFO: Spider opened`) or to `LOG_FILE`, and
counts lines per level into stats. To route logs into your host's logging, pass your own factory:

```csharp
var factory = new ScrapyLoggerFactory(LogLevel.Information, console: false);
factory.AddProvider(myProvider);                  // Serilog, OpenTelemetry, ...
var crawler = new Crawler(new BooksSpider(), settings, factory);
```
