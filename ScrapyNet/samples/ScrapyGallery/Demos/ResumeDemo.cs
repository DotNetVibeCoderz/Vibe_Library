using ScrapyGallery.Infrastructure;
using ScrapyNet;

namespace ScrapyGallery.Demos;

public sealed class ResumeDemo : Demo
{
    public override DemoGroup Group => DemoGroup.Operate;
    public override string Title => "Pause and resume";
    public override string TitleId => "Jeda dan lanjutkan";
    public override string Summary => "With JOBDIR set, the queue lives on disk from the moment a request is scheduled, next to the fingerprints of every page seen, so even a crash keeps the crawl. The next run with the same folder picks up the queue and skips what was already fetched.";
    public override string SummaryId => "Dengan JOBDIR, antrean tersimpan di disk sejak request dijadwalkan, bersama sidik jari setiap halaman yang sudah dilihat, sehingga crash pun tidak menghilangkan crawl. Run berikutnya dengan folder yang sama melanjutkan antrean dan melewati yang sudah diambil.";
    public override IReadOnlyList<string> Concepts => ["JOBDIR", "requests.queue.log", "requests.seen", "Spider.State"];

    // <demo>
    public static Settings Resumable(Settings settings, string jobDir)
    {
        settings.Set(SettingKeys.JobDir, jobDir);   // disk queue, seen fingerprints and spider state live here
        return settings;
    }
    // </demo>

    public override async Task RunAsync(DemoContext ctx)
    {
        var jobDir = Path.Combine(Path.GetTempPath(), "scrapy-gallery-jobs", Guid.NewGuid().ToString("N")[..8]);

        var first = Resumable(ctx.NewSettings(), jobDir);
        first.Set(SettingKeys.CloseSpiderItemCount, 60);   // stand-in for Ctrl+C
        var run1 = await ctx.CrawlAsync(new LinkGraphDemo.GraphSpider(ctx.Site.Url("/graph/0")), first);
        var journal = Path.Combine(jobDir, "requests.queue.log");
        var lines = File.Exists(journal) ? File.ReadAllLines(journal) : [];
        var queued = lines.Count(l => l.StartsWith('+')) - lines.Count(l => l.StartsWith('-'));
        ctx.Print($"run 1: {run1.ItemCount} pages, stopped ({run1.FinishReason}); {queued} requests still queued in requests.queue.log");
        ctx.Print($"       {run1.Stats.GetValueOrDefault("scheduler/enqueued/disk")} requests went through the disk queue");
        ctx.Observer.AddPoint("pages per run", 1, run1.ItemCount);

        await Task.Delay(600, ctx.CancellationToken);
        var run2 = await ctx.CrawlAsync(new LinkGraphDemo.GraphSpider(ctx.Site.Url("/graph/0")), Resumable(ctx.NewSettings(), jobDir));
        ctx.Print($"run 2: {run2.ItemCount} pages, restored {run2.Stats.GetValueOrDefault("scheduler/restored")} requests, {run2.FinishReason}");
        ctx.Observer.AddPoint("pages per run", 2, run2.ItemCount);
        ctx.Print($"total {run1.ItemCount + run2.ItemCount} pages for a 200-page site — nothing crawled twice except the start page");
    }
}
