# 6. Setting dan middleware

## Setting

Setting memakai nama milik Scrapy, sehingga dokumentasi Scrapy tetap berlaku. Setiap nilai membawa
prioritas, dan sebuah nilai hanya menggantikan nilai dengan prioritas yang sama atau lebih rendah:

| Prioritas | Diset oleh |
|---|---|
| `Default` (0) | Default Scrapy.Net |
| `Command` (10) | Perintah yang sedang dijalankan |
| `Project` (20) | `scrapy.json`, kode Anda |
| `Spider` (30) | `Spider.ConfigureSettings` |
| `Cmdline` (40) | `-s KEY=VALUE` |

```csharp
var settings = new Settings();                                        // default sudah dimuat
settings.Set(SettingKeys.ConcurrentRequests, 32);
settings[SettingKeys.UserAgent] = "MyBot/1.0 (+https://example.com/bot)";
settings.LoadProjectFiles(".");                                       // scrapy.json + scrapy.{SCRAPYNET_ENV}.json
settings.LoadEnvironmentVariables();                                  // SCRAPYNET_SETTINGS_KEY=value
settings.GetInt(SettingKeys.ConcurrentRequests);                      // getter bertipe
```

`scrapy.json` (komentar dan koma di akhir diperbolehkan):

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

File per lingkungan memisahkan dev dan prod: `scrapy.dev.json`, `scrapy.prod.json`, dipilih dengan
`SCRAPYNET_ENV` atau `--env`. Setting dibekukan saat crawler mulai.

### Referensi

| Setting | Default | Arti |
|---|---|---|
| `BOT_NAME` | `scrapynet` | Nama proyek |
| `USER_AGENT` | `Scrapy.Net/0.1 (+…)` | User-Agent default |
| `CONCURRENT_REQUESTS` | 16 | Request berjalan secara keseluruhan |
| `CONCURRENT_REQUESTS_PER_DOMAIN` | 8 | Per slot (host) |
| `CONCURRENT_REQUESTS_PER_IP` | 0 | Bila > 0, slot per IP dan ini batasnya |
| `CONCURRENT_ITEMS` | 100 | Item di pipeline sekaligus |
| `DOWNLOAD_DELAY` | 0 | Detik antar unduhan dalam satu slot |
| `RANDOMIZE_DOWNLOAD_DELAY` | true | Jeda × 0,5–1,5 |
| `DOWNLOAD_SLOTS` | {} | `{concurrency, delay, randomize_delay}` per slot |
| `DOWNLOAD_TIMEOUT` | 180 | Detik |
| `DOWNLOAD_MAXSIZE` / `DOWNLOAD_WARNSIZE` | 1 GiB / 32 MiB | Batas ukuran respons |
| `DOWNLOAD_VERIFY_SSL` | true | Validasi sertifikat |
| `DOWNLOAD_HTTP2` | false | Utamakan HTTP/2 |
| `DEFAULT_REQUEST_HEADERS` | Accept, Accept-Language | Ditambahkan bila belum ada |
| `DOMAIN_HEADERS` | {} | Header per host (API key yang tidak boleh bocor) |
| `COOKIES_ENABLED` / `COOKIES_DEBUG` / `COOKIES_PERSIST_FILE` | true / false / – | Penanganan cookie |
| `DEPTH_LIMIT` / `DEPTH_PRIORITY` / `DEPTH_STATS_VERBOSE` | 0 / 0 / false | Kontrol kedalaman tautan |
| `SCHEDULER_ORDER` | `lifo` | `lifo` (depth-first) atau `fifo` (breadth-first) dalam satu prioritas |
| `JOBDIR` | – | Folder jeda/lanjut |
| `RETRY_ENABLED` / `RETRY_TIMES` / `RETRY_HTTP_CODES` | true / 2 / 500,502,503,504,522,524,408,429 | Retry |
| `RETRY_PRIORITY_ADJUST` / `RETRY_BACKOFF_BASE` / `RETRY_BACKOFF_MAX` | -1 / 0 / 60 | Penjadwalan retry |
| `REDIRECT_ENABLED` / `REDIRECT_MAX_TIMES` | true / 20 | Redirect |
| `METAREFRESH_ENABLED` / `METAREFRESH_MAXDELAY` | true / 100 | `<meta http-equiv="refresh">` |
| `ROBOTSTXT_OBEY` / `ROBOTSTXT_USER_AGENT` | false / – | robots.txt |
| `HTTPERROR_ALLOWED_CODES` / `HTTPERROR_ALLOW_ALL` | [] / false | Non-2xx diteruskan ke callback |
| `HTTPPROXY_ENABLED` | true | Mengikuti `http_proxy`/`https_proxy`/`no_proxy` |
| `PROXY_LIST` / `PROXY_PER_DOMAIN` / `PROXY_BAN_STATUS_CODES` / `PROXY_BAN_COOLDOWN` | – / – / 403,407,429 / 300 | Rotasi proxy |
| `HTTP_USER` / `HTTP_PASS` / `HTTP_BEARER_TOKEN` / `HTTP_AUTH_DOMAIN` | – | Autentikasi, dibatasi satu domain |
| `HTTPCACHE_ENABLED` / `HTTPCACHE_DIR` / `HTTPCACHE_EXPIRATION_SECS` / `HTTPCACHE_POLICY` | false / httpcache / 0 / dummy | Cache HTTP |
| `AUTOTHROTTLE_ENABLED` / `_START_DELAY` / `_MAX_DELAY` / `_TARGET_CONCURRENCY` / `_DEBUG` | false / 5 / 60 / 1.0 / false | AutoThrottle |
| `CLOSESPIDER_TIMEOUT` / `_ITEMCOUNT` / `_PAGECOUNT` / `_ERRORCOUNT` / `_TIMEOUT_NO_ITEM` | 0 | Kondisi berhenti |
| `FEEDS` dan `FEED_EXPORT_*` | – | Ekspor feed |
| `FILES_STORE`, `IMAGES_STORE`, `FILES_EXPIRES`, `IMAGES_MIN_WIDTH`, … | – | Pipeline media |
| `LOG_ENABLED` / `LOG_LEVEL` / `LOG_FILE` / `LOGSTATS_INTERVAL` | true / INFO / – / 60 | Logging |
| `MEMUSAGE_ENABLED` / `MEMUSAGE_LIMIT_MB` / `MEMUSAGE_WARNING_MB` | false / 0 / 0 | Penjaga memori |
| `STATS_DUMP` | true | Mencatat statistik saat spider ditutup |
| `URLLENGTH_LIMIT` | 2083 | URL yang lebih panjang dibuang |
| `SCHEDULER`, `DUPEFILTER_CLASS`, `STATS_CLASS` | – | Mengganti komponen inti |

