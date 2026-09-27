using ScrapyGallery.Infrastructure;
using ScrapyNet;
using ScrapyNet.Playwright;

namespace ScrapyGallery.Demos;

public sealed class BrowserDemo : Demo
{
    public override DemoGroup Group => DemoGroup.Crawl;
    public override string Title => "JavaScript pages in a browser";
    public override string TitleId => "Halaman JavaScript di browser";
    public override string Summary => "The same URL fetched twice: over plain HTTP the quotes are missing because JavaScript builds them; rendered in headless Chromium they are all there. Only requests marked for the browser use it.";
    public override string SummaryId => "URL yang sama diambil dua kali: lewat HTTP biasa kutipannya tidak ada karena dibuat JavaScript; dirender di Chromium headless semuanya muncul. Hanya request bertanda yang memakai browser.";
    public override IReadOnlyList<string> Concepts => ["Playwright", "WithPlaywright()", "PageMethod", "UsePlaywright()"];

    // <demo>
    public sealed class JsQuotesSpider(string url) : Spider
    {
        public override async IAsyncEnumerable<Request> StartAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            yield return new Request(url) { DontFilter = true, Meta = { ["via"] = "http" } };
            yield return new Request(url) { DontFilter = true, Meta = { ["via"] = "browser" } }
                .WithPlaywright(PageMethod.WaitForSelector("div.quote"), PageMethod.Screenshot(fullPage: false));
            await Task.CompletedTask;
        }

        public override async IAsyncEnumerable<object> Parse(Response response)
        {
            var quotes = response.Css("div.quote");
            yield return new { Via = response.Meta["via"], Quotes = quotes.Count, First = quotes.Css("span.text::text").Get() ?? "(nothing — needs JavaScript)" };
            if (response.Meta.GetValueOrDefault(PlaywrightMeta.Screenshot) is byte[] png)
                Screenshot = png;
            await Task.CompletedTask;
        }

        public byte[]? Screenshot { get; private set; }
    }
    // </demo>

    public override async Task RunAsync(DemoContext ctx)
    {
        var spider = new JsQuotesSpider(ctx.Site.Url("/quotes/js/"));
        var result = await ctx.CrawlAsync(spider, ctx.NewSettings().UsePlaywright(blockResourceTypes: ["font"]));
        ctx.Print(result.ToString());
        if (spider.Screenshot is { } png)
        {
            var path = Path.Combine(Path.GetTempPath(), $"scrapy-gallery-js-{Guid.NewGuid():N}.png");
            await File.WriteAllBytesAsync(path, png);
            ctx.Observer.AddImage(path);
            ctx.Print("Screenshot of the rendered page:");
        }
        else
        {
            ctx.Print("No screenshot: install Playwright browsers (pwsh bin/…/playwright.ps1 install chromium).");
        }
    }
}
