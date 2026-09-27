using ScrapyGallery.Infrastructure;
using ScrapyNet;
using ScrapyNet.Extensions;
using ScrapyNet.Sandbox;

namespace ScrapyGallery.Demos;

public sealed class ThrottleDemo : Demo
{
    public override DemoGroup Group => DemoGroup.Operate;
    public override string Title => "AutoThrottle";
    public override string TitleId => "AutoThrottle";
    public override string Summary => "Politeness that adapts: AutoThrottle measures each response's latency and moves the download delay toward latency ÷ target concurrency. Point it at a slow server and watch the delay climb in the Charts tab.";
    public override string SummaryId => "Kesopanan yang adaptif: AutoThrottle mengukur latensi tiap respons dan menggeser jeda unduh menuju latensi ÷ target konkurensi. Arahkan ke server lambat dan lihat jedanya naik di tab Charts.";
    public override IReadOnlyList<string> Concepts => ["AutoThrottle", "AUTOTHROTTLE_TARGET_CONCURRENCY", "download_latency", "DownloadSlot.Delay"];

    public override IReadOnlyList<DemoInput> Inputs => [new("latency", "Server latency (ms)", "Latensi server (ms)", "220")];

    // <demo>
    public static void EnableAutoThrottle(Settings settings)
    {
        settings.Set(SettingKeys.AutoThrottleEnabled, true);
        settings.Set(SettingKeys.AutoThrottleStartDelay, 0.02);
        settings.Set(SettingKeys.AutoThrottleMaxDelay, 5);
        settings.Set(SettingKeys.AutoThrottleTargetConcurrency, 1.5);
        settings.Set(SettingKeys.AutoThrottleDebug, true);
    }
    // </demo>

    public override async Task RunAsync(DemoContext ctx)
    {
        var latency = int.TryParse(ctx.Input("latency"), out var ms) ? ms : 220;
        await using var slow = await SandboxSite.StartAsync(new SandboxOptions { LatencyMs = latency });
        var settings = ctx.NewSettings();
        EnableAutoThrottle(settings);
        var crawler = ctx.CreateCrawler(new LinkGraphDemo.GraphSpider(slow.Url("/graph/0")), settings);
        crawler.Settings.Set(SettingKeys.CloseSpiderPageCount, 30);
        var started = DateTimeOffset.UtcNow;
        crawler.Signals.Connect(Signals.ResponseDownloaded, e =>
        {
            var t = (DateTimeOffset.UtcNow - started).TotalSeconds;
            if (e.Request.Meta.GetValueOrDefault(MetaKeys.DownloadLatency) is double lat) ctx.Observer.AddPoint("latency ms", t, lat * 1000);
            var slot = crawler.Engine.Downloader.Slots.Values.FirstOrDefault();
            if (slot is not null) ctx.Observer.AddPoint("delay ms", t, slot.Delay * 1000);
        });
        var result = await crawler.CrawlAsync(cancellationToken: ctx.CancellationToken);
        var history = crawler.Extensions.OfType<AutoThrottle>().Single().History;
        ctx.Print(result.ToString());
        if (history.Count > 0)
            ctx.Print($"delay: {history[0].Delay * 1000:0} ms → {history[^1].Delay * 1000:0} ms · mean latency {history.Average(h => h.Latency) * 1000:0} ms");
    }
}