`LOG_LEVEL` default-nya `INFO` (Scrapy memakai `DEBUG`); set `DEBUG` untuk melihat setiap URL yang dicrawl.

## Downloader middleware

Aktif secara default, berurutan:

| Urutan | Middleware | Fungsi |
|---|---|---|
| 50 | `OffsiteDownloaderMiddleware` | Membuang request di luar `AllowedDomains` (termasuk start request) |
| 100 | `RobotsTxtMiddleware` | Mengambil dan mematuhi robots.txt (`ROBOTSTXT_OBEY`) |
| 300 | `HttpAuthMiddleware` | Autentikasi Basic atau Bearer untuk satu domain |
| 350 | `DownloadTimeoutMiddleware` | Meta timeout per request |
| 400 | `DefaultHeadersMiddleware` | `DEFAULT_REQUEST_HEADERS` |
| 450 | `DomainHeadersMiddleware` | `DOMAIN_HEADERS` |
| 500 | `UserAgentMiddleware` | `USER_AGENT` |
| 550 | `RetryMiddleware` | Mengulang kegagalan sementara dan status yang bisa diulang |
| 580 | `MetaRefreshMiddleware` | Mengikuti meta refresh |
| 600 | `RedirectMiddleware` | Mengikuti 3xx lewat scheduler |
| 700 | `CookiesMiddleware` | Cookie jar (`meta["cookiejar"]` untuk beberapa sesi) |
| 750 | `HttpProxyMiddleware` | `meta["proxy"]` dan variabel lingkungan proxy |
| 850 | `DownloaderStatsMiddleware` | Statistik `downloader/*` |
| 900 | `HttpCacheMiddleware` | Cache disk (`HTTPCACHE_ENABLED`) |

Tersedia tetapi tidak aktif: `RandomUserAgentMiddleware` (tambahkan di 400), `ProxyRotationMiddleware`
(740), `IncrementalRecrawlMiddleware` (paket AI, 950).

### Membuat sendiri

```csharp
public sealed class ApiKeyMiddleware(Settings settings) : DownloaderMiddleware
{
    private readonly string _key = settings.GetString("MY_API_KEY") ?? throw new NotConfiguredException("MY_API_KEY belum diset");

    public override ValueTask<DownloadResult> ProcessRequestAsync(Request request, Spider spider)
    {
        request.Headers.SetDefault("X-Api-Key", _key);
        return DownloadResult.Continue;                   // atau kembalikan Response untuk melewati unduhan,
    }                                                    // atau Request untuk menjadwalkan ulang

    public override async ValueTask<DownloadResult> ProcessResponseAsync(Request request, Response response, Spider spider)
    {
        if (response.Status == 401) throw new IgnoreRequestException("API key ditolak");
        return response;
    }
}

settings.DownloaderMiddlewares.Add<ApiKeyMiddleware>(543);
settings.DownloaderMiddlewares.Disable<RetryMiddleware>();
```

`ProcessRequestAsync` berjalan berurutan naik, `ProcessResponseAsync` dan `ProcessExceptionAsync`
berurutan turun. Mengembalikan `Request` dari salah satunya mengirim request itu kembali ke scheduler.

## Spider middleware

| Urutan | Middleware | Fungsi |
|---|---|---|
| 50 | `HttpErrorMiddleware` | Menyaring respons non-2xx (errback menerima `HttpErrorException`) |
| 500 | `OffsiteMiddleware` | Membuang request offsite dari callback |
| 700 | `RefererMiddleware` | Mengisi `Referer` sesuai `REFERRER_POLICY` |
| 800 | `UrlLengthMiddleware` | Membuang URL lebih dari `URLLENGTH_LIMIT` |
| 900 | `DepthMiddleware` | Melacak `depth`, menerapkan `DEPTH_LIMIT` dan `DEPTH_PRIORITY` |

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

## Ekstensi

Kelas apa pun yang didaftarkan di `settings.Extensions` dibuat bersama crawler dan biasanya tersambung
ke sinyal di konstruktornya. Bawaan: `CoreStats`, `LogStats`, `MemoryUsage`, `CloseSpiderExtension`,
`FeedExporter`, `SpiderStateExtension`, `AutoThrottle`; paket platform menambahkan
`RawContentExtension` dan `LoginRedirectDetector`.
