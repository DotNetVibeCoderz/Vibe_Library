using ScrapyGallery.Infrastructure;
using ScrapyNet;
using ScrapyNet.Loaders;
using ScrapyNet.Pipelines;

namespace ScrapyGallery.Demos;

public sealed class PipelinesDemo : Demo
{
    public override DemoGroup Group => DemoGroup.Process;
    public override string Title => "Item pipelines";
    public override string TitleId => "Pipeline item";
    public override string Summary => "Every item passes through ordered pipelines: clean the text, validate required fields and business rules, drop duplicates, then enrich. Dropped items are counted and logged with the reason.";
    public override string SummaryId => "Setiap item melewati pipeline berurutan: bersihkan teks, validasi field wajib dan aturan bisnis, buang duplikat, lalu perkaya. Item yang dibuang dihitung dan dicatat alasannya.";
    public override IReadOnlyList<string> Concepts => ["ItemPipeline", "ValidationPipeline", "CleaningPipeline", "DuplicatesPipeline", "DropItemException"];

    public override IReadOnlyList<DemoInput> Inputs => [new("minPrice", "Drop books cheaper than (£)", "Buang buku lebih murah dari (£)", "20")];

    // <demo>
    public sealed class ListingSpider(string url) : CrawlSpider
    {
        public override IReadOnlyList<string> StartUrls => [url];
        protected override IReadOnlyList<Rule> Rules => [new(new LinkExtractor { RestrictCss = ["li.next"] }, ParseListing) { Follow = true }];

        public override IAsyncEnumerable<object> ParseStartUrl(Response response) => Callbacks.FromSync<Response>(ParseListing)(response);

        private IEnumerable<object> ParseListing(Response response)
        {
            foreach (var book in response.Css("article.product_pod"))
                yield return new Dictionary<string, object?>
                {
                    ["title"] = book.Css("h3 a::attr(title)").Get(),
                    ["price"] = Transforms.Price(book.Css("p.price_color::text").Get()),
                    ["stock"] = book.Css("p.availability::text").Get(),
                };
        }
    }

    /// <summary>A custom pipeline: adds a price band.</summary>
    public sealed class PriceBandPipeline : ItemPipeline
    {
        public override ValueTask<object> ProcessItemAsync(object item, Spider spider)
        {
            var book = (Dictionary<string, object?>)item;
            book["band"] = (decimal)book["price"]! switch { < 30 => "budget", < 45 => "standard", _ => "premium" };
            return ValueTask.FromResult(item);
        }
    }

    public static void ConfigurePipelines(Settings settings, double minPrice)
    {
        settings.ItemPipelines
            .Add(new CleaningPipeline().Field("title", Transforms.Upper), 100)
            .Add(new ValidationPipeline()
                .Require("title", "price")
                .Range("price", min: minPrice)
                .Rule("in stock", item => (string?)((Dictionary<string, object?>)item)["stock"] == "In stock"), 200)
            .Add(new DuplicatesPipeline("title"), 300)
            .Add<PriceBandPipeline>(400);
    }
    // </demo>

    public override async Task RunAsync(DemoContext ctx)
    {
        var settings = ctx.NewSettings();
        ConfigurePipelines(settings, double.TryParse(ctx.Input("minPrice"), System.Globalization.CultureInfo.InvariantCulture, out var p) ? p : 20);
        var result = await ctx.CrawlAsync(new ListingSpider(ctx.Site.Url("/books/")), settings);
        ctx.Print(result.ToString());
        ctx.Print($"kept {result.ItemCount}, dropped {result.Stats.GetValueOrDefault("item_dropped_count") ?? 0} — see the Log tab for each reason");
    }
}
