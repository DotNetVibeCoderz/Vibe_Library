using ScrapyGallery.Infrastructure;
using ScrapyNet;

namespace ScrapyGallery.Demos;

public sealed class SitemapDemo : Demo
{
    public override DemoGroup Group => DemoGroup.Crawl;
    public override string Title => "Sitemaps and robots.txt";
    public override string TitleId => "Sitemap dan robots.txt";
    public override string Summary => "Start from robots.txt, read the sitemap index it points to (one sitemap is gzipped), and route each URL to a callback by pattern. robots.txt is also obeyed: the private folder is never fetched.";
    public override string SummaryId => "Mulai dari robots.txt, baca indeks sitemap (salah satunya terkompresi gzip), dan arahkan tiap URL ke callback berdasarkan pola. robots.txt juga dipatuhi.";
    public override IReadOnlyList<string> Concepts => ["SitemapSpider", "SitemapRule", "ROBOTSTXT_OBEY", "gzip"];

    // <demo>
    public sealed class SiteMapSpider(string robotsUrl) : SitemapSpider
    {
        public override string Name => "sitemap";
        public override IReadOnlyList<string> SitemapUrls => [robotsUrl];

        protected override IReadOnlyList<SitemapRule> SitemapRules =>
        [
            new("/books/catalogue/", ParseBook),
            new("/quotes/author/", ParseAuthor),
        ];

        private IEnumerable<object> ParseBook(Response r)
        {
            yield return new { Kind = "book", Name = r.Css("h1::text").Get(), Price = r.Css("p.price_color::text").Get() };
        }

        private IEnumerable<object> ParseAuthor(Response r)
        {
            yield return new { Kind = "author", Name = r.Css("h3.author-title::text").Get(), Born = r.Css(".author-born-date::text").Get() };
        }
    }
    // </demo>

    public override async Task RunAsync(DemoContext ctx)
    {
        var settings = ctx.NewSettings();
        settings.Set(SettingKeys.RobotsTxtObey, true);
        var result = await ctx.CrawlAsync(new SiteMapSpider(ctx.Site.Url("/robots.txt")), settings);
        ctx.Print(result.ToString());
        ctx.Print($"robots.txt requests: {result.Stats.GetValueOrDefault("robotstxt/request_count")}");
    }
}
