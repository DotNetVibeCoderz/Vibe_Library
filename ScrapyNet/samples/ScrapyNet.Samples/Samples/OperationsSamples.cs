using ScrapyNet.DownloaderMiddlewares;
using ScrapyNet.Extensions;
using ScrapyNet.Sandbox;

namespace ScrapyNet.Samples;

// ---- middleware ---------------------------------------------------------------------------------------

/// <summary>A custom downloader middleware: tags requests and times responses.</summary>
public sealed class TimingMiddleware(IStatsCollector stats) : DownloaderMiddleware
{
    public override ValueTask<DownloadResult> ProcessRequestAsync(Request request, Spider spider)
    {
        request.Headers.Set("X-Crawled-By", "Scrapy.Net sample");
        request.Meta["started"] = DateTimeOffset.UtcNow;
        return DownloadResult.Continue;
    }

    public override ValueTask<DownloadResult> ProcessResponseAsync(Request request, Response response, Spider spider)
    {
        if (request.Meta.GetValueOrDefault("started") is DateTimeOffset started)
            stats.Max("timing/slowest_ms", (DateTimeOffset.UtcNow - started).TotalMilliseconds);
        return (DownloadResult)response;
    }
}

public sealed class EchoSpider(IEnumerable<string> urls) : Spider
{
    public override string Name => "echo";

    public override async IAsyncEnumerable<Request> StartAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var url in urls) yield return new Request(url).WithErrback(f => Logger.LogErrorMessage($"Failed {f.Request.Url}: {f.Message}"));
        await Task.CompletedTask;
    }

    public override async IAsyncEnumerable<object> Parse(Response response)
    {
        var redirects = response.Request.GetMeta<List<string>>(MetaKeys.RedirectUrls)?.Count ?? 0;
        var retries = response.Request.GetMeta<int>(MetaKeys.RetryTimes);
        var headers = response is JsonResponse json ? json.Json() : null;
        yield return new
        {
            response.Url,
            response.Status,
            Redirects = redirects,
            Retries = retries,
            UserAgent = headers?["User-Agent"]?.GetValue<string>(),
            Tag = headers?["X-Crawled-By"]?.GetValue<string>(),
        };
        await Task.CompletedTask;
    }
}

public static class MiddlewareSample
{
    public static async Task RunAsync(SandboxSite site)
    {
        var settings = SampleRegistry.Defaults("WARNING");
        settings.DownloaderMiddlewares
            .Add<TimingMiddleware>(543)
            .Add<RandomUserAgentMiddleware>(400);
        var key = Guid.NewGuid().ToString("N");
        var (result, items) = await Scrapy.CrawlAndCollectAsync(new EchoSpider(
        [
            site.Url("/headers"),              // shows the random User-Agent and our header
            site.Url("/redirect/3"),           // followed through three redirects
            site.Url($"/flaky/{key}?fail=2"),  // 503 twice, then 200: retried
        ]), settings);
        foreach (var item in items) Console.WriteLine(ItemAdapter.For(item));
        Console.WriteLine($"retries: {result.Stats.GetValueOrDefault("retry/count")}, redirects: {result.Stats.GetValueOrDefault("downloader/redirect_count")}, slowest: {result.Stats.GetValueOrDefault("timing/slowest_ms"):0} ms");
    }
}

// ---- proxies ------------------------------------------------------------------------------------------

public static class ProxySample
{
    public static async Task RunAsync(SandboxSite site)
    {
        // Two local "proxies"; the second one starts answering 403 after 3 requests, like a banned IP.
        await using var proxyA = await SandboxSite.StartAsync(new SandboxOptions { Name = "proxy-a" });
        await using var proxyB = await SandboxSite.StartAsync(new SandboxOptions { Name = "proxy-b", BanProxyAfter = 3 });

        var settings = SampleRegistry.Defaults("INFO");
        settings.Set(SettingKeys.ProxyList, new List<string> { proxyA.ProxyUrl, proxyB.ProxyUrl });
        settings.DownloaderMiddlewares.Add<ProxyRotationMiddleware>(740);
        settings.Set(SettingKeys.ConcurrentRequests, 1);

        var (result, items) = await Scrapy.CrawlAndCollectAsync(
            new EchoSpider(Enumerable.Range(1, 12).Select(i => site.Url($"/ip?request={i}"))), settings);
        Console.WriteLine($"{items.Count} responses; banned proxies: {result.Stats.GetValueOrDefault("proxies/banned")}, rerouted: {result.Stats.GetValueOrDefault("proxies/rerouted")}");
        foreach (var (k, v) in result.Stats.Where(kv => kv.Key.StartsWith("proxies/used/"))) Console.WriteLine($"  {k} = {v}");
    }
}

// ---- throttle -----------------------------------------------------------------------------------------

