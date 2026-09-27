using ScrapyGallery.Infrastructure;
using ScrapyNet;

namespace ScrapyGallery.Demos;

public sealed class ResumeDemo : Demo
{
    public override DemoGroup Group => DemoGroup.Operate;
    public override string Title => "Pause and resume";
    public override string TitleId => "Jeda dan lanjutkan";
    public override string Summary => "With JOBDIR set, stopping a crawl saves its queue and the fingerprints of every page seen. The next run with the same folder picks up the queue and skips what was already fetched.";
    public override string SummaryId => "Dengan JOBDIR, menghentikan crawl menyimpan antrean dan sidik jari setiap halaman yang sudah dilihat. Run berikutnya dengan folder yang sama melanjutkan antrean dan melewati yang sudah diambil.";
    public override IReadOnlyList<string> Concepts => ["JOBDIR", "requests.queue", "requests.seen", "Spider.State"];

    // <demo>
    public static Settings Resumable(Settings settings, string jobDir)
    {
        settings.Set(SettingKeys.JobDir, jobDir);   // queue, seen fingerprints and spider state live here
        return settings;
    }
    // </demo>

    public override async Task RunAsync(DemoContext ctx)
    {
        var jobDir = Path.Combine(Path.GetTempPath(), "scrapy-gallery-jobs", Guid.NewGuid().ToString("N")[..8]);

        var first = Resumable(ctx.NewSettings(), jobDir);
        first.Set(SettingKeys.CloseSpiderItemCount, 60);   // stand-in for Ctrl+C
        var run1 = await ctx.CrawlAsync(new LinkGraphDemo.GraphSpider(ctx.Site.Url("/graph/0")), first);
        var queued = File.Exists(Path.Combine(jobDir, "requests.queue")) ? File.ReadLines(Path.Combine(jobDir, "requests.queue")).Count() : 0;
        ctx.Print($"run 1: {run1.ItemCount} pages, stopped ({run1.FinishReason}); {queued} requests saved to requests.queue");
        ctx.Observer.AddPoint("pages per run", 1, run1.ItemCount);

        await Task.Delay(600, ctx.CancellationToken);
        var run2 = await ctx.CrawlAsync(new LinkGraphDemo.GraphSpider(ctx.Site.Url("/graph/0")), Resumable(ctx.NewSettings(), jobDir));
        ctx.Print($"run 2: {run2.ItemCount} pages, restored {run2.Stats.GetValueOrDefault("scheduler/restored")} requests, {run2.FinishReason}");
        ctx.Observer.AddPoint("pages per run", 2, run2.ItemCount);
        ctx.Print($"total {run1.ItemCount + run2.ItemCount} pages for a 200-page site — nothing crawled twice except the start page");
    }
}
