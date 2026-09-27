using ScrapyGallery.Infrastructure;
using ScrapyNet;

namespace ScrapyGallery.Demos;

public sealed class CacheDemo : Demo
{
    public override DemoGroup Group => DemoGroup.Operate;
    public override string Title => "HTTP cache";
    public override string TitleId => "Cache HTTP";
    public override string Summary => "Turn on HTTPCACHE_ENABLED while you work on selectors: the first run stores every response on disk, later runs replay them without touching the site. Replayed pages are ringed in amber on the web.";
    public override string SummaryId => "Aktifkan HTTPCACHE_ENABLED saat mengerjakan selector: run pertama menyimpan setiap respons ke disk, run berikutnya memutar ulang tanpa menyentuh situs. Halaman dari cache ditandai cincin kuning.";
    public override IReadOnlyList<string> Concepts => ["HttpCacheMiddleware", "HTTPCACHE_ENABLED", "HTTPCACHE_DIR", "flags: cached"];

    // <demo>
    public static Settings WithCache(Settings settings, string folder)
    {
        settings.Set(SettingKeys.HttpCacheEnabled, true);
        settings.Set(SettingKeys.HttpCacheDir, folder);
        settings.Set(SettingKeys.HttpCacheExpirationSecs, 3600);   // re-fetch after an hour
        return settings;
    }
    // </demo>

    public override async Task RunAsync(DemoContext ctx)
    {
        var folder = Path.Combine(Path.GetTempPath(), "scrapy-gallery-cache", Guid.NewGuid().ToString("N")[..8]);
        var before = ctx.Site.RequestCount;
        var run1 = await ctx.CrawlAsync(new CrawlRulesDemo.BooksSpider(ctx.Site.Url("/books/")), WithCache(ctx.NewSettings(), folder));
        var middle = ctx.Site.RequestCount;
        ctx.Print($"run 1: {run1.ItemCount} items, {middle - before} requests reached the site, {run1.Elapsed.TotalMilliseconds:N0} ms");
        var run2 = await ctx.CrawlAsync(new CrawlRulesDemo.BooksSpider(ctx.Site.Url("/books/")), WithCache(ctx.NewSettings(), folder));
        ctx.Print($"run 2: {run2.ItemCount} items, {ctx.Site.RequestCount - middle} requests reached the site, {run2.Stats.GetValueOrDefault("httpcache/hit")} cache hits, {run2.Elapsed.TotalMilliseconds:N0} ms");
        ctx.Observer.AddPoint("requests to the site", 1, middle - before);
        ctx.Observer.AddPoint("requests to the site", 2, ctx.Site.RequestCount - middle);
    }
}
