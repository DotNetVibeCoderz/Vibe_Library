using ScrapyGallery.Infrastructure;
using ScrapyNet;

namespace ScrapyGallery.Demos;

public sealed class RetriesDemo : Demo
{
    public override DemoGroup Group => DemoGroup.Operate;
    public override string Title => "Retries, redirects and errors";
    public override string TitleId => "Retry, redirect, dan error";
    public override string Summary => "Real-world failure modes against endpoints built to misbehave: 503s that recover, a server that drops the connection, a 404, a redirect chain and a meta refresh. Each outcome reaches the callback or the errback.";
    public override string SummaryId => "Mode kegagalan nyata pada endpoint yang sengaja bermasalah: 503 yang pulih, server yang memutus koneksi, 404, rantai redirect, dan meta refresh. Setiap hasil sampai ke callback atau errback.";
    public override IReadOnlyList<string> Concepts => ["RetryMiddleware", "RedirectMiddleware", "Errback", "Failure", "RETRY_TIMES"];

    // <demo>
    public sealed class TroubleSpider(IReadOnlyList<string> urls) : Spider
    {
        public override async IAsyncEnumerable<Request> StartAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            foreach (var url in urls)
                yield return new Request(url).WithErrback(OnError);
            await Task.CompletedTask;
        }

        public override async IAsyncEnumerable<object> Parse(Response response)
        {
            yield return new
            {
                Outcome = "ok",
                Url = response.Request.GetMeta<List<string>>(MetaKeys.RedirectUrls)?.First() ?? response.Url,
                FinalUrl = response.Url,
                response.Status,
                Retries = response.Request.GetMeta<int>(MetaKeys.RetryTimes),
                Redirects = response.Request.GetMeta<int>(MetaKeys.RedirectTimes),
            };
            await Task.CompletedTask;
        }

        // Errbacks receive the failure and can still yield items or new requests.
        private IEnumerable<object> OnError(Failure failure)
        {
            yield return new
            {
                Outcome = failure.Is<HttpErrorException>() ? $"HTTP {failure.Response!.Status}" : failure.Exception.GetType().Name,
                failure.Request.Url,
                FinalUrl = "",
                Status = failure.Response?.Status ?? 0,
                Retries = failure.Request.GetMeta<int>(MetaKeys.RetryTimes),
                Redirects = 0,
            };
        }
    }
    // </demo>

    public override async Task RunAsync(DemoContext ctx)
    {
        var key = Guid.NewGuid().ToString("N")[..8];
        var site = ctx.Site;
        string[] urls =
        [
            site.Url($"/flaky/{key}-a?fail=1"), site.Url($"/flaky/{key}-b?fail=2"), site.Url($"/flaky/{key}-c?fail=5"),
            site.Url($"/flaky/{key}-d?fail=1&code=429"), site.Url("/drop"), site.Url("/status/404"),
            site.Url("/redirect/4"), site.Url("/meta-refresh"),
        ];
        var settings = ctx.NewSettings();
        settings.Set(SettingKeys.RetryTimes, 2);
        var result = await ctx.CrawlAsync(new TroubleSpider(urls), settings);
        ctx.Print(result.ToString());
        foreach (var (k, v) in result.Stats.Where(kv => kv.Key.StartsWith("retry/", StringComparison.Ordinal) || kv.Key.Contains("redirect")))
            ctx.Print($"{k,-44} {v}");
    }
}
