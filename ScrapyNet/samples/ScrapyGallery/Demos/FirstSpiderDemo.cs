using ScrapyGallery.Infrastructure;
using ScrapyNet;

namespace ScrapyGallery.Demos;

public sealed class FirstSpiderDemo : Demo
{
    public override DemoGroup Group => DemoGroup.Crawl;
    public override string Title => "Your first spider";
    public override string TitleId => "Spider pertama Anda";
    public override string Summary => "The Scrapy tutorial spider: start at the quotes page, yield one item per quote, follow the Next link until there is none. Items are anonymous objects — any C# object is an item.";
    public override string SummaryId => "Spider tutorial Scrapy: mulai dari halaman kutipan, hasilkan satu item per kutipan, ikuti tautan Next sampai habis. Objek C# apa pun bisa menjadi item.";
    public override IReadOnlyList<string> Concepts => ["Spider", "Parse()", "response.Follow", "yield return"];

    // <demo>
    public sealed class QuotesSpider(string startUrl) : Spider
    {
        public override string Name => "quotes";
        public override IReadOnlyList<string> StartUrls => [startUrl];

        public override async IAsyncEnumerable<object> Parse(Response response)
        {
            foreach (var quote in response.Css("div.quote"))
                yield return new
                {
                    Author = quote.Css("small.author::text").Get(),
                    Text = quote.Css("span.text::text").Get()?.Trim('“', '”'),
                    Tags = quote.Css("a.tag::text").GetAll(),
                };

            if (response.Css("li.next a::attr(href)").Get() is { } next)
                yield return response.Follow(next, Parse);
            await Task.CompletedTask;
        }
    }
    // </demo>

    public override async Task RunAsync(DemoContext ctx)
    {
        var settings = ctx.NewSettings();
        settings.Set(SettingKeys.DownloadDelay, 0.25);   // slow enough to watch the web grow
        var result = await ctx.CrawlAsync(new QuotesSpider(ctx.Site.Url("/quotes/")), settings);
        ctx.Print(result.ToString());
    }
}
