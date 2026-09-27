using ScrapyNet.AI;
using ScrapyNet.Platform;
using ScrapyNet.Playwright;
using ScrapyNet.Sandbox;

namespace ScrapyNet.Samples;

// ---- rag: website → chunks → vectors → answers -----------------------------------------------------------

public sealed class NewsSpider(string url) : Spider
{
    public override string Name => "news";
    public override IReadOnlyList<string> StartUrls => [url];

    public override async IAsyncEnumerable<object> Parse(Response response)
    {
        foreach (var r in response.FollowAllCss("article.teaser h2 a", ParseArticle)) yield return r;
        await Task.CompletedTask;
    }

    private IEnumerable<object> ParseArticle(Response response)
    {
        // Hand the raw HTML to the RAG pipeline; it extracts the article and drops the boilerplate.
        yield return new Dictionary<string, object?> { ["url"] = response.Url, ["html"] = response.Text };
    }
}

public static class RagSample
{
    public static async Task RunAsync(SandboxSite site)
    {
        // Local hashing embeddings work offline. For semantic embeddings use OpenAiEmbeddingGenerator
        // (OpenAI, Azure OpenAI, Ollama, LM Studio...).
        var kb = new KnowledgeBase(new HashingEmbeddingGenerator(512), chunking: new ChunkingOptions { MaxWords = 120, OverlapWords = 20 });

        // Optional: set SCRAPYNET_DEEPSEEK_KEY (or wire in any ILanguageModel) for generated answers.
        if (Environment.GetEnvironmentVariable("SCRAPYNET_DEEPSEEK_KEY") is { } key)
            kb = new KnowledgeBase(kb.Embeddings, kb.Store, kb.Chunking) { LanguageModel = new OpenAiChatModel("deepseek-v4-flash", key, "https://api.deepseek.com/v1/") };

        var categorizer = new KeywordCategorizer(new Dictionary<string, string[]>
        {
            ["ai"] = ["embedding", "embeddings", "vector", "model", "retrieval", "language"],
            ["crawling"] = ["crawler", "proxy", "robots", "spider", "delay", "sitemap"],
            ["operations"] = ["monitoring", "schedule", "alerts", "cron", "statistics"],
        });
        var settings = SampleRegistry.Defaults("WARNING");
        settings.ItemPipelines.Add(new RagIngestionPipeline(kb) { Categorizer = categorizer, Summarizer = new ExtractiveSummarizer() }, 800);
        var result = await Scrapy.CrawlAsync(new NewsSpider(site.Url("/news/")), settings);

        Console.WriteLine($"ingested {kb.Stats.Documents} articles into {kb.Stats.Chunks} chunks; skipped {kb.Stats.NearDuplicates} near-duplicate(s)");
        foreach (var question in new[] { "How do I avoid getting proxies banned?", "What is retrieval-augmented generation?" })
        {
            var answer = await kb.AskAsync(question, top: 3);
            Console.WriteLine($"\nQ: {question}");
            foreach (var hit in answer.Sources)
                Console.WriteLine($"  {hit.Score:0.000}  [{hit.Record.Metadata.GetValueOrDefault("category")}] {hit.Record.Metadata.GetValueOrDefault("title")}");
            Console.WriteLine($"A: {answer.Answer}");
        }
        _ = result;
    }
}

// ---- platform: control plane ----------------------------------------------------------------------------

public static class PlatformSample
{
    public static async Task RunAsync(SandboxSite site)
    {
        var catalog = new SpiderCatalog();
        // A spider defined purely as data — this JSON could come from a UI or the REST API.
        catalog.Create(new SpiderDefinition
        {
            Id = "quotes",
            Name = "quotes",
            Description = "Quotes with authors and tags",
            Target = new TargetConfig { StartUrls = [site.Url("/quotes/")] },
            Rules = new ScrapingRules
            {
                ItemSelector = "div.quote",
                Fields = new()
                {
                    ["text"] = new FieldRule { Css = "span.text::text", Processors = ["clean"] },
                    ["author"] = new FieldRule { Css = "small.author::text" },
                    ["tags"] = new FieldRule { Css = "a.tag::text", Multiple = true },
                },
                FollowLinks = ["li.next a"],
            },
        });
        catalog.Clone("quotes", "quotes-books-tag");

        await using var jobs = new JobManager(catalog, new JobManagerOptions
        {
            BaseSettings = () => SampleRegistry.Defaults("WARNING"),
            DataDirectory = SampleRegistry.OutputDir("platform"),
        });
        using var monitor = new CrawlMonitor(jobs);
        _ = new NotificationDispatcher(jobs).Route(new CallbackNotifier(a =>
        {
            Console.WriteLine($"  alert: {a}");
            return Task.CompletedTask;
        }), AlertSeverity.Info);

        await using var scheduler = new CrawlScheduler(jobs);
        var schedule = scheduler.Add(new ScheduleEntry { Id = "hourly", SpiderId = "quotes", Cron = Schedules.Hourly(15) });
        Console.WriteLine($"next scheduled run: {schedule.NextRun:u} (cron '{schedule.Cron}')");

        var job = jobs.RunNow("quotes");
        // The dispatcher evaluates alerts automatically when a job finishes.
        var done = await jobs.WaitAsync(job.Id);
        Console.WriteLine($"job {done.Id}: {done.Status}, {done.ItemCount} items → {done.OutputPath}");

        var snapshot = monitor.Snapshot();
        foreach (var s in snapshot.Spiders) Console.WriteLine($"  spider {s.Name,-18} last={s.LastStatus} runs={s.Runs} items={s.LastItems}");
    }
}

// ---- browser --------------------------------------------------------------------------------------------

public sealed class JsQuotesSpider(string url) : Spider
{
    public override string Name => "js_quotes";

    public override async IAsyncEnumerable<Request> StartAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        // Only this request goes to the browser; everything else stays plain HTTP.
        yield return new Request(url).WithPlaywright(PageMethod.WaitForSelector("div.quote"), PageMethod.Screenshot());
        await Task.CompletedTask;
    }

    public override async IAsyncEnumerable<object> Parse(Response response)
    {
        foreach (var q in response.Css("div.quote"))
            yield return new { Text = q.Css("span.text::text").Get(), Author = q.Css("small.author::text").Get() };
        if (response.Meta.GetValueOrDefault(PlaywrightMeta.Screenshot) is byte[] png)
        {
            var path = Path.Combine(SampleRegistry.OutputDir("browser"), "quotes-js.png");
            await File.WriteAllBytesAsync(path, png);
            Console.WriteLine($"screenshot saved to {path}");
        }
    }
}

public static class BrowserSample
{
    public static async Task RunAsync(SandboxSite site)
    {
        var settings = SampleRegistry.Defaults("WARNING").UsePlaywright(blockResourceTypes: ["image", "font"]);
        var (result, items) = await Scrapy.CrawlAndCollectAsync(new JsQuotesSpider(site.Url("/quotes/js/")), settings);
        Console.WriteLine($"{items.Count} quotes rendered by JavaScript. First: {(items.Count > 0 ? ItemAdapter.For(items[0]) : "none — are Playwright browsers installed?")}");
        _ = result;
    }
}
