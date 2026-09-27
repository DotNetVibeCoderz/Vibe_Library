# 2. Architecture

Scrapy.Net keeps Scrapy's architecture, so everything you know about Scrapy's data flow applies.

```
                 ┌────────────────────────── Crawler ──────────────────────────┐
                 │  Settings (frozen) · Stats · Signals · Extensions · Logging │
                 └──────────────────────────────┬───────────────────────────────┘
                                                │
 Spider.StartAsync() ──► spider middlewares ──► ExecutionEngine ◄──────────────────────┐
                            (ProcessStartRequests)   │                                   │
                                                     ▼                                   │
                                                Scheduler  (priority queue + dupe filter │
                                                     │       + JOBDIR persistence)       │
                                                     ▼                                   │
                               downloader middlewares (ProcessRequest ▼ / Response ▲)    │
                                                     │                                   │
                                          Downloader slots (per domain / IP:             │
                                          concurrency + delay) ─► download handlers       │
                                          (HTTP · file · data · Playwright)              │
                                                     │ Response                          │
                                                     ▼                                   │
                               spider middlewares (ProcessSpiderInput / Output)          │
                                                     │                                   │
                                                     ▼                                   │
                                     Spider callback ── Request ─────────────────────────┘
                                                     │
                                                    item
                                                     ▼
                                           Item pipelines ─► item_scraped signal ─► feed exporters
```

## The pieces

| Component | Class | Role |
|---|---|---|
| Crawler | `Crawler` | Owns one spider run: settings, stats, signals, extensions, engine |
| Engine | `ExecutionEngine` | Pulls start requests lazily, moves requests from scheduler to downloader, detects idleness, closes the spider |
| Scheduler | `Scheduler` (`IScheduler`) | Priority queue (LIFO or FIFO within a priority), duplicate filter, JOBDIR persistence |
| Dupe filter | `RFPDupeFilter` (`IDupeFilter`) | SHA-1 request fingerprints stored as 20-byte structs |
| Downloader | `Downloader` | Global `CONCURRENT_REQUESTS`, per-slot concurrency and delay, handler dispatch by URL scheme |
| Download handlers | `HttpDownloadHandler`, `FileDownloadHandler`, `DataUriDownloadHandler`, `PlaywrightDownloadHandler` | Turn a request into a response |
| Scraper | `Scraper` | Runs callbacks through spider middlewares, routes requests back to the engine and items through pipelines |
| Signals | `SignalManager` | Typed event bus (`Signals.ItemScraped`, `Signals.SpiderClosed`, ...) |
| Stats | `MemoryStatsCollector` | Lock-free counters with Scrapy's keys (`downloader/request_count`, ...) |

## Lifecycle of a request

1. The spider yields a `Request` (from `StartAsync` or a callback).
2. Spider middlewares see it on the way out (offsite filter, depth, referer, URL length).
3. The engine hands it to the scheduler, which drops it if its fingerprint was seen (unless
   `DontFilter`).
4. When the downloader has capacity, the engine dequeues it. Downloader middlewares run
   `ProcessRequestAsync` in ascending order (robots.txt, auth, headers, user agent, cookies, proxy,
   cache...).
5. The request waits for a slot permit and the slot's delay, then a download handler fetches it.
6. Downloader middlewares run `ProcessResponseAsync` in descending order; the retry and redirect
   middlewares may replace the response with a new request, which goes back to step 3.
7. Spider middlewares run `ProcessSpiderInputAsync` (the HTTP error middleware rejects non-2xx here);
   the callback runs; its output passes through `ProcessSpiderOutput`.
8. Items go through the item pipelines in order; surviving items fire `item_scraped`, which the
   feed exporter writes out.

## When does a spider close?

The engine closes a spider with reason `finished` when all of these hold: start requests are
exhausted, the scheduler is empty, nothing is downloading or being scraped, and no `spider_idle`
handler threw `DontCloseSpiderException`. Other reasons come from extensions
(`closespider_itemcount`, `closespider_timeout`, ...), from `CloseSpiderException` in a callback, from
`crawler.StopAsync()` (`shutdown`) or `crawler.Kill()` (`killed`).

## Concurrency model

Everything is `async`. There is no reactor thread: downloads run as tasks on the thread pool, and the
engine loop sleeps on a wake-up signal that every scheduled request, finished download and unpause
triggers. The backpressure rules are Scrapy's:

- the downloader accepts at most `CONCURRENT_REQUESTS` requests;
- each slot (domain, or IP with `CONCURRENT_REQUESTS_PER_IP`) downloads at most
  `CONCURRENT_REQUESTS_PER_DOMAIN` at once, spaced by `DOWNLOAD_DELAY` (randomized 0.5–1.5× by default);
- the scraper stops the engine from fetching more while over `SCRAPER_SLOT_MAX_ACTIVE_SIZE` bytes of
  responses are being processed;
- item pipelines run at most `CONCURRENT_ITEMS` items at once.

## Extending

Every box above is replaceable through settings:

| To replace | Setting | Contract |
|---|---|---|
| Scheduler | `SCHEDULER` | `IScheduler` |
| Duplicate filter | `DUPEFILTER_CLASS` | `IDupeFilter` |
| Stats | `STATS_CLASS` | `IStatsCollector` |
| Download handler for a scheme | `settings.SetDownloadHandler("https", typeof(...))` | `IDownloadHandler` |
| Downloader middleware | `settings.DownloaderMiddlewares.Add<T>(order)` | `DownloaderMiddleware` |
| Spider middleware | `settings.SpiderMiddlewares.Add<T>(order)` | `SpiderMiddleware` |
| Item pipeline | `settings.ItemPipelines.Add<T>(order)` | `ItemPipeline` |
| Extension | `settings.Extensions.Add<T>(order)` | any class; connect to signals in its constructor |

Component constructors may take any of `Crawler`, `Settings`, `IStatsCollector`, `SignalManager`,
`ILogger`, `ILoggerFactory` or the spider; the crawler injects them. Throw `NotConfiguredException`
from a constructor to switch a component off (for example when its setting is missing).
