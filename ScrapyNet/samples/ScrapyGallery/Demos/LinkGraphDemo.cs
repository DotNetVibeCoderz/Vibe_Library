using ScrapyGallery.Infrastructure;
using ScrapyNet;

namespace ScrapyGallery.Demos;

public sealed class LinkGraphDemo : Demo
{
    public override DemoGroup Group => DemoGroup.Operate;
    public override string Title => "Depth, duplicates and signals";
    public override string TitleId => "Kedalaman, duplikat, dan sinyal";
    public override string Summary => "A 200-page site full of cycles. The duplicate filter fingerprints every request so each page is fetched once; DEPTH_LIMIT caps how far the crawl wanders; signals report progress as it happens.";
    public override string SummaryId => "Situs 200 halaman penuh siklus. Filter duplikat mengambil sidik jari setiap request sehingga tiap halaman diambil sekali; DEPTH_LIMIT membatasi jarak crawl; sinyal melaporkan kemajuan secara langsung.";
    public override IReadOnlyList<string> Concepts => ["RFPDupeFilter", "DEPTH_LIMIT", "SignalManager", "Signals.ItemScraped"];

    public override IReadOnlyList<DemoInput> Inputs => [new("depth", "DEPTH_LIMIT (0 = unlimited)", "DEPTH_LIMIT (0 = tanpa batas)", "4")];

    // <demo>
    public sealed class GraphSpider(string start) : Spider
    {
        public override string Name => "graph";
        public override IReadOnlyList<string> StartUrls => [start];

        public override async IAsyncEnumerable<object> Parse(Response response)
        {
            yield return new { Node = response.Css("h1.node::text").Get(), Depth = response.Request.Depth, Links = response.Css("ul.links a").Count };
            foreach (var next in response.FollowAllCss("ul.links a", Parse))
                yield return next;
            await Task.CompletedTask;
        }
    }

    public static void ReportProgress(Crawler crawler, Action<string> print)
    {
        var items = 0;
        crawler.Signals.Connect(Signals.SpiderOpened, e => print($"opened {e.Spider.Name}"));
        crawler.Signals.Connect(Signals.ItemScraped, _ =>
        {
            if (Interlocked.Increment(ref items) % 25 == 0) print($"… {items} pages");
        });
        crawler.Signals.Connect(Signals.RequestDropped, _ => { /* a duplicate was filtered */ });
        crawler.Signals.Connect(Signals.SpiderClosed, e => print($"closed: {e.Reason}"));
    }
    // </demo>

    public override async Task RunAsync(DemoContext ctx)
    {
        var settings = ctx.NewSettings();
        settings.Set(SettingKeys.DepthLimit, int.TryParse(ctx.Input("depth"), out var d) ? d : 4);
        settings.Set(SettingKeys.DepthStatsVerbose, true);
        settings.Set(SettingKeys.DownloadDelay, 0.01);
        var crawler = ctx.CreateCrawler(new GraphSpider(ctx.Site.Url("/graph/0")), settings);
        ReportProgress(crawler, ctx.Print);
        var result = await crawler.CrawlAsync(cancellationToken: ctx.CancellationToken);
        ctx.Print(result.ToString());
        ctx.Print($"duplicates filtered: {result.Stats.GetValueOrDefault("dupefilter/filtered")} · deepest: {result.Stats.GetValueOrDefault("request_depth_max")}");
        foreach (var (k, v) in result.Stats.Where(kv => kv.Key.StartsWith("request_depth_count/", StringComparison.Ordinal)).OrderBy(kv => kv.Key))
            ctx.Observer.AddPoint("pages per depth", int.Parse(k[(k.LastIndexOf('/') + 1)..]), Convert.ToDouble(v));
    }
}
