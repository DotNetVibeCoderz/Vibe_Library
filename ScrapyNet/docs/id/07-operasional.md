# 7. Operasional crawl

## Retry, redirect, dan error

![Retry, redirect, dan error](../images/gallery-retries.png)

`RetryMiddleware` mengulang timeout, kegagalan koneksi, error DNS, dan `RETRY_HTTP_CODES` sampai
`RETRY_TIMES` percobaan tambahan, dengan `RETRY_PRIORITY_ADJUST`. Scrapy.Net menambahkan backoff
eksponensial opsional (`RETRY_BACKOFF_BASE`, dibatasi `RETRY_BACKOFF_MAX`) yang disukai situs yang
membalas 429. Per request: `meta["dont_retry"]`, `meta["max_retry_times"]`. Dari callback, ulangi halaman
yang termuat tetapi tidak lengkap:

```csharp
if (response.Css(".price").Count == 0 &&
    RetryMiddleware.GetRetryRequest(response.Request, "harga tidak ada") is { } retry)
    yield return retry;
```

Redirect (301/302/303/307/308 dan meta refresh) dijadwalkan sebagai request baru, sehingga melewati
filter duplikat dan tercatat di statistik; `meta["redirect_urls"]` menyimpan rantainya. Header
Authorization dan Cookie dihapus bila redirect berpindah host.

## Proxy

```csharp
// satu request
yield return new Request(url) { Meta = { ["proxy"] = "http://user:pass@proxy.example:8080" } };

// rotasi dengan deteksi blokir
settings.Set(SettingKeys.ProxyList, new List<string> { "http://p1:8080", "http://p2:8080", "http://p3:8080" });
settings.Set(SettingKeys.ProxyPerDomain, new Dictionary<string, object?> { ["api.example.com"] = "http://static-ip:8080" });
settings.DownloaderMiddlewares.Add<ProxyRotationMiddleware>(740);
```

Proxy yang membalas status blokir (`PROXY_BAN_STATUS_CODES`) atau gagal terhubung diistirahatkan selama
`PROXY_BAN_COOLDOWN` detik dan request-nya dialihkan. Statistik: `proxies/used/...`, `proxies/banned`,
`proxies/rerouted`. Kredensial proxy tidak pernah dicatat di log.

## AutoThrottle

![AutoThrottle](../images/gallery-autothrottle.png)

```csharp
settings.Set(SettingKeys.AutoThrottleEnabled, true);
settings.Set(SettingKeys.AutoThrottleTargetConcurrency, 2.0);
```

Latensi setiap respons menggeser jeda slot menuju `latensi / AUTOTHROTTLE_TARGET_CONCURRENCY`, dibatasi
`DOWNLOAD_DELAY` dan `AUTOTHROTTLE_MAX_DELAY`. Respons error tidak pernah memperpendek jeda.
`AutoThrottle.History` menyediakan riwayat penyesuaian untuk grafik.

## Cache HTTP

```csharp
settings.Set(SettingKeys.HttpCacheEnabled, true);           // nyalakan saat mengembangkan selektor
settings.Set(SettingKeys.HttpCacheExpirationSecs, 3600);
```

Respons disimpan di `HTTPCACHE_DIR/<spider>/<fp[0:2]>/<fp>/`. Kebijakan `dummy` menyimpan semuanya;
`rfc2616` mengikuti `Cache-Control`, `Expires`, dan `Last-Modified`. Respons yang diputar ulang membawa
flag `cached`. `HTTPCACHE_IGNORE_MISSING` membuat pemutaran ulang offline langsung gagal alih-alih
mengunduh.

## Jeda dan lanjutkan

```bash
dotnet run -- crawl books -s JOBDIR=crawls/books-1
# Ctrl+C sekali: request yang sedang berjalan selesai, antrean disimpan
dotnet run -- crawl books -s JOBDIR=crawls/books-1     # dilanjutkan
```

`JOBDIR` berisi `requests.queue` (request tertunda), `requests.seen` (sidik jari), dan `spider.state`
(`Spider.State`). Hanya request dengan callback berupa method bernama milik spider yang bisa disimpan;
sisanya dihitung dalam `scheduler/unserializable`. Nilai meta harus bisa diserialisasi ke JSON.

## Kondisi berhenti

```csharp
settings.Set(SettingKeys.CloseSpiderItemCount, 1000);
settings.Set(SettingKeys.CloseSpiderTimeout, 600);          // detik
settings.Set(SettingKeys.CloseSpiderTimeoutNoItem, 120);    // crawl yang macet
settings.Set(SettingKeys.CloseSpiderErrorCount, 50);
```

Dari kode: `await crawler.StopAsync()` (rapi), `crawler.Kill()` (seketika), `crawler.Pause()` dan
`crawler.Resume()`; atau batalkan token yang diberikan ke `CrawlAsync`.

## Sinyal

```csharp
var crawler = new Crawler(new BooksSpider(), settings);
crawler.Signals.Connect(Signals.ItemScraped, e => Console.WriteLine($"item dari {e.Response?.Url}"));
crawler.Signals.Connect(Signals.SpiderClosed, e => Console.WriteLine($"ditutup: {e.Reason}"));
crawler.Signals.Connect(Signals.SpiderIdle, e =>
{
    if (queue.TryDequeue(out var url)) { _ = crawler.Engine.CrawlAsync(new Request(url)); throw new DontCloseSpiderException(); }
});   // pertahankan spider selama masih ada pekerjaan dari luar
await crawler.CrawlAsync();
```

Tersedia: `EngineStarted`, `EngineStopped`, `SpiderOpened`, `SpiderIdle`, `SpiderClosed`, `SpiderError`,
`RequestScheduled`, `RequestDropped`, `RequestReachedDownloader`, `RequestLeftDownloader`,
`ResponseReceived`, `ResponseDownloaded`, `ItemScraped`, `ItemDropped`, `ItemError`, `FeedSlotClosed`,
`FeedExporterClosed`. Handler boleh sync atau async; exception dicatat dan tidak menghentikan handler lain.

## Statistik

`result.Stats` (atau `crawler.Stats.GetStats()`) memakai kunci milik Scrapy:

```
downloader/request_count, downloader/response_count, downloader/response_status_count/200,
downloader/response_bytes, downloader/exception_type_count/..., item_scraped_count,
item_dropped_count, response_received_count, retry/count, retry/reason_count/...,
dupefilter/filtered, scheduler/enqueued, request_depth_max, httpcache/hit,
log_count/ERROR, start_time, finish_time, finish_reason, elapsed_time_seconds
```

Counter diperbarui dengan `Interlocked` pada sel yang sudah dialokasikan, sehingga biaya statistik
tidak terukur.

## Logging

Scrapy.Net mencatat log lewat `Microsoft.Extensions.Logging`. Secara default ia menulis baris gaya
Scrapy ke konsol (`2026-01-01 12:00:00 [scrapynet.core.engine] INFO: Spider opened`) atau ke `LOG_FILE`,
dan menghitung baris per level ke statistik. Untuk meneruskan log ke sistem logging aplikasi Anda,
berikan factory sendiri:

```csharp
var factory = new ScrapyLoggerFactory(LogLevel.Information, console: false);
factory.AddProvider(myProvider);                  // Serilog, OpenTelemetry, ...
var crawler = new Crawler(new BooksSpider(), settings, factory);
```
