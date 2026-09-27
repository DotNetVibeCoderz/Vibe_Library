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
- ✅ Documentation in English and Bahasa Indonesia (15 chapters each)
- ✅ Benchmarks

## Phase 5 — Release ✅

- ✅ Test suite (xunit.v3), all green offline; live LLM tests opt-in
- ✅ Moved to `Vibe_Library/ScrapyNet`, CI workflows, published `Gravicode.ScrapyNet*` 0.1.0 to NuGet

## Later 💡

- 💡 Source-generated item accessors for NativeAOT
- 💡 Disk-backed scheduler queue (persist continuously, not only on close)
- 💡 Distributed scheduling over Redis (scrapy-redis equivalent)
- 💡 HTTP/3 download handler
- 💡 Web admin UI on top of `ScrapyNet.Server`
- 💡 Spider contracts (`scrapy check`)
- 💡 More vector stores (pgvector, Azure AI Search) and `Microsoft.Extensions.AI` adapters
