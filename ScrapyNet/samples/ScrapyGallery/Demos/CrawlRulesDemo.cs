using ScrapyGallery.Infrastructure;
using ScrapyNet;

namespace ScrapyGallery.Demos;

public sealed class CrawlRulesDemo : Demo
{
    public override DemoGroup Group => DemoGroup.Crawl;
    public override string Title => "CrawlSpider and link rules";
    public override string TitleId => "CrawlSpider dan aturan tautan";
    public override string Summary => "Describe which links to follow instead of coding it: one rule walks the category pages, another sends every product page to a callback. The link extractor filters by region, pattern and domain.";
    public override string SummaryId => "Jelaskan tautan mana yang diikuti alih-alih menulis kodenya: satu aturan menelusuri halaman kategori, aturan lain mengirim setiap halaman produk ke callback.";
    public override IReadOnlyList<string> Concepts => ["CrawlSpider", "Rule", "LinkExtractor", "RestrictCss", "Deny"];

    // <demo>
    public sealed class BooksSpider(string startUrl) : CrawlSpider
    {
        public override string Name => "books";
        public override IReadOnlyList<string> StartUrls => [startUrl];
        public override IReadOnlyList<string> AllowedDomains => ["127.0.0.1"];

        protected override IReadOnlyList<Rule> Rules =>
        [
            // Category listings: follow, don't parse.
            new(new LinkExtractor { RestrictCss = ["aside.sidebar"], Allow = ["/category/"] }),
            // Product pages: parse with ParseBook.
            new(new LinkExtractor { RestrictCss = ["article.product_pod h3"] }, ParseBook),
        ];

        private IEnumerable<object> ParseBook(Response r)
        {
            yield return new
            {
                Title = r.Css("h1::text").Get(),
                Category = r.Css("ul.breadcrumb li:nth-child(2) a::text").Get(),
                Price = r.Css("p.price_color::text").Get(),
                Upc = r.Xpath("//th[text()='UPC']/following-sibling::td/text()").Get(),
            };
        }
    }
    // </demo>

    public override async Task RunAsync(DemoContext ctx)
    {
        var settings = ctx.NewSettings();
        settings.Set(SettingKeys.ConcurrentRequestsPerDomain, 4);
        settings.Set(SettingKeys.DownloadDelay, 0.02);
        var result = await ctx.CrawlAsync(new BooksSpider(ctx.Site.Url("/books/")), settings);
        ctx.Print(result.ToString());
    }
}
