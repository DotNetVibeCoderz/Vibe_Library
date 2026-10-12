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
| Documentation EN/ID (16 chapters each), README | Done | reviewed |
| Benchmarks (BenchmarkDotNet + end-to-end crawl) | Done | ~316,000 pages/min end to end |
| CI/publish workflows, NuGet packaging (7 packages) | Done | `dotnet pack` with README, license, icon |
| Move to Vibe_Library, NuGet publish 0.1.0 | Done | build + 109 tests in the monorepo, packages pushed |
| `[ScrapyItem]` source generator, NativeAOT | Done | `GeneratedItemTests`; `samples/AotSpider` published native (win-x64) and run; CI `aot` job (linux-x64) |
| Disk queue for `JOBDIR` (crash-safe journal) | Done | `DiskQueueTests`, `Jobdir_pause_and_resume...`; benchmark 187k vs 229k pages/min in memory |
| HTTP/3 | Done | `Http3Tests` against Kestrel over QUIC (exact and Alt-Svc upgrade) |
| Spider contracts, `scrapynet check` | Done | `ContractTests` (runner, report, command exit code) |
| Redis distributed crawling (`Gravicode.ScrapyNet.Redis`) | Done | `RedisTests` against a real Redis (two workers, 200 pages, no duplicates); CI `integration` job |
| pgvector, Azure AI Search, Microsoft.Extensions.AI | Done | `VectorStoreTests` (Azure against an in-process fake of the REST API; pgvector against PostgreSQL in CI) |
| Scrapy.Net Console (web admin) | Done | `AdminConsoleTests`; `tools/console-screenshots.cs` drives every view in Chromium (light, dark, phone, viewer role), zero console/CSP errors |
| Gallery demos: contracts, distributed | Done | headless screenshots `gallery-contracts.png`, `gallery-distributed.png` |
| NuGet publish 0.2.0 (8 packages) | Done | packages pushed |

## Log

### 2026-10-13 (0.2.0)
- Every "Later" item of PLAN.md built: generated item accessors + NativeAOT, disk queue, Redis, HTTP/3, console, contracts, vector stores, Microsoft.Extensions.AI.
- Test suite: 131 tests (126 pass locally with Redis; pgvector and live-LLM tests opt in through environment variables).
- Found and fixed along the way:
  - NativeAOT trimmed the constructors of built-in components created from settings; Core now embeds an ILLink descriptor that keeps them.
  - Routing treats `/admin` and `/admin/` alike, so the console's redirect looped; it now redirects only without the slash.
  - A two-worker Redis crawl could finish inside one idle poll (1 s), leaving the second worker nothing; documented, and the test crawl paced.
- Benchmarks: in-memory 229,166 vs disk queue 187,002 pages/min (same session, 16 concurrent requests).

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
