using System.Net;
using System.Security.Cryptography;
using System.Text;
using ScrapyNet.Platform;
using ScrapyNet.Sandbox;
using Xunit;

namespace ScrapyNet.Tests;

public class PlatformTests(SandboxFixture sandbox)
{
    private static Settings Quiet()
    {
        var s = new Settings();
        TestSettings.Quiet(s);
        return s;
    }

    private SpiderDefinition BooksDefinition(string id = "books") => new()
    {
        Id = id,
        Name = id,
        Target = new TargetConfig { StartUrls = [sandbox.Url("/books/")], IncludePatterns = ["/books/"] },
        Rules = new ScrapingRules
        {
            DetailLinks = ["article.product_pod h3 a"],
            FollowLinks = ["li.next a"],
            Fields = new()
            {
                ["title"] = new FieldRule { Css = "h1::text", Processors = ["clean"] },
                ["price"] = new FieldRule { Css = "p.price_color::text", Processors = ["price"] },
                ["upc"] = new FieldRule { Xpath = "//th[text()='UPC']/following-sibling::td/text()" },
                ["image"] = new FieldRule { Css = "div.item img::attr(src)", Processors = ["absolute_url"] },
            },
            RequiredFields = ["title", "price"],
        },
    };

    [Fact]
    public async Task Declarative_spider_scrapes_without_code()
    {
        var spider = new DeclarativeSpider(BooksDefinition());
        var (_, items) = await Scrapy.CrawlAndCollectAsync(spider, TestSettings.Quiet, TestContext.Current.CancellationToken);
        Assert.Equal(SandboxData.Books.Count, items.Count);
        var first = items.Cast<Dictionary<string, object?>>().First(i => (string)i["title"]! == SandboxData.Books[0].Title);
        Assert.Equal(SandboxData.Books[0].Price, first["price"]);
        Assert.StartsWith(sandbox.Site.BaseUrl, (string)first["image"]!);
    }

    [Fact]
    public void Rule_evaluator_previews_item_containers()
    {
        var html = new HtmlResponse(sandbox.Url("/x"), "<div class='q'><b>A</b><i>1,5</i></div><div class='q'><b>B</b></div>");
        var rules = new ScrapingRules
        {
            ItemSelector = "div.q",
            Fields = new() { ["name"] = new FieldRule { Css = "b::text" }, ["score"] = new FieldRule { Css = "i::text", Processors = ["price"], Default = "n/a" } },
        };
        var items = RuleEvaluator.Extract(rules, html.Selector, html.Url);
        Assert.Equal(2, items.Count);
        Assert.Equal(1.5m, items[0]["score"]);
        Assert.Equal("n/a", items[1]["score"]);
    }

    [Fact]
    public void Catalog_versions_clone_rollback_and_persistence()
    {
        var path = Path.Combine(TestSettings.TempDir(), "catalog.json");
        var catalog = new SpiderCatalog(path);
        catalog.Create(BooksDefinition());
        catalog.Update("books", d => d with { Description = "v2" });
        catalog.Disable("books");
        Assert.Equal(3, catalog.Get("books").Version);
        Assert.False(catalog.Get("books").Enabled);
        catalog.Rollback("books", 1);
        Assert.True(catalog.Get("books").Enabled);
        Assert.Null(catalog.Get("books").Description);
        var clone = catalog.Clone("books", "books-copy");
        Assert.Equal(1, clone.Version);

        var reloaded = new SpiderCatalog(path);
        Assert.Equal(4, reloaded.Get("books").Version);
        Assert.Equal(2, reloaded.List().Count);
        Assert.True(reloaded.Delete("books-copy"));
    }

    [Fact]
    public async Task Job_manager_runs_retries_and_keeps_history()
    {
        var dir = TestSettings.TempDir();
        var catalog = new SpiderCatalog();
        catalog.Create(BooksDefinition());
        await using var jobs = new JobManager(catalog, new JobManagerOptions
        {
            BaseSettings = Quiet,
            HistoryPath = Path.Combine(dir, "history.jsonl"),
            DataDirectory = Path.Combine(dir, "data"),
        });
        var started = new List<string>();
        jobs.JobStarted += j => started.Add(j.Id);

        var job = jobs.RunNow("books");
        var done = await jobs.WaitAsync(job.Id, TestContext.Current.CancellationToken);
        Assert.Equal(JobStatus.Completed, done.Status);
        Assert.Equal(SandboxData.Books.Count, done.ItemCount);
        Assert.Equal(SandboxData.Books.Count, File.ReadAllLines(done.OutputPath!).Length);

        var retry = await jobs.WaitAsync(jobs.Retry(job.Id).Id, TestContext.Current.CancellationToken);
        Assert.Equal(2, retry.Attempt);
        Assert.Equal($"retry:{job.Id}", retry.Trigger);
        Assert.Equal(2, started.Count);

        var reopened = new JobManager(catalog, new JobManagerOptions { HistoryPath = Path.Combine(dir, "history.jsonl") });
        Assert.Equal(2, reopened.History("books").Count);
    }

