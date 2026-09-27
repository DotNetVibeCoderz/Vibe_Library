# Dokumentasi Scrapy.Net

*Dibuat oleh Gravicode Studios dipimpin oleh Kang Fadhil. · [English](../en/README.md)*

Scrapy.Net adalah port [Scrapy](https://scrapy.org/) ke .NET 10: arsitektur yang sama, nama komponen
dan kunci setting yang sama, ditulis sebagai C# yang idiomatis.

![Scrapy Gallery](../images/gallery-crawlspider-items.png)

## Bab

| | |
|---|---|
| [1. Memulai](01-memulai.md) | Instalasi, spider pertama, menjalankan, mengekspor item |
| [2. Arsitektur](02-arsitektur.md) | Engine, scheduler, downloader, scraper, sinyal — perjalanan sebuah request |
| [3. Spider](03-spider.md) | `Spider`, `CrawlSpider`, `SitemapSpider`, spider feed XML/CSV, callback, errback |
| [4. Selektor, item, dan loader](04-selektor-item-loader.md) | CSS/XPath, `::text`, `::attr()`, regex, item, `ItemLoader` |
| [5. Pipeline dan ekspor feed](05-pipeline-dan-ekspor.md) | Pembersihan, validasi, deduplikasi, database, file dan gambar, JSON/CSV/XML |
| [6. Setting dan middleware](06-setting-dan-middleware.md) | Prioritas setting, semua middleware bawaan, membuat sendiri |
| [7. Operasional crawl](07-operasional.md) | Retry, proxy, AutoThrottle, cache HTTP, jeda/lanjut, statistik, logging |
| [8. Command line dan shell](08-cli-dan-shell.md) | Tool `scrapynet`, struktur proyek, `crawl`, `shell`, `parse`, spider satu file |
| [9. Halaman JavaScript](09-javascript.md) | Rendering Playwright, aksi halaman, screenshot |
| [10. AI dan RAG](10-ai-rag.md) | Ekstraksi artikel, chunking, deduplikasi, embedding, vector store, LLM |
| [11. Platform dan server](11-platform-dan-server.md) | Spider deklaratif, job, cron, secret, monitoring, alert, REST API |
| [12. Galeri, sampel, dan notebook](12-galeri-sampel-notebook.md) | Galeri Avalonia, sampel konsol, notebook .NET Interactive, situs sandbox |
| [13. Performa](13-performa.md) | Pilihan desain dan angka hasil pengukuran |
| [14. Dari Scrapy Python](14-dari-scrapy-python.md) | Pemetaan API dan idiom berdampingan |
| [15. Pemecahan masalah](15-pemecahan-masalah.md) | Masalah umum dan solusinya |

## Paket

| Paket | Isi |
|---|---|
| `Gravicode.ScrapyNet` | Framework: engine, spider, selektor, item, loader, middleware, pipeline, ekspor, ekstensi, runner command line |
| `Gravicode.ScrapyNet.Playwright` | Merender halaman JavaScript di Chromium, Firefox, atau WebKit |
| `Gravicode.ScrapyNet.AI` | Ekstraksi konten, chunking, deteksi hampir-duplikat, embedding, vector store, RAG |
| `Gravicode.ScrapyNet.Platform` | Katalog spider, spider deklaratif, job, cron, secret, monitoring, alert |
| `Gravicode.ScrapyNet.Server` | REST API ASP.NET Core di atas platform, dengan peran (role) |
| `Gravicode.ScrapyNet.Sandbox` | Situs latihan offline yang berjalan di dalam proses |
| `Gravicode.ScrapyNet.Cli` | Tool command line `scrapynet` |
