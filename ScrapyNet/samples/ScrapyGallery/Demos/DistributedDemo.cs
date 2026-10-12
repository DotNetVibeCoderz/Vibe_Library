using ScrapyGallery.Infrastructure;
using ScrapyNet;
using ScrapyNet.Redis;
using StackExchange.Redis;

namespace ScrapyGallery.Demos;

public sealed class DistributedDemo : Demo
{
    public override DemoGroup Group => DemoGroup.Operate;
    public override string Title => "Distributed crawling (Redis)";
    public override string TitleId => "Crawl terdistribusi (Redis)";
    public override string Summary => "Two workers share one Redis queue and one duplicate filter, as with scrapy-redis. They are fed start URLs through a Redis list, split the 200-page site between them, and never fetch a page twice. Needs a Redis server (docker run -p 6379:6379 redis).";
    public override string SummaryId => "Dua worker berbagi satu antrean Redis dan satu filter duplikat, seperti scrapy-redis. Mereka diberi URL awal lewat list Redis, membagi situs 200 halaman, dan tidak pernah mengambil halaman yang sama dua kali. Butuh server Redis (docker run -p 6379:6379 redis).";
    public override IReadOnlyList<string> Concepts => ["UseRedis", "RedisSpider", "RedisScheduler", "RedisDupeFilter", "SCHEDULER_PERSIST"];
    public override IReadOnlyList<DemoInput> Inputs => [new("redis", "Redis server", "Server Redis", "localhost:6379")];

    // <demo>
    public sealed class GraphWorker : RedisSpider          // start URLs come from "graph-worker:start_urls"
    {
        public override string Name => "graph-worker";

        public override async IAsyncEnumerable<object> Parse(Response response)
        {
            yield return new { Node = response.Css("h1.node::text").Get(), Worker = Crawler.Settings.GetString("WORKER") };
            foreach (var next in response.FollowAllCss("ul.links a", Parse)) yield return next;
            await Task.CompletedTask;
        }
    }

    public static Settings Worker(Settings settings, string redis, int number)
    {
        settings.UseRedis(redis, persist: true);           // shared queue + shared dupe filter
        settings.Set(RedisSettingKeys.MaxIdleTimeBeforeClose, 2);
        settings.Set("WORKER", $"worker {number}");
        return settings;
    }
    // </demo>

    public override async Task RunAsync(DemoContext ctx)
    {
        var redis = string.IsNullOrWhiteSpace(ctx.Input("redis")) ? "localhost:6379" : ctx.Input("redis").Trim();
        IDatabase db;
        try
        {
            db = RedisConnections.Get(redis).GetDatabase();
            await db.PingAsync();
        }
        catch (RedisConnectionException)
        {
            RedisConnections.CloseAll();
            ctx.Print($"No Redis server answered at {redis}.\nStart one with:  docker run -p 6379:6379 redis\nthen run this demo again.");
            return;
        }

        // A fresh crawl: clear what an earlier run left, then seed the start URL.
        await db.KeyDeleteAsync(["graph-worker:requests", "graph-worker:dupefilter", "graph-worker:start_urls"]);
        await db.ListRightPushAsync("graph-worker:start_urls", ctx.Site.Url("/graph/0"));
        var workers = Enumerable.Range(1, 2).Select(n =>
        {
            var settings = Worker(ctx.NewSettings(), redis, n);
            settings.Set(SettingKeys.DownloadDelay, 0.02);   // slow enough to watch the split
            return ctx.CrawlAsync(new GraphWorker(), settings);
        }).ToList();
        var results = await Task.WhenAll(workers);
        for (var i = 0; i < results.Length; i++)
            ctx.Print($"worker {i + 1}: {results[i].ItemCount,4} pages  {new string('#', (int)(results[i].ItemCount / 4))}");
        ctx.Print($"together: {results.Sum(r => r.ItemCount)} pages; fingerprints in Redis: {await db.SetLengthAsync("graph-worker:dupefilter")}");
        await db.KeyDeleteAsync(["graph-worker:requests", "graph-worker:dupefilter"]);
    }
}