    [Fact]
    public async Task Jobs_can_be_paused_resumed_and_stopped()
    {
        var catalog = new SpiderCatalog();
        catalog.Create(new SpiderDefinition
        {
            Id = "slow",
            Name = "slow",
            Target = new TargetConfig { StartUrls = [.. Enumerable.Range(0, 40).Select(i => sandbox.Url($"/slow?ms=60&i={i}"))] },
            Rules = new ScrapingRules { Fields = new() { ["text"] = new FieldRule { Css = "p.slow::text" } } },
            Settings = new() { [SettingKeys.ConcurrentRequests] = 2 },
        });
        await using var jobs = new JobManager(catalog, new JobManagerOptions { BaseSettings = Quiet });
        var job = jobs.RunNow("slow");
        await Task.Delay(250, TestContext.Current.CancellationToken);
        jobs.Pause(job.Id);
        Assert.Equal(JobStatus.Paused, jobs.Get(job.Id).Status);
        var crawler = jobs.GetCrawler(job.Id)!;
        await Task.Delay(200, TestContext.Current.CancellationToken);
        var countWhilePaused = crawler.Stats.GetCount("response_received_count");
        await Task.Delay(300, TestContext.Current.CancellationToken);
        Assert.InRange(crawler.Stats.GetCount("response_received_count") - countWhilePaused, 0, 2);
        jobs.Resume(job.Id);
        await Task.Delay(150, TestContext.Current.CancellationToken);
        await jobs.StopAsync(job.Id);
        var done = await jobs.WaitAsync(job.Id, TestContext.Current.CancellationToken);
        Assert.Equal(JobStatus.Stopped, done.Status);
        Assert.InRange(done.ItemCount, 1, 39);
    }

    [Theory]
    [InlineData("*/15 * * * *", "2026-03-10T10:07:00Z", "2026-03-10T10:15:00Z")]
    [InlineData("0 9 * * MON-FRI", "2026-03-13T09:30:00Z", "2026-03-16T09:00:00Z")]
    [InlineData("30 2 1 * *", "2026-01-15T00:00:00Z", "2026-02-01T02:30:00Z")]
    [InlineData("@daily", "2026-03-10T23:59:00Z", "2026-03-11T00:00:00Z")]
    [InlineData("0 0 * * 7", "2026-03-10T00:00:00Z", "2026-03-15T00:00:00Z")]
    [InlineData("0 12 13 * FRI", "2026-03-01T00:00:00Z", "2026-03-06T12:00:00Z")]
    public void Cron_next_occurrence(string cron, string after, string expected) =>
        Assert.Equal(DateTimeOffset.Parse(expected), new CronExpression(cron).GetNextOccurrence(DateTimeOffset.Parse(after)));

    [Fact]
    public void Cron_rejects_invalid_expressions()
    {
        Assert.False(CronExpression.TryParse("61 * * * *", out _));
        Assert.False(CronExpression.TryParse("* * *", out _));
        Assert.Equal("15 6 * * 1", Schedules.Weekly(DayOfWeek.Monday, 6, 15));
    }

    [Fact]
    public async Task Scheduler_triggers_due_schedules_once()
    {
        var catalog = new SpiderCatalog();
        catalog.Create(BooksDefinition("sched") with { Target = new TargetConfig { StartUrls = [sandbox.Url("/books/")] }, Rules = new ScrapingRules { Fields = new() { ["h"] = new FieldRule { Css = "h1::text" } } } });
        await using var jobs = new JobManager(catalog, new JobManagerOptions { BaseSettings = Quiet });
        await using var scheduler = new CrawlScheduler(jobs);
        var entry = scheduler.Add(new ScheduleEntry { Id = "every-5", SpiderId = "sched", Cron = Schedules.EveryMinutes(5) });
        Assert.NotNull(entry.NextRun);
        Assert.Empty(await scheduler.TickAsync(entry.NextRun!.Value.AddSeconds(-1)));
        var started = await scheduler.TickAsync(entry.NextRun!.Value.AddSeconds(1));
        var job = Assert.Single(started);
        Assert.Equal("schedule:every-5", job.Trigger);
        await jobs.WaitAsync(job.Id, TestContext.Current.CancellationToken);
        Assert.True(scheduler.List()[0].NextRun > entry.LastRun);
    }

