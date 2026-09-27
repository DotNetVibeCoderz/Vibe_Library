using ScrapyNet.Sandbox;

namespace ScrapyNet.Samples;

public sealed record Sample(string Name, string Description, Func<SandboxSite, Task> Run, bool Online = false);

public static class SampleRegistry
{
    public static readonly IReadOnlyList<Sample> All =
    [
        new("quotes", "Basic spider: CSS selectors, pagination, JSON export", QuotesSample.RunAsync),
        new("books", "CrawlSpider + LinkExtractor rules + typed ItemLoader + CSV export", BooksSample.RunAsync),
        new("login", "Log in with FormRequest.FromResponse (CSRF) and crawl behind the session", LoginSample.RunAsync),
        new("api", "JSON API with API-key headers per domain and cursor pagination", ApiSample.RunAsync),
        new("sitemap", "SitemapSpider reading robots.txt, sitemap index and gzipped sitemaps", SitemapSample.RunAsync),
        new("feeds", "XmlFeedSpider and CsvFeedSpider for product feeds", FeedsSample.RunAsync),
        new("media", "FilesPipeline and ImagesPipeline downloading PDFs and images", MediaSample.RunAsync),
        new("pipelines", "Validation, cleaning, dedup and SQLite storage pipelines", PipelinesSample.RunAsync),
        new("middleware", "Custom downloader middleware, random user agents, retries and redirects", MiddlewareSample.RunAsync),
        new("proxies", "Proxy rotation with ban detection across two local proxies", ProxySample.RunAsync),
        new("throttle", "AutoThrottle adapting download delay to server latency", ThrottleSample.RunAsync),
        new("resume", "Pause with JOBDIR and resume where the crawl stopped", ResumeSample.RunAsync),
        new("cache", "HTTP cache: second run replays from disk without network", CacheSample.RunAsync),
        new("signals", "Signals and stats: live progress and the final stats dump", SignalsSample.RunAsync),
        new("rag", "AI ingestion: article extraction, chunking, dedup, embeddings, RAG search", RagSample.RunAsync),
        new("platform", "Platform: declarative spider, jobs, cron schedule, alerts, dashboard", PlatformSample.RunAsync),
        new("browser", "Playwright rendering of a JavaScript page (needs installed browsers)", BrowserSample.RunAsync),
        new("toscrape", "The classic Scrapy tutorial on the real quotes.toscrape.com (online)", ToScrapeSample.RunAsync, Online: true),
    ];

    /// <summary>Settings shared by samples: readable logs, no periodic stats.</summary>
    public static Settings Defaults(string logLevel = "INFO")
    {
        var s = new Settings();
        s.Set(SettingKeys.LogLevel, logLevel);
        s.Set(SettingKeys.LogStatsInterval, 0);
        s.Set(SettingKeys.StatsDump, false);
        return s;
    }

    public static string OutputDir(string name)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "output", name);
        Directory.CreateDirectory(dir);
        return dir;
    }
}
