# 2. Arsitektur

Scrapy.Net mempertahankan arsitektur Scrapy, sehingga semua pengetahuan tentang alur data Scrapy tetap
berlaku.

```
                 ┌────────────────────────── Crawler ──────────────────────────┐
                 │  Settings (beku) · Stats · Signals · Extensions · Logging   │
                 └──────────────────────────────┬───────────────────────────────┘
                                                │
 Spider.StartAsync() ──► spider middleware ──► ExecutionEngine ◄───────────────────────┐
                            (ProcessStartRequests)   │                                   │
                                                     ▼                                   │
                                                Scheduler  (antrean prioritas + filter   │
                                                     │       duplikat + persistensi JOBDIR)
                                                     ▼                                   │
                               downloader middleware (ProcessRequest ▼ / Response ▲)     │
                                                     │                                   │
                                          Slot downloader (per domain / IP:              │
                                          konkurensi + jeda) ─► download handler         │
                                          (HTTP · file · data · Playwright)              │
                                                     │ Response                          │
                                                     ▼                                   │
                               spider middleware (ProcessSpiderInput / Output)           │
                                                     │                                   │
                                                     ▼                                   │
                                      Callback spider ── Request ────────────────────────┘
                                                     │
                                                   item
                                                     ▼
                                          Item pipeline ─► sinyal item_scraped ─► feed exporter
```

## Komponen

| Komponen | Kelas | Peran |
|---|---|---|
| Crawler | `Crawler` | Memiliki satu run spider: setting, statistik, sinyal, ekstensi, engine |
| Engine | `ExecutionEngine` | Mengambil start request secara malas, memindahkan request dari scheduler ke downloader, mendeteksi idle, menutup spider |
| Scheduler | `Scheduler` (`IScheduler`) | Antrean prioritas (LIFO atau FIFO dalam satu prioritas), filter duplikat, persistensi JOBDIR |
| Filter duplikat | `RFPDupeFilter` (`IDupeFilter`) | Sidik jari request SHA-1 yang disimpan sebagai struct 20 byte |
| Downloader | `Downloader` | `CONCURRENT_REQUESTS` global, konkurensi dan jeda per slot, pemilihan handler berdasarkan skema URL |
| Download handler | `HttpDownloadHandler`, `FileDownloadHandler`, `DataUriDownloadHandler`, `PlaywrightDownloadHandler` | Mengubah request menjadi respons |
| Scraper | `Scraper` | Menjalankan callback melalui spider middleware, mengembalikan request ke engine dan mengirim item ke pipeline |
| Sinyal | `SignalManager` | Event bus bertipe (`Signals.ItemScraped`, `Signals.SpiderClosed`, ...) |
| Statistik | `MemoryStatsCollector` | Counter tanpa lock dengan kunci milik Scrapy (`downloader/request_count`, ...) |

## Siklus hidup sebuah request

1. Spider meng-yield `Request` (dari `StartAsync` atau callback).
2. Spider middleware melihatnya saat keluar (filter offsite, kedalaman, referer, panjang URL).
3. Engine menyerahkannya ke scheduler, yang membuangnya bila sidik jarinya sudah pernah terlihat
   (kecuali `DontFilter`).
4. Saat downloader punya kapasitas, engine mengambilnya dari antrean. Downloader middleware menjalankan
   `ProcessRequestAsync` berurutan naik (robots.txt, auth, header, user agent, cookie, proxy, cache...).
5. Request menunggu izin slot dan jeda slot, lalu download handler mengambilnya.
6. Downloader middleware menjalankan `ProcessResponseAsync` berurutan turun; middleware retry dan
   redirect bisa mengganti respons dengan request baru yang kembali ke langkah 3.
7. Spider middleware menjalankan `ProcessSpiderInputAsync` (middleware HTTP error menolak non-2xx di
   sini); callback berjalan; hasilnya melewati `ProcessSpiderOutput`.
8. Item melewati item pipeline berurutan; item yang lolos memicu `item_scraped`, yang ditulis oleh
   feed exporter.

## Kapan spider ditutup?

Engine menutup spider dengan alasan `finished` bila semua ini terpenuhi: start request habis,
scheduler kosong, tidak ada yang sedang diunduh atau diproses, dan tidak ada handler `spider_idle` yang
melempar `DontCloseSpiderException`. Alasan lain datang dari ekstensi (`closespider_itemcount`,
`closespider_timeout`, ...), dari `CloseSpiderException` di callback, dari `crawler.StopAsync()`
(`shutdown`) atau `crawler.Kill()` (`killed`).

## Model konkurensi

Semuanya `async`. Tidak ada thread reactor: unduhan berjalan sebagai task di thread pool, dan loop
engine tidur menunggu sinyal bangun yang dipicu setiap request terjadwal, unduhan selesai, dan
unpause. Aturan backpressure mengikuti Scrapy:

- downloader menerima paling banyak `CONCURRENT_REQUESTS` request;
- setiap slot (domain, atau IP dengan `CONCURRENT_REQUESTS_PER_IP`) mengunduh paling banyak
  `CONCURRENT_REQUESTS_PER_DOMAIN` sekaligus, diberi jarak `DOWNLOAD_DELAY` (diacak 0,5–1,5× secara default);
- scraper menahan engine selama lebih dari `SCRAPER_SLOT_MAX_ACTIVE_SIZE` byte respons sedang diproses;
- item pipeline memproses paling banyak `CONCURRENT_ITEMS` item sekaligus.

## Memperluas

Setiap kotak di atas bisa diganti lewat setting:

| Yang diganti | Setting | Kontrak |
|---|---|---|
| Scheduler | `SCHEDULER` | `IScheduler` |
| Filter duplikat | `DUPEFILTER_CLASS` | `IDupeFilter` |
| Statistik | `STATS_CLASS` | `IStatsCollector` |
| Download handler untuk suatu skema | `settings.SetDownloadHandler("https", typeof(...))` | `IDownloadHandler` |
| Downloader middleware | `settings.DownloaderMiddlewares.Add<T>(order)` | `DownloaderMiddleware` |
| Spider middleware | `settings.SpiderMiddlewares.Add<T>(order)` | `SpiderMiddleware` |
| Item pipeline | `settings.ItemPipelines.Add<T>(order)` | `ItemPipeline` |
| Ekstensi | `settings.Extensions.Add<T>(order)` | kelas apa pun; sambungkan ke sinyal di konstruktornya |

Konstruktor komponen boleh menerima `Crawler`, `Settings`, `IStatsCollector`, `SignalManager`,
`ILogger`, `ILoggerFactory`, atau spider; crawler akan menyuntikkannya. Lempar
`NotConfiguredException` dari konstruktor untuk menonaktifkan komponen (misalnya bila setting-nya
tidak ada).