    [Fact]
    public async Task Secrets_are_encrypted_and_resolved_into_job_settings()
    {
        var path = Path.Combine(TestSettings.TempDir(), "secrets.json");
        var store = SecretStore.Open("correct horse", path);
        store.Set("sandbox-api", SecretKind.ApiKey, SandboxSite.ApiKey);
        store.SetLogin("sandbox-login", SandboxSite.Username, SandboxSite.Password);
        Assert.DoesNotContain(SandboxSite.ApiKey, File.ReadAllText(path));
        Assert.Throws<CryptographicException>(() => SecretStore.Open("wrong", path));
        var reopened = SecretStore.Open("correct horse", path);
        Assert.Equal("net", reopened.ResolvePlaceholders("${secret:sandbox-login:password}"));

        var catalog = new SpiderCatalog();
        catalog.Create(new SpiderDefinition
        {
            Id = "api",
            Name = "api",
            Target = new TargetConfig { StartUrls = [sandbox.Url("/api/secure/products?page=1")] },
            Rules = new ScrapingRules { Fields = new() { ["body"] = new FieldRule { Xpath = "string(.)" } } },
            Settings = new() { [SettingKeys.DomainHeaders] = new Dictionary<string, object?> { ["127.0.0.1"] = new Dictionary<string, object?> { ["X-Api-Key"] = "${secret:sandbox-api}" } } },
        });
        await using var jobs = new JobManager(catalog, new JobManagerOptions { BaseSettings = Quiet, Secrets = reopened });
        var job = await jobs.WaitAsync(jobs.RunNow("api").Id, TestContext.Current.CancellationToken);
        Assert.Equal(1, job.ItemCount);
        Assert.False(job.Stats.ContainsKey("downloader/response_status_count/401"));
    }

    [Fact]
    public async Task Alerts_detect_zero_items_auth_failures_and_decreases()
    {
        var catalog = new SpiderCatalog();
        catalog.Create(new SpiderDefinition
        {
            Id = "locked",
            Name = "locked",
            Target = new TargetConfig { StartUrls = [sandbox.Url("/api/secure/products")] },
            Rules = new ScrapingRules { Fields = new() { ["x"] = new FieldRule { Css = "h1::text" } } },
        });
        await using var jobs = new JobManager(catalog, new JobManagerOptions { BaseSettings = Quiet });
        var slack = new RecordingHandler();
        var received = new List<Alert>();
        var dispatcher = new NotificationDispatcher(jobs)
            .Route(new CallbackNotifier(a => { lock (received) received.Add(a); return Task.CompletedTask; }), AlertSeverity.Info)
            .Route(new SlackNotifier("https://hooks.slack.test/x", new HttpClient(slack)), AlertSeverity.Critical);
        var job = await jobs.WaitAsync(jobs.RunNow("locked").Id, TestContext.Current.CancellationToken);
        await dispatcher.DispatchAsync(job);
        Assert.Contains(received, a => a.Type == AlertType.ZeroItems);
        Assert.Contains(received, a => a.Type == AlertType.AuthenticationExpired);
        Assert.Contains("AuthenticationExpired", slack.Bodies.First());

        var evaluator = new AlertEvaluator();
        JobRecord Run(string id, long items, int minutesAgo) => new()
        {
            Id = id, SpiderId = "s", SpiderName = "s", Status = JobStatus.Completed, ItemCount = items,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-minutesAgo), FinishedAt = DateTimeOffset.UtcNow.AddMinutes(-minutesAgo + 1),
        };
        var history = new List<JobRecord> { Run("a", 100, 40), Run("b", 110, 30), Run("c", 95, 20) };
        var alerts = evaluator.Evaluate(Run("d", 30, 1), history);
        Assert.Contains(alerts, a => a.Type == AlertType.AbnormalItemDecrease);
        Assert.DoesNotContain(evaluator.Evaluate(Run("e", 90, 1), history), a => a.Type == AlertType.AbnormalItemDecrease);
    }

    [Fact]
    public async Task Monitor_tracks_live_metrics_and_dashboard()
    {
        var catalog = new SpiderCatalog();
        catalog.Create(BooksDefinition());
        await using var jobs = new JobManager(catalog, new JobManagerOptions { BaseSettings = Quiet });
        using var monitor = new CrawlMonitor(jobs, TimeSpan.FromMilliseconds(50));
        var job = await jobs.WaitAsync(jobs.RunNow("books").Id, TestContext.Current.CancellationToken);
        var metrics = monitor.Get(job.Id)!;
        Assert.Equal(SandboxData.Books.Count, metrics.Items);
        Assert.True(metrics.Responses > SandboxData.Books.Count);
        Assert.Equal(metrics.Responses, metrics.StatusCounts[200]);
        Assert.NotEmpty(metrics.Series);
        var snapshot = monitor.Snapshot();
        Assert.Equal(1, snapshot.CompletedJobs);
        Assert.Equal(JobStatus.Completed, snapshot.Spiders.Single().LastStatus);
    }

    [Fact]
    public async Task Raw_content_is_archived_and_reloadable()
    {
        var dir = TestSettings.TempDir();
        await Scrapy.CrawlAsync(new QuotesSpider(sandbox.Site.BaseUrl), s =>
        {
            TestSettings.Quiet(s);
            s.Set(RawContentExtension.SettingKey, dir);
            s.Extensions.Add<RawContentExtension>(0);
        }, TestContext.Current.CancellationToken);
        var entries = new RawContentArchive(dir).Entries("quotes").ToList();
        Assert.Equal(SandboxData.QuotePages, entries.Count);
        var response = RawContentArchive.Load(entries[0]);
        Assert.Equal(10, response.Css("div.quote").Count);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
