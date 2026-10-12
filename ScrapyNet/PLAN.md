# PLAN — Scrapy.Net roadmap / peta jalan

*Dibuat oleh Gravicode Studios dipimpin oleh Kang Fadhil.*

Status per phase. ✅ done · 🔜 next · 💡 considered

## Phase 1 — Core framework ✅

- ✅ Request/Response model, encoding detection, response types (HTML, XML, JSON, text, binary)
- ✅ Selectors: CSS with `::text`/`::attr()`, XPath 1.0, `has-class`, EXSLT regex, variables, namespaces
- ✅ Items (POCO, dictionary, declared `Item`), `ItemAdapter`, `ItemLoader<T>`, processors, transforms
- ✅ Spiders: `Spider`, `CrawlSpider`, `SitemapSpider`, `XmlFeedSpider`, `CsvFeedSpider`
- ✅ Engine, scheduler (LIFO/FIFO, priorities), fingerprint dupe filter, downloader slots, scraper
- ✅ Downloader middlewares: retry, redirect, meta refresh, cookies, proxy, proxy rotation, robots.txt,
  HTTP cache, auth, headers, per-domain headers, user agents, stats, offsite, timeout
- ✅ Spider middlewares: HTTP error, offsite, referer, URL length, depth
- ✅ Pipelines: files, images, cleaning, validation, dedup, ADO.NET, REST API, Elasticsearch
- ✅ Feed exports: JSON, JSON Lines, CSV, TSV, XML; file/stdout/memory/HTTP PUT; gzip; batches
- ✅ Extensions: core stats, log stats, memory usage, close spider, AutoThrottle, spider state
- ✅ Settings with priorities, JSON project files, environments, env vars
- ✅ Signals, stats, Scrapy-style logging, pause/resume (`JOBDIR`)

## Phase 2 — Tooling ✅

- ✅ `scrapynet` CLI: startproject, genspider (5 templates), crawl, list, parse, fetch, view, shell,
  settings, version, bench, sandbox
- ✅ In-project command runner, .NET 10 single-file spiders
- ✅ Sandbox practice site (offline, deterministic, with failure modes and proxy mode)

## Phase 3 — Extensions ✅

- ✅ Playwright rendering (page methods, contexts, resource blocking, screenshots)
- ✅ AI: extraction, chunking, SimHash, change detection, embeddings, vector stores, summarizers,
  categorizers, RAG knowledge base, OpenAI/Azure/DeepSeek/Ollama clients
- ✅ Platform: catalog with versions, declarative spiders, jobs, cron, secrets, monitoring, alerts,
  notifications, raw content archive
- ✅ Server: REST API with API-key roles

## Phase 4 — Learning material ✅

- ✅ Scrapy Gallery (Avalonia) with 21 demos and headless screenshot generation
- ✅ 18 console samples, single-file spider, server host
- ✅ 6 .NET Interactive notebooks with verification programs
- ✅ Documentation in English and Bahasa Indonesia (16 chapters each)
- ✅ Benchmarks

## Phase 5 — Release ✅

- ✅ Test suite (xunit.v3), all green offline; live LLM tests opt-in
- ✅ Moved to `Vibe_Library/ScrapyNet`, CI workflows, published `Gravicode.ScrapyNet*` 0.1.0 to NuGet

## Phase 6 — 0.2.0 ✅

- ✅ Source-generated item accessors (`[ScrapyItem]`) for trimming and NativeAOT; `samples/AotSpider`
- ✅ Disk-backed scheduler queue: `JOBDIR` journal written as requests are scheduled, crash-safe
- ✅ Distributed scheduling over Redis (`Gravicode.ScrapyNet.Redis`, scrapy-redis equivalent)
- ✅ HTTP/3 (`DOWNLOAD_HTTP_VERSION`, `DOWNLOAD_HTTP_VERSION_POLICY`)
- ✅ Web admin UI on top of `ScrapyNet.Server` (the Scrapy.Net Console, `MapScrapyNetAdmin`)
- ✅ Spider contracts and `scrapynet check`
- ✅ pgvector and Azure AI Search vector stores; `Microsoft.Extensions.AI` adapters both ways
- ✅ Gallery demos for contracts and distributed crawling; docs chapter 16; CI jobs for Redis, pgvector and NativeAOT

## Later 💡

- 💡 Trim-safe component registration (no `preserve="all"` descriptor for built-in components)
- 💡 Microsoft.Extensions.VectorData adapter for `IVectorStore`
- 💡 Console: live log tail per job and editing spiders with a form as well as JSON
- 💡 Other shared queues for distributed crawls (RabbitMQ, Azure Service Bus)