public static class ThrottleSample
{
    public static async Task RunAsync(SandboxSite _)
    {
        await using var slowSite = await SandboxSite.StartAsync(new SandboxOptions { LatencyMs = 150 });
        var settings = SampleRegistry.Defaults("WARNING");
        settings.Set(SettingKeys.AutoThrottleEnabled, true);
        settings.Set(SettingKeys.AutoThrottleStartDelay, 0.05);
        settings.Set(SettingKeys.AutoThrottleTargetConcurrency, 2.0);
        settings.Set(SettingKeys.AutoThrottleDebug, true);
        settings.Set(SettingKeys.LogLevel, "INFO");

        var crawler = new Crawler(new EchoSpider(Enumerable.Range(1, 10).Select(i => slowSite.Url($"/quotes/page/{i % 5 + 1}/?v={i}"))), settings);
        await crawler.CrawlAsync();
        var history = crawler.Extensions.OfType<AutoThrottle>().Single().History;
        Console.WriteLine($"delay went from {history[0].Delay * 1000:0} ms to {history[^1].Delay * 1000:0} ms (latency ≈ {history.Average(h => h.Latency) * 1000:0} ms)");
    }
}

// ---- resume -------------------------------------------------------------------------------------------

public sealed class GraphSpider(string startUrl) : Spider
{
    public override string Name => "graph";
    public override IReadOnlyList<string> StartUrls => [startUrl];

    public override async IAsyncEnumerable<object> Parse(Response response)
    {
        yield return new { Node = response.Css("h1.node::text").Get() };
        foreach (var r in response.FollowAllCss("ul.links a", Parse)) yield return r;
        await Task.CompletedTask;
    }
}

public static class ResumeSample
{
    public static async Task RunAsync(SandboxSite site)
    {
        var jobDir = Path.Combine(SampleRegistry.OutputDir("resume"), "job-" + Guid.NewGuid().ToString("N")[..6]);
        Settings Make(int? stopAfter)
        {
            var s = SampleRegistry.Defaults("WARNING");
            s.Set(SettingKeys.JobDir, jobDir);
            if (stopAfter is { } n) s.Set(SettingKeys.CloseSpiderItemCount, n);
            return s;
        }

        // First run stops early (as if interrupted with Ctrl+C); its queue is saved to JOBDIR.
        var first = await Scrapy.CrawlAsync(new GraphSpider(site.Url("/graph/0")), Make(50));
        Console.WriteLine($"run 1: {first.ItemCount} pages, reason={first.FinishReason}, saved queue: {File.Exists(Path.Combine(jobDir, "requests.queue"))}");
        // Second run picks up the saved queue and skips everything already seen.
        var second = await Scrapy.CrawlAsync(new GraphSpider(site.Url("/graph/0")), Make(null));
        Console.WriteLine($"run 2: {second.ItemCount} pages, restored {second.Stats.GetValueOrDefault("scheduler/restored")} requests, reason={second.FinishReason}");
    }
}

// ---- cache --------------------------------------------------------------------------------------------

public static class CacheSample
{
    public static async Task RunAsync(SandboxSite site)
    {
        var dir = Path.Combine(SampleRegistry.OutputDir("cache"), "httpcache-" + Guid.NewGuid().ToString("N")[..6]);
        Settings Make()
        {
            var s = SampleRegistry.Defaults("WARNING");
            s.Set(SettingKeys.HttpCacheEnabled, true);
            s.Set(SettingKeys.HttpCacheDir, dir);
            return s;
        }
        var before = site.RequestCount;
        var first = await Scrapy.CrawlAsync(new QuotesSpider(site.Url("/quotes/")), Make());
        var middle = site.RequestCount;
        var second = await Scrapy.CrawlAsync(new QuotesSpider(site.Url("/quotes/")), Make());
        Console.WriteLine($"run 1: {first.ItemCount} items, {middle - before} requests hit the server");
        Console.WriteLine($"run 2: {second.ItemCount} items, {site.RequestCount - middle} requests hit the server, cache hits: {second.Stats.GetValueOrDefault("httpcache/hit")}");
    }
}

// ---- signals and stats ------------------------------------------------------------------------------------

public static class SignalsSample
{
    public static async Task RunAsync(SandboxSite site)
    {
        var crawler = new Crawler(new BooksSpider(site.Url("/books/")), SampleRegistry.Defaults("WARNING"));
        var items = 0;
        crawler.Signals.Connect(Signals.SpiderOpened, e => Console.WriteLine($"opened: {e.Spider.Name}"));
        crawler.Signals.Connect(Signals.ItemScraped, e =>
        {
            if (Interlocked.Increment(ref items) % 20 == 0) Console.WriteLine($"  … {items} items so far (last: {((Book)e.Item).Title})");
        });
        crawler.Signals.Connect(Signals.SpiderClosed, e => Console.WriteLine($"closed: {e.Reason}"));
        var result = await crawler.CrawlAsync();
        Console.WriteLine(MemoryStatsCollector.Format(result.Stats));
    }
}
