using ScrapyNet.Engine;
using ScrapyNet.Redis;
using StackExchange.Redis;
using Xunit;

namespace ScrapyNet.Tests;

public sealed class RedisGraphSpider : RedisSpider
{
    public override string Name => "redis-graph";

    public override async IAsyncEnumerable<object> Parse(Response response)
    {
        yield return new Dictionary<string, object?> { ["node"] = response.Css("h1.node::text").Get() };
        foreach (var r in response.FollowAllCss("ul.links a", Parse)) yield return r;
        await Task.CompletedTask;
    }
}

/// <summary>
/// Runs against a real Redis when SCRAPYNET_REDIS is set (e.g. <c>localhost:6379</c>; CI starts one).
/// Every test uses its own key prefix and deletes its keys.
/// </summary>
public class RedisTests(SandboxFixture sandbox)
{
    private static string? Url => Environment.GetEnvironmentVariable("SCRAPYNET_REDIS");

    private static Settings RedisSettings(string prefix, bool persist = true)
    {
        var s = new Settings();
        TestSettings.Quiet(s);
        s.UseRedis(Url!, persist: persist);
        s.Set(RedisSettingKeys.QueueKey, prefix + ":{spider}:requests");
        s.Set(RedisSettingKeys.DupeFilterKey, prefix + ":{spider}:dupefilter");
        s.Set(RedisSettingKeys.StartUrlsKey, prefix + ":{spider}:start_urls");
        s.Set(RedisSettingKeys.ItemsKey, prefix + ":{spider}:items");
        s.Set(RedisSettingKeys.MaxIdleTimeBeforeClose, 2);
        s.Set(SettingKeys.ConcurrentRequests, 4);
        return s;
    }

    private static async Task CleanupAsync(string prefix)
    {
        var server = RedisConnections.Get(Url!).GetServers()[0];
        var db = RedisConnections.Get(Url!).GetDatabase();
        await foreach (var key in server.KeysAsync(pattern: prefix + ":*")) await db.KeyDeleteAsync(key);
    }

    [Fact]
    public void Redis_urls_become_configuration_strings()
    {
        Assert.Equal("cache.example.com:6380,user=bob,password=p@ss,defaultDatabase=2,ssl=true,abortConnect=false",
            RedisConnections.ToConfiguration("rediss://bob:p%40ss@cache.example.com:6380/2"));
        Assert.Equal("localhost:6379", RedisConnections.ToConfiguration("localhost:6379"));
    }

    [Fact]
    public async Task Two_workers_share_one_queue_and_never_crawl_a_page_twice()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(Url), "Set SCRAPYNET_REDIS to a Redis server (host:port).");
        var prefix = "scrapynet-test-" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            var db = RedisConnections.Get(Url!).GetDatabase();
            await db.ListRightPushAsync(prefix + ":redis-graph:start_urls", sandbox.Url("/graph/0"));

            var workers = Enumerable.Range(0, 2).Select(_ =>
            {
                // A small delay keeps the crawl longer than the 1 s idle poll, so both workers overlap.
                var settings = RedisSettings(prefix).PushItemsToRedis();
                settings.Set(SettingKeys.DownloadDelay, 0.01);
                return Scrapy.CrawlAndCollectAsync(new RedisGraphSpider(), settings, TestContext.Current.CancellationToken);
            }).ToList();
            var results = await Task.WhenAll(workers);

            var nodes = results.SelectMany(r => r.Items).Select(i => (string)((Dictionary<string, object?>)i)["node"]!).ToList();
            Assert.Equal(200, nodes.Count);
            Assert.Equal(200, nodes.Distinct().Count());
            Assert.All(results, r => Assert.True(r.Items.Count > 0, "both workers took part"));
            Assert.All(results, r => Assert.Equal("finished", r.Result.FinishReason));
            Assert.Equal(200, await db.ListLengthAsync(prefix + ":redis-graph:items"));
            Assert.Equal(200, await db.SetLengthAsync(prefix + ":redis-graph:dupefilter"));
        }
        finally
        {
            await CleanupAsync(prefix);
        }
    }

    [Fact]
    public async Task Scheduler_orders_by_priority_keeps_lambdas_local_and_clears_unless_persisted()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(Url), "Set SCRAPYNET_REDIS to a Redis server (host:port).");
        var prefix = "scrapynet-test-" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            var spider = new RedisGraphSpider();
            var crawler = new Crawler(spider, RedisSettings(prefix, persist: false));
            var scheduler = new RedisScheduler(crawler);
            await scheduler.OpenAsync(spider);

            scheduler.Enqueue(new Request("https://example.com/low", spider.Parse) { Priority = 1 });
            scheduler.Enqueue(new Request("https://example.com/high", spider.Parse) { Priority = 9 });
            scheduler.Enqueue(new Request("https://example.com/same", spider.Parse) { DontFilter = true });
            scheduler.Enqueue(new Request("https://example.com/same", spider.Parse) { DontFilter = true });
            scheduler.Enqueue(new Request("https://example.com/lambda", _ => AsyncEnumerable.Empty<object>()));
            Assert.False(scheduler.Enqueue(new Request("https://example.com/low", spider.Parse)));

            Assert.Equal(5, scheduler.Count);
            Assert.Equal("https://example.com/lambda", scheduler.Dequeue()!.Url);   // local first
            Assert.Equal("https://example.com/high", scheduler.Dequeue()!.Url);
            Assert.Equal("Parse", scheduler.Dequeue()!.CallbackName);
            await scheduler.CloseAsync("finished");

            var db = RedisConnections.Get(Url!).GetDatabase();
            Assert.False(await db.KeyExistsAsync(scheduler.QueueKey));
        }
        finally
        {
            await CleanupAsync(prefix);
        }
    }
}
