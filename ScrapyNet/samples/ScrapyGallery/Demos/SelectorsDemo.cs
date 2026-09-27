using ScrapyGallery.Infrastructure;
using ScrapyNet;
using ScrapyNet.Cmdline;

namespace ScrapyGallery.Demos;

public sealed class SelectorsDemo : Demo
{
    public override DemoGroup Group => DemoGroup.Extract;
    public override string Title => "Selectors playground";
    public override string TitleId => "Taman bermain selector";
    public override string Summary => "Fetch any page of the practice site and try CSS and XPath queries against it — the same selectors spiders use, including Scrapy's ::text and ::attr() and parsel's has-class().";
    public override string SummaryId => "Ambil halaman mana pun dari situs latihan lalu coba query CSS dan XPath — selector yang sama dengan yang dipakai spider, termasuk ::text, ::attr(), dan has-class().";
    public override IReadOnlyList<string> Concepts => ["Selector", "Css()", "Xpath()", "::text", "::attr()", "Re()"];

    public override IReadOnlyList<DemoInput> Inputs =>
    [
        new("path", "Page on the practice site", "Halaman di situs latihan", "/quotes/"),
        new("css", "CSS query", "Query CSS", "div.quote small.author::text"),
        new("xpath", "XPath query", "Query XPath", "//div[has-class('quote')]//a[@class='tag']/text()"),
    ];

    public override async Task RunAsync(DemoContext ctx)
    {
        // <demo>
        // Fetch through the full engine (headers, cookies, retries) — like `scrapy shell`.
        var response = await Fetcher.FetchAsync(ctx.Site.Url(ctx.Input("path")));

        var css = response.Css(ctx.Input("css"));
        var xpath = response.Xpath(ctx.Input("xpath"));

        for (var i = 0; i < css.Count; i++)
            ctx.Observer.AddItem(new { Query = "css", Index = i, Value = css[i].Get() });
        for (var i = 0; i < xpath.Count; i++)
            ctx.Observer.AddItem(new { Query = "xpath", Index = i, Value = xpath[i].Get() });

        ctx.Print($"{response.Status} {response.Url} — {response.Body.Length:N0} bytes");
        ctx.Print($"css   → {css.Count} results, first: {css.Get() ?? "(none)"}");
        ctx.Print($"xpath → {xpath.Count} results, distinct: {string.Join(", ", xpath.GetAll().Distinct())}");
        ctx.Print($"regex → prices on the page: {string.Join(" ", response.Css("body").Re(@"£\d+\.\d{2}").Take(5))}");
        // </demo>
        ctx.Observer.Log($"Fetched {response.Url}");
    }
}
