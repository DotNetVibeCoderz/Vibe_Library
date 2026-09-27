# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

**Scrapy.Net**: a .NET 10 port of Python Scrapy (https://github.com/scrapy/scrapy). Scrapy's docs
(https://docs.scrapy.org/) are the behavioural reference: component names, setting keys, stats keys
and lifecycle deliberately match. `requirements.md` (Bahasa Indonesia) is the original brief;
`features.md` the feature list; `PLAN.md` the roadmap; `Progress.md` the log — keep `Progress.md`
current as work lands.

Every doc exists in English (`docs/en`) and Bahasa Indonesia (`docs/id`); change both together. Docs,
package descriptions and the gallery must credit "Dibuat oleh Gravicode Studios dipimpin oleh Kang Fadhil".

## Commands

```bash
dotnet build ScrapyNet.slnx
dotnet run --project tests/ScrapyNet.Tests                                  # all tests (~3 s, offline)
dotnet run --project tests/ScrapyNet.Tests -- --filter-class '*CrawlTests*'  # one class
dotnet run --project tests/ScrapyNet.Tests -- --filter-method '*Jobdir*'     # one test
dotnet run --project samples/ScrapyNet.Samples -- all                       # every offline sample
dotnet run --project samples/ScrapyGallery                                  # the Avalonia gallery
dotnet run --project samples/ScrapyGallery -- --screenshots docs/images     # regenerate doc screenshots (headless)
node tools/notebooks/build.mjs                                              # regenerate notebooks + verify programs
dotnet run tools/notebooks/verify/05-ai-rag.cs                              # run a notebook's code
dotnet run -c Release --project benchmarks/ScrapyNet.Benchmarks -- --filter '*' --job short
dotnet run -c Release --project benchmarks/ScrapyNet.Benchmarks -- crawl    # end-to-end pages/min
dotnet pack ScrapyNet.slnx -c Release -o artifacts
```

**`dotnet test` does not work**: xunit.v3 runs on Microsoft.Testing.Platform, which the .NET 10 SDK no
longer bridges through VSTest. Run the test project with `dotnet run` (`dotnet.config` exists for this).

Live LLM tests (`LiveLlmTests`) are skipped unless `SCRAPYNET_AZURE_OPENAI_ENDPOINT`/`_KEY`/`_DEPLOYMENT`
or `SCRAPYNET_DEEPSEEK_KEY` (+ `SCRAPYNET_DEEPSEEK_MODEL`) are set. Never write keys into files.

## Layout

```
src/ScrapyNet.Core        Gravicode.ScrapyNet — the framework (namespace ScrapyNet + sub-namespaces)
  Http/                   Request (record), Response, TextResponse/Html/Xml/Json, FormRequest, encoding
  Selectors/              Selector over AngleSharp (+AngleSharp.XPath), CSS pseudo-elements, XPath extensions
  Items/, Loaders/        Item, ItemAdapter (compiled accessors), ItemLoader<T>, processors, Transforms
  Spiders/                Spider, CrawlSpider/Rule, SitemapSpider, XML/CSV feed spiders
  Engine/, Downloader/    ExecutionEngine, Scheduler, RFPDupeFilter, fingerprints, Downloader + slots, handlers
  DownloaderMiddlewares/, SpiderMiddlewares/, Pipelines/, Exporters/, Extensions/
  Settings/               Settings (priorities), ComponentDictionary, FeedCollection, DefaultSettings, SettingKeys
  Crawling/               Crawler, CrawlerRunner/CrawlerProcess, SpiderLoader, Scrapy (static helpers)
  Cmdline/                ScrapyCommandLine (in-project runner), Fetcher, ScrapyShell, Templates
src/ScrapyNet.Sandbox     offline practice site on a raw-socket HTTP server (tests, samples, gallery, CLI)
src/ScrapyNet.AI          extraction, chunking, SimHash, embeddings, vector stores, LLM clients, RAG
src/ScrapyNet.Platform    catalog, declarative spiders, jobs, cron, secrets, monitor, alerts, raw content
src/ScrapyNet.Playwright  browser download handler
src/ScrapyNet.Server      ASP.NET Core minimal API over the platform
src/ScrapyNet.Cli         `scrapynet` dotnet tool (forwards crawl/list/parse to `dotnet run` in a project)
samples/                  console samples, single-file spider, server host, ScrapyGallery (Avalonia)
tests/ScrapyNet.Tests     xunit.v3; one SandboxSite shared via AssemblyFixture
```

## Design points that span files

- **Callbacks** are `Func<Response, IAsyncEnumerable<object>>`; sync iterators are wrapped by
  `Callbacks.FromSync` in adapter objects so `Callbacks.GetName` can still recover the spider method
  name — that name is what `RequestSerializer` stores for `JOBDIR`. Lambda callbacks can't be persisted.
- **Components** (middlewares, pipelines, extensions, handlers) are built by `ComponentFactory`, which
  injects constructor parameters (`Crawler`, `Settings`, `IStatsCollector`, `SignalManager`, `ILogger`...).
  Throwing `NotConfiguredException` from a constructor disables the component — built-ins rely on this.
- **Middleware results** use `DownloadResult` (implicit from `Response`/`Request`); in non-async methods
  return `(DownloadResult)response` because C# won't chain two user-defined conversions into `ValueTask`.
- **Settings** are copied into each `Crawler` and frozen at `CrawlAsync`; `Spider.ConfigureSettings`
  receives an empty `Settings` that is merged at Spider priority. Component registries merge, not replace.
- **Engine idleness**: `_processing` counts requests from dequeue until scraping ends; the loop waits on
  a semaphore woken by every scheduling/completion, with a 1 s heartbeat.
- **JSON**: always pass `ScrapyJson.Default`/`Web` (explicit `DefaultJsonTypeInfoResolver`). .NET 10
  file-based apps disable reflection JSON by default; bare `JsonSerializer.Serialize(x)` breaks there.
  Exporters write primitives directly with `Utf8JsonWriter`.
- **AngleSharp.XPath quirks** handled in `Selector.Xpath`: scalar functions of the document root return
  null or throw, so they are re-evaluated on the root element.
- **Byte signatures**: never write `"\x89PNG"u8` — `\x` escapes are code points, UTF-8-encoded to
  multiple bytes. Use byte arrays.
- `WebProxy` bypasses loopback; `HttpDownloadHandler` uses its own non-bypassing `IWebProxy`.
- The sandbox's data is deterministic (seeded); tests assert exact counts against `SandboxData`.

## Gallery

`samples/ScrapyGallery`: each demo's shown code is the region between `// <demo>` and `// </demo>` in
its own source file (embedded as a resource), so shown code always equals running code. The design
(palette "silk/ink/thread/Argiope amber", IBM Plex fonts, the crawl web as the signature element) is
documented in `Controls/Palette.cs` and `Controls/CrawlWebView.cs`; keep new UI within it.

## Publishing (NuGet)

- The project lives in the monorepo `C:\experiment\VibeCoding\Vibe_Library\ScrapyNet`; workflows are
  `.github/workflows/scrapynet-ci.yml` and `scrapynet-publish.yml` at the monorepo root, tags `ScrapyNet-v*`.
- The NuGet API key is in `C:\Users\mifma\Documents\CodeSandbox\PackageCredentials.txt`. Read it only at
  publish time; never copy it into the repo, docs, or commit messages.
