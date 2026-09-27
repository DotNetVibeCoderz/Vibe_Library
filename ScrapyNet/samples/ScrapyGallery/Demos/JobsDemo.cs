using ScrapyGallery.Infrastructure;
using ScrapyNet;
using ScrapyNet.Platform;

namespace ScrapyGallery.Demos;

public sealed class JobsDemo : Demo
{
    public override DemoGroup Group => DemoGroup.Platform;
    public override string Title => "Jobs, schedules and alerts";
    public override string TitleId => "Job, jadwal, dan peringatan";
    public override string Summary => "The control plane: a catalog of spiders, a job manager that runs them with a concurrency limit, cron schedules, live metrics, and alerts when a run fails, finds nothing or drops sharply against recent history.";
    public override string SummaryId => "Control plane: katalog spider, job manager yang menjalankannya dengan batas konkurensi, jadwal cron, metrik langsung, dan peringatan saat run gagal, kosong, atau turun tajam dibanding riwayat.";
    public override IReadOnlyList<string> Concepts => ["SpiderCatalog", "JobManager", "CrawlScheduler", "CronExpression", "NotificationDispatcher"];

    // <demo>
    public static SpiderCatalog BuildCatalog(string site)
    {
        var catalog = new SpiderCatalog();
        catalog.Create(new SpiderDefinition
        {
            Id = "quotes",
            Name = "quotes",
            Target = new TargetConfig { StartUrls = [site + "/quotes/"] },
            Rules = new ScrapingRules
            {
                ItemSelector = "div.quote",
                Fields = new() { ["author"] = new FieldRule { Css = "small.author::text" } },
                FollowLinks = ["li.next a"],
            },
        });
        // A clone pointed at a page with nothing to scrape triggers the "zero items" alert.
        catalog.Clone("quotes", "quotes-broken");
        catalog.Update("quotes-broken", d => d with { Target = new TargetConfig { StartUrls = [site + "/status/200"] } });
        // A spider behind a login it doesn't have triggers "authentication expired".
        catalog.Clone("quotes", "account");
        catalog.Update("account", d => d with { Target = new TargetConfig { StartUrls = [site + "/api/secure/products"] } });
        return catalog;
    }

    public static (JobManager Jobs, CrawlScheduler Scheduler, NotificationDispatcher Alerts) StartPlatform(SpiderCatalog catalog, Func<Settings> settings)
    {
        var jobs = new JobManager(catalog, new JobManagerOptions { MaxConcurrentJobs = 2, BaseSettings = settings });
        var scheduler = new CrawlScheduler(jobs);
        scheduler.Add(new ScheduleEntry { Id = "quotes-hourly", SpiderId = "quotes", Cron = Schedules.Hourly(minute: 15) });
        scheduler.Add(new ScheduleEntry { Id = "account-daily", SpiderId = "account", Cron = Schedules.Daily(hour: 6) });
        var alerts = new NotificationDispatcher(jobs);
        return (jobs, scheduler, alerts);
    }
    // </demo>

    public override async Task RunAsync(DemoContext ctx)
    {
        var catalog = BuildCatalog(ctx.Site.BaseUrl);
        var (jobs, scheduler, alerts) = StartPlatform(catalog, () =>
        {
            var s = ctx.NewSettings();
            s.Set(SettingKeys.LogLevel, "WARNING");
            return s;
        });
        jobs.CrawlerCreated += (_, crawler) => ctx.Observer.Attach(crawler);
        alerts.AlertRaised += a => ctx.Print($"[{a.Severity}] {a.Type} · {a.SpiderId}: {a.Message}");

        foreach (var entry in scheduler.List()) ctx.Print($"schedule {entry.Id,-14} cron '{entry.Cron}' next {entry.NextRun:yyyy-MM-dd HH:mm} UTC");
        // Scheduled runs happen on their own; here every spider is also started right away.
        var started = catalog.List().Select(d => jobs.RunNow(d.Id)).ToList();
        foreach (var job in started)
        {
            var done = await jobs.WaitAsync(job.Id, ctx.CancellationToken);
            ctx.Observer.AddItem(new { Job = done.Id, Spider = done.SpiderId, done.Status, Items = done.ItemCount, Responses = done.ResponseCount, Seconds = Math.Round(done.Duration?.TotalSeconds ?? 0, 2) });
        }
        await Task.Delay(200, ctx.CancellationToken);
        await scheduler.DisposeAsync();
        await jobs.DisposeAsync();
    }
}
