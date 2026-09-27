# Progress — Scrapy.Net

Development log. Newest status first. See [PLAN.md](PLAN.md) for the roadmap.

## Status

| Area | State | Verified by |
|---|---|---|
| Core engine (scheduler, downloader, scraper, signals, stats, settings) | Done | 40+ end-to-end crawl tests against the sandbox |
| Selectors (CSS `::text`/`::attr`, XPath, `has-class`, `re:*`) | Done | `SelectorTests` |
| Items, ItemAdapter, ItemLoader, processors | Done | `ComponentTests` |
| Spiders: Spider, CrawlSpider, SitemapSpider, XmlFeedSpider, CsvFeedSpider | Done | `CrawlTests` |
| Downloader middlewares (retry, redirect, meta refresh, cookies, proxy, proxy rotation, robots.txt, HTTP cache, auth, headers, UA, stats, offsite) | Done | `CrawlTests` |
| Spider middlewares (HTTP error, offsite, referer, URL length, depth) | Done | `CrawlTests` |
| Pipelines (files, images, validation, cleaning, duplicates, ADO.NET, REST API, Elasticsearch) | Done | `CrawlTests` (SQLite, media) |
| Feed exports (JSON, JSON Lines, CSV, XML; file/stdout/memory/HTTP PUT; gzip; batches) | Done | `CrawlTests`, `ComponentTests` |
| Extensions (CoreStats, LogStats, MemoryUsage, CloseSpider, AutoThrottle, SpiderState, FeedExporter) | Done | `CrawlTests` |
| Pause/resume (`JOBDIR`) | Done | `Jobdir_pause_and_resume_continues_where_it_stopped` |
| Command line (`scrapynet` tool + in-project runner), shell | Done | manual smoke tests (startproject, genspider, fetch, shell, bench) |
| Sandbox practice site | Done | used by every crawl test |
| AI/RAG package | Done | `AITests` |
| Platform package (catalog, declarative spiders, jobs, cron, secrets, monitoring, alerts, raw content) | Done | `PlatformTests` |
| Playwright (JavaScript rendering) | Done | `PlaywrightTests` against real Chromium |
| Server (REST API, roles) | Done | `ServerTests`, `ScrapyNet.ServerHost` checked with curl |
| Live LLM clients (Azure OpenAI, DeepSeek) | Done | `LiveLlmTests` (opt-in via env vars), passed against real endpoints |
| Samples (18 console, single-file spider, server host) | Done | every sample run (`-- all`) |
| Notebooks (6) | Done | generated verify programs run with `dotnet run file.cs` |
| Scrapy Gallery (Avalonia, 21 demos) + screenshots | Done | 12 headless screenshots in `docs/images` |
| Documentation EN/ID (15 chapters each), README | Done | reviewed |
| Benchmarks (BenchmarkDotNet + end-to-end crawl) | Done | ~316,000 pages/min end to end |
| CI/publish workflows, NuGet packaging (7 packages) | Done | `dotnet pack` with README, license, icon |
| Move to Vibe_Library, NuGet publish 0.1.0 | Done | build + 109 tests in the monorepo, packages pushed |

## Log

### 2026-09-27 (release)
- Playwright handler, Server API, samples, notebooks, Scrapy Gallery, bilingual docs, benchmarks, workflows.
- Test suite: 109 tests passing offline (~3 s); 4 live-LLM tests skip without credentials.
- More bugs found and fixed:
  - .NET 10 file-based apps disable reflection JSON: all serialization passes `ScrapyJson` options; exporters write primitives with `Utf8JsonWriter`.
  - AngleSharp.XPath throws on some scalar functions of the document root; re-evaluated on the root element.
  - Sandbox book titles repeated after the 12th; volume numerals added.
  - Reasoning models reject `max_tokens`/`temperature`: `TokenLimitParameter`, nullable `Temperature`, `OpenAiChatModel.ForAzure`.
- Performance: fingerprint allocations cut from 1.71 MB to 1.09 MB per 1,000 requests; end-to-end ~316,000 pages/min at 16 concurrent requests.
- Moved to `Vibe_Library/ScrapyNet`; published `Gravicode.ScrapyNet*` 0.1.0 to nuget.org.

### 2026-09-27 (core)
- Core library written and compiling with zero warnings.
- Sandbox site (raw-socket HTTP server) with quotes, books, news, login + CSRF, secured API, sitemaps (gzip too), feeds, robots.txt, redirects, flaky/slow/drop endpoints, ETag/304, proxy mode.
- Test suite: 93 tests, all passing (~3 s).
- Bugs found by tests and fixed:
  - `"\x89PNG"u8`-style literals encode code points as multi-byte UTF-8; replaced by byte arrays (BOM and PNG checks).
  - Sitemap parser skipped the node after each `ReadElementContentAsString`.
  - `WebProxy` silently bypasses loopback; a non-bypassing `IWebProxy` is used instead.
  - AngleSharp.XPath returns `null` for scalar functions of the document root; evaluated against the root element instead.
  - SimHash defaults recalibrated (word bigrams, 7-bit threshold with pigeonhole banding) after measuring real distances.
- `scrapynet bench` against the local sandbox: ~74,600 pages/min on a laptop.
