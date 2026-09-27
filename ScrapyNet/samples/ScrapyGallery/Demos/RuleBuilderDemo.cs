using System.Text.Json;
using ScrapyGallery.Infrastructure;
using ScrapyNet;
using ScrapyNet.Platform;

namespace ScrapyGallery.Demos;

public sealed class RuleBuilderDemo : Demo
{
    public override DemoGroup Group => DemoGroup.Platform;
    public override string Title => "No-code scraping rules";
    public override string TitleId => "Aturan scraping tanpa kode";
    public override string Summary => "A spider written as JSON: item container, fields with CSS or XPath and processors, detail links and pagination. Edit the rules and run again — this is what a web admin would store and the REST API accepts.";
    public override string SummaryId => "Spider yang ditulis sebagai JSON: kontainer item, field dengan CSS atau XPath dan processor, tautan detail, dan paginasi. Ubah aturannya lalu jalankan lagi — inilah yang disimpan admin web dan diterima REST API.";
    public override IReadOnlyList<string> Concepts => ["DeclarativeSpider", "ScrapingRules", "FieldRule", "RuleEvaluator"];

    public override IReadOnlyList<DemoInput> Inputs =>
    [
        new("rules", "Scraping rules (JSON)", "Aturan scraping (JSON)", """
            {
              "detailLinks": ["article.product_pod h3 a"],
              "followLinks": ["li.next a"],
              "fields": {
                "title": { "css": "h1::text", "processors": ["clean"] },
                "price": { "css": "p.price_color::text", "processors": ["price"] },
                "stock": { "css": "p.availability::text", "processors": ["int"] },
                "upc":   { "xpath": "//th[text()='UPC']/following-sibling::td/text()" }
              },
              "requiredFields": ["title", "price"]
            }
            """, Multiline: true),
    ];

    // <demo>
    public static DeclarativeSpider FromJson(string startUrl, string rulesJson)
    {
        var rules = JsonSerializer.Deserialize<ScrapingRules>(rulesJson, ScrapyJson.Web)
            ?? throw new ArgumentException("Rules JSON is empty.");
        return new DeclarativeSpider(new SpiderDefinition
        {
            Id = "books-rules",
            Name = "books-rules",
            Target = new TargetConfig { StartUrls = [startUrl], MaxDepth = 4 },
            Rules = rules,
        });
    }
    // </demo>

    public override async Task RunAsync(DemoContext ctx)
    {
        var spider = FromJson(ctx.Site.Url("/books/"), ctx.Input("rules"));
        var result = await ctx.CrawlAsync(spider, ctx.NewSettings());
        ctx.Print(result.ToString());
    }
}
