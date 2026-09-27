using ScrapyGallery.Infrastructure;
using ScrapyNet;
using ScrapyNet.DownloaderMiddlewares;
using ScrapyNet.Sandbox;

namespace ScrapyGallery.Demos;

public sealed class ProxiesDemo : Demo
{
    public override DemoGroup Group => DemoGroup.Operate;
    public override string Title => "Proxy rotation";
    public override string TitleId => "Rotasi proxy";
    public override string Summary => "Three local proxies share the traffic. One starts answering 403 after a few requests, like a banned IP: it is benched for a cooldown and its request is rerouted through a healthy proxy.";
    public override string SummaryId => "Tiga proxy lokal berbagi lalu lintas. Salah satunya mulai membalas 403 setelah beberapa request, seperti IP yang diblokir: proxy itu diistirahatkan dan request-nya dialihkan ke proxy yang sehat.";
    public override IReadOnlyList<string> Concepts => ["ProxyRotationMiddleware", "PROXY_LIST", "PROXY_BAN_STATUS_CODES", "meta[\"proxy\"]"];

    // <demo>
    public sealed class WhoAmISpider(IEnumerable<string> urls) : Spider
    {
        public override async IAsyncEnumerable<Request> StartAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            foreach (var url in urls) yield return new Request(url);
            await Task.CompletedTask;
        }

        public override async IAsyncEnumerable<object> Parse(Response response)
        {
            yield return new { response.Url, Proxy = response.Headers.Get("X-Sandbox-Proxy"), response.Status };
            await Task.CompletedTask;
        }
    }

    public static void ConfigureProxies(Settings settings, IEnumerable<string> proxies)
    {
        settings.Set(SettingKeys.ProxyList, proxies.ToList());
        settings.Set(SettingKeys.ProxyBanCooldown, 600);
        settings.DownloaderMiddlewares.Add<ProxyRotationMiddleware>(740);
    }
    // </demo>

    public override async Task RunAsync(DemoContext ctx)
    {
        await using var alpha = await SandboxSite.StartAsync(new SandboxOptions { Name = "alpha" });
        await using var beta = await SandboxSite.StartAsync(new SandboxOptions { Name = "beta", BanProxyAfter = 3 });
        await using var gamma = await SandboxSite.StartAsync(new SandboxOptions { Name = "gamma", LatencyMs = 40 });
        var settings = ctx.NewSettings();
        settings.Set(SettingKeys.ConcurrentRequests, 2);
        ConfigureProxies(settings, [alpha.ProxyUrl, beta.ProxyUrl, gamma.ProxyUrl]);

        var crawler = ctx.CreateCrawler(new WhoAmISpider(Enumerable.Range(1, 24).Select(i => ctx.Site.Url($"/ip?n={i}"))), settings);
        var result = await crawler.CrawlAsync(cancellationToken: ctx.CancellationToken);
        ctx.Print(result.ToString());
        var names = new Dictionary<string, string> { [alpha.ProxyUrl] = "alpha", [beta.ProxyUrl] = "beta", [gamma.ProxyUrl] = "gamma" };
        foreach (var (k, v) in result.Stats.Where(kv => kv.Key.StartsWith("proxies/", StringComparison.Ordinal)))
        {
            var label = names.Aggregate(k, (acc, n) => acc.Replace(n.Key, n.Value));
            ctx.Print($"{label,-30} {v}");
            if (label.StartsWith("proxies/used/", StringComparison.Ordinal))
                ctx.Observer.AddPoint("requests per proxy", names.Values.ToList().IndexOf(label["proxies/used/".Length..]) + 1, Convert.ToDouble(v));
        }
    }
}
