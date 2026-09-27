# 12. Galeri, sampel, dan notebook

## Scrapy Gallery

![Scrapy Gallery — spider pertama](../images/gallery-first-spider.png)

Aplikasi desktop (Avalonia, Windows/Linux/macOS) yang menyajikan setiap fitur sebagai demo yang bisa
dijalankan terhadap situs latihan bawaan:

```bash
dotnet run --project samples/ScrapyGallery
```

Setiap demo memiliki deskripsi dua bahasa (tombol EN/ID), kode persis yang dijalankan — galeri
membacanya dari file sumbernya sendiri, jadi yang Anda lihat adalah yang berjalan — input yang bisa
diubah, dan tombol **Run**. Selama berjalan Anda bisa melihat:

- **jaring crawl**: halaman awal di pusat, setiap halaman yang diikuti ditempatkan di cincin sesuai
  kedalaman tautannya, dengan benang ke halaman yang menautkannya, diwarnai sesuai hasil (hijau 2xx,
  ungu 3xx, merah karat error, abu-abu antre; cincin kuning menandai hasil cache);
- **buku besar**: request, respons, item, yang dibuang, error, retry, redirect, cache hit, byte,
  halaman per menit;
- tab **Items** (tabel langsung), **Log** (baris gaya Scrapy), **Charts** (kemajuan, status, dan seri
  khusus demo seperti jeda AutoThrottle), dan **Output** (teks, gambar hasil unduhan, screenshot browser).

| Grup | Demo |
|---|---|
| Ekstraksi data | Taman bermain selector · Item dan item loader |
| Menjelajah situs | Spider pertama · CrawlSpider dan aturan tautan · Sitemap dan robots.txt · Login, formulir, dan sesi · API JSON dan API key · Halaman JavaScript di browser |
| Olah & ekspor | Pipeline item · Ekspor feed · File dan gambar |
| Operasional crawl | Retry, redirect, dan error · Rotasi proxy · AutoThrottle · Kedalaman, duplikat, dan sinyal · Jeda dan lanjutkan · Cache HTTP |
| AI & pengetahuan | Dari crawl ke basis pengetahuan · Hampir-duplikat dan ringkasan |
| Platform crawler | Job, jadwal, dan peringatan · Aturan scraping tanpa kode |

![Kedalaman, duplikat, dan sinyal](../images/gallery-link-graph.png)
![Cache HTTP](../images/gallery-http-cache.png)

Screenshot di dokumentasi ini dibuat oleh galeri itu sendiri secara headless:

```bash
dotnet run --project samples/ScrapyGallery -- --screenshots docs/images
```

## Sampel konsol

```bash
dotnet run --project samples/ScrapyNet.Samples            # daftar
dotnet run --project samples/ScrapyNet.Samples -- books   # jalankan satu
dotnet run --project samples/ScrapyNet.Samples -- all     # jalankan semua sampel offline
```

| Sampel | Menunjukkan |
|---|---|
| `quotes` | Spider dasar, paginasi, ekspor JSON |
| `books` | CrawlSpider, ItemLoader bertipe, ekspor CSV |
| `login` | FormRequest dengan CSRF, cookie |
| `api` | API JSON, API key per domain, paginasi |
| `sitemap` | robots.txt, indeks sitemap, sitemap ber-gzip |
| `feeds` | XmlFeedSpider, CsvFeedSpider |
| `media` | FilesPipeline, ImagesPipeline |
| `pipelines` | Pembersihan, validasi, deduplikasi, SQLite |
| `middleware` | Middleware sendiri, user agent acak, retry, redirect |
| `proxies` | Rotasi proxy dengan proxy yang diblokir |
| `throttle` | AutoThrottle terhadap server lambat |
| `resume` | Jeda dan lanjut dengan JOBDIR |
| `cache` | Pemutaran ulang cache HTTP |
| `signals` | Sinyal dan dump statistik |
| `rag` | Ekstraksi, chunking, deduplikasi, embedding, pencarian (jawaban LLM dengan `SCRAPYNET_DEEPSEEK_KEY`) |
| `platform` | Spider deklaratif, job, cron, peringatan, dashboard |
| `browser` | Rendering Playwright dan screenshot |
| `toscrape` | Tutorial Scrapy di quotes.toscrape.com yang asli |

`samples/SingleFile/quotes_spider.cs` adalah spider lengkap dalam satu file
(`dotnet run quotes_spider.cs`), dan `samples/ScrapyNet.ServerHost` menjalankan REST API.

## Notebook

Notebook .NET Interactive di `notebooks/` (buka di VS Code dengan ekstensi Polyglot Notebooks):

| Notebook | Isi |
|---|---|
| `01-getting-started` | Fetch, selektor, spider pertama di quotes.toscrape.com |
| `02-selectors-and-loaders` | Selektor offline dan item loader bertipe |
| `03-crawlspider-and-exports` | CrawlSpider di books.toscrape.com, ekspor CSV/JSONL |
| `04-sandbox-practice` | Login, retry, dan API di situs latihan dalam proses |
| `05-ai-rag` | Crawl → basis pengetahuan → pencarian → jawaban LLM |
| `06-platform-jobs` | Spider deklaratif, job, peringatan, cron |

Notebook dibuat dari `tools/notebooks/notebooks.mjs`, yang juga menulis program verifikasi untuk setiap
notebook; `dotnet run tools/notebooks/verify/05-ai-rag.cs` menjalankan kode notebook di luar Jupyter.

## Situs sandbox

`Gravicode.ScrapyNet.Sandbox` menjalankan situs latihan di port loopback yang kosong:

| Path | Isi |
|---|---|
| `/quotes/`, `/quotes/page/N/`, `/quotes/tag/T/`, `/quotes/author/A/` | Kutipan untuk di-scrape (50 kutipan, 5 halaman) |
| `/quotes/js/` | Kutipan yang sama, dirender oleh JavaScript |
| `/books/`, `/books/catalogue/page-N.html`, halaman produk dan kategori, `/books/media/N.png` | Toko buku (60 buku) dengan sampul PNG sungguhan |
| `/news/`, `/news/SLUG` | Artikel dengan boilerplate, OpenGraph, dan JSON-LD (plus satu salinan sindikasi yang hampir sama) |
| `/news/live` | Konten dengan ETag/Last-Modified; `site.BumpLiveVersion()` mengubahnya |
| `/login`, `/account`, `/logout` | Login formulir dengan token CSRF dan cookie sesi (`scrapy` / `net`) |
| `/api/products`, `/api/secure/products`, `/api/quotes` | API JSON; yang aman butuh `X-Api-Key: sandbox-key` |
| `/robots.txt`, `/sitemap.xml`, `/sitemap-books.xml`, `/sitemap-quotes.xml.gz` | Aturan robots, indeks sitemap, sitemap ber-gzip |
| `/feeds/products.xml`, `/feeds/products.csv` | Feed produk |
| `/files/`, `/files/report-N.pdf` | File yang bisa diunduh |
| `/graph/N` | Graf tautan 200 halaman yang penuh siklus |
| `/status/CODE`, `/flaky/KEY?fail=N`, `/slow?ms=N`, `/drop`, `/redirect/N`, `/meta-refresh` | Mode kegagalan |
| `/headers`, `/cookies`, `/cookies/set?k=v`, `/ip` | Endpoint echo |

Bila dijalankan dengan `SandboxOptions { BanProxyAfter = N }`, sandbox juga berperan sebagai proxy HTTP
yang mulai membalas 403 setelah N request — demo rotasi proxy memakai dua atau tiga di antaranya.
