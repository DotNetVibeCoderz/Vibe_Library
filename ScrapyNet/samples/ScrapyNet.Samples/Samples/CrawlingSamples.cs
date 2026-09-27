using ScrapyNet.Loaders;
using ScrapyNet.Sandbox;

namespace ScrapyNet.Samples;

// ---- quotes: the Scrapy tutorial spider -----------------------------------------------------------

public sealed record Quote(string Text, string Author, List<string> Tags);

public sealed class QuotesSpider(string startUrl) : Spider
{
    public override string Name => "quotes";
    public override IReadOnlyList<string> StartUrls => [startUrl];

    public override async IAsyncEnumerable<object> Parse(Response response)
    {
        foreach (var quote in response.Css("div.quote"))
        {
            yield return new Quote(
                quote.Css("span.text::text").Get()!.Trim('“', '”'),
                quote.Css("small.author::text").Get()!,
                quote.Css("div.tags a.tag::text").GetAll());
        }

        // Follow pagination until there is no "Next" link.
        if (response.Css("li.next a::attr(href)").Get() is { } next)
            yield return response.Follow(next, Parse);
        await Task.CompletedTask;
    }
}

public static class QuotesSample
{
    public static async Task RunAsync(SandboxSite site)
    {
        var output = Path.Combine(SampleRegistry.OutputDir("quotes"), "quotes.json");
        var settings = SampleRegistry.Defaults();
        settings.Feeds.Add(output, new FeedOptions { Format = "json", Overwrite = true, Indent = 2 });

        var result = await new Crawler(new QuotesSpider(site.Url("/quotes/")), settings).CrawlAsync();
        Console.WriteLine($"{result}\nSaved to {output}");
    }
}

// ---- books: CrawlSpider with rules and a typed item loader ------------------------------------------

public sealed class Book
{
    public string? Title { get; set; }
    public decimal Price { get; set; }
    public int Rating { get; set; }
    public int Stock { get; set; }
    public string? Upc { get; set; }
    public string? Url { get; set; }
}

public sealed class BooksSpider(string startUrl) : CrawlSpider
{
    public override string Name => "books";
    public override IReadOnlyList<string> StartUrls => [startUrl];

    protected override IReadOnlyList<Rule> Rules =>
    [
        // Pagination links are followed but not parsed.
        new(new LinkExtractor { RestrictCss = ["li.next"] }),
        // Product links are parsed by ParseBook.
        new(new LinkExtractor { RestrictCss = ["article.product_pod h3"] }, ParseBook),
    ];

    private IEnumerable<object> ParseBook(Response response)
    {
        var loader = new ItemLoader<Book>(response);
        loader.AddCss(b => b.Title, "div.product_main h1::text", Transforms.Clean);
        loader.AddCss(b => b.Price, "div.product_main p.price_color::text", Transforms.Price);
        loader.AddCss(b => b.Rating, "div.product_main p.star-rating::attr(class)", Transforms.Str(RatingFromClass));
        loader.AddCss(b => b.Stock, "div.product_main p.availability::text", Transforms.ToInt);
        loader.AddXpath(b => b.Upc, "//th[text()='UPC']/following-sibling::td/text()");
        loader.AddValue(b => b.Url, response.Url);
        yield return loader.LoadItem();
    }

    private static object? RatingFromClass(string css) =>
        css.Split(' ').Last() switch { "One" => 1, "Two" => 2, "Three" => 3, "Four" => 4, "Five" => 5, _ => 0 };
}

public static class BooksSample
{
    public static async Task RunAsync(SandboxSite site)
    {
        var output = Path.Combine(SampleRegistry.OutputDir("books"), "books.csv");
        var settings = SampleRegistry.Defaults();
        settings.Feeds.Add(output, new FeedOptions { Format = "csv", Overwrite = true, Fields = ["Title", "Price", "Rating", "Stock", "Upc"] });
        var (result, items) = await Scrapy.CrawlAndCollectAsync(new BooksSpider(site.Url("/books/")), settings);
        var books = items.Cast<Book>().ToList();
        Console.WriteLine($"{result}\nCheapest: {books.MinBy(b => b.Price)!.Title} ({books.Min(b => b.Price):0.00}) · 5-star books: {books.Count(b => b.Rating == 5)}\nSaved to {output}");
    }
}

// ---- login: forms, CSRF and cookies ----------------------------------------------------------------

public sealed class LoginSpider(string loginUrl) : Spider
{
    public override string Name => "login";
    public override IReadOnlyList<string> StartUrls => [loginUrl];

    public override async IAsyncEnumerable<object> Parse(Response response)
    {
        // FromResponse keeps the hidden csrf_token field and fills in ours.
        yield return FormRequest.FromResponse(response,
            [new("username", SandboxSite.Username), new("password", SandboxSite.Password)],
            callback: AfterLogin);
        await Task.CompletedTask;
    }

    private IEnumerable<object> AfterLogin(Response response)
    {
        if (response.Css("p.error").Get() is { } error)
        {
            Logger.LogErrorMessage($"Login failed: {error}");
            yield break;
        }
        yield return new
        {
            Greeting = response.Css("h1.welcome::text").Get(),
            Orders = response.Css("li.order::text").GetAll(),
        };
    }
}

public static class LoginSample
{
    public static async Task RunAsync(SandboxSite site)
    {
        var (_, items) = await Scrapy.CrawlAndCollectAsync(new LoginSpider(site.Url("/login")), SampleRegistry.Defaults("WARNING"));
        foreach (var item in items) Console.WriteLine(ItemAdapter.For(item));
    }
}

// ---- api: JSON with per-domain API keys ------------------------------------------------------------

public sealed class ProductsApiSpider(string startUrl) : Spider
{
    public override string Name => "products_api";
    public override IReadOnlyList<string> StartUrls => [startUrl];

    public override async IAsyncEnumerable<object> Parse(Response response)
    {
        var json = ((TextResponse)response).Json()!;
        foreach (var p in json["products"]!.AsArray())
            yield return new { Id = (int)p!["id"]!, Title = (string)p["title"]!, Price = (decimal)p["price"]!, InStock = (int)p["stock"]! > 0 };

        if (json["next"]?.GetValue<string>() is { } next) yield return response.Follow(next, Parse);
        await Task.CompletedTask;
    }
}

public static class ApiSample
{
    public static async Task RunAsync(SandboxSite site)
    {
        var settings = SampleRegistry.Defaults("WARNING");
        // The key is only ever sent to this host, never to links that lead elsewhere.
        settings.Set(SettingKeys.DomainHeaders, new Dictionary<string, object?>
        {
            ["127.0.0.1"] = new Dictionary<string, object?> { ["X-Api-Key"] = SandboxSite.ApiKey },
        });
        var (result, items) = await Scrapy.CrawlAndCollectAsync(new ProductsApiSpider(site.Url("/api/secure/products?page=1")), settings);
        Console.WriteLine($"{result}\nFirst: {ItemAdapter.For(items[0])}");
    }
}

// ---- sitemap ---------------------------------------------------------------------------------------

public sealed class SandboxSitemapSpider(string robotsUrl) : SitemapSpider
{
    public override string Name => "sitemap";
    public override IReadOnlyList<string> SitemapUrls => [robotsUrl];

    protected override IReadOnlyList<SitemapRule> SitemapRules =>
    [
        new("/books/catalogue/", ParseBook),
        new("/quotes/author/", ParseAuthor),
    ];

    private IEnumerable<object> ParseBook(Response r) { yield return new { Type = "book", Name = r.Css("h1::text").Get() }; }

    private IEnumerable<object> ParseAuthor(Response r) { yield return new { Type = "author", Name = r.Css("h3.author-title::text").Get(), Born = r.Css(".author-born-date::text").Get() }; }
}

public static class SitemapSample
{
    public static async Task RunAsync(SandboxSite site)
    {
        var (result, items) = await Scrapy.CrawlAndCollectAsync(new SandboxSitemapSpider(site.Url("/robots.txt")), SampleRegistry.Defaults("WARNING"));
        foreach (var group in items.GroupBy(i => ItemAdapter.For(i).Get("Type"))) Console.WriteLine($"{group.Key}: {group.Count()}");
        Console.WriteLine(result);
    }
}

// ---- feeds -----------------------------------------------------------------------------------------

public sealed class ProductXmlSpider(string url) : XmlFeedSpider
{
    public override string Name => "xmlfeed";
    public override IReadOnlyList<string> StartUrls => [url];
    public override string IterTag => "item";

    protected override IEnumerable<object> ParseNode(Response response, Selector node)
    {
        yield return new
        {
            Id = node.Xpath("//id/text()").Get(),
            Title = node.Xpath("//title/text()").Get(),
            Price = Transforms.Price(node.Xpath("//price/text()").Get()),
        };
    }
}

public sealed class ProductCsvSpider(string url) : CsvFeedSpider
{
    public override string Name => "csvfeed";
    public override IReadOnlyList<string> StartUrls => [url];

    protected override IEnumerable<object> ParseRow(Response response, IReadOnlyDictionary<string, string> row)
    {
        if (int.Parse(row["stock"]) > 0) yield return new { Title = row["title"], Category = row["category"] };
    }
}

public static class FeedsSample
{
    public static async Task RunAsync(SandboxSite site)
    {
        var (xml, xmlItems) = await Scrapy.CrawlAndCollectAsync(new ProductXmlSpider(site.Url("/feeds/products.xml")), SampleRegistry.Defaults("WARNING"));
        var (csv, csvItems) = await Scrapy.CrawlAndCollectAsync(new ProductCsvSpider(site.Url("/feeds/products.csv")), SampleRegistry.Defaults("WARNING"));
        Console.WriteLine($"XML feed: {xmlItems.Count} products, e.g. {ItemAdapter.For(xmlItems[0])}");
        Console.WriteLine($"CSV feed: {csvItems.Count} products in stock, by category: " +
            string.Join(", ", csvItems.GroupBy(i => ItemAdapter.For(i).Get("Category")).Select(g => $"{g.Key}={g.Count()}")));
    }
}

// ---- toscrape: the real site -------------------------------------------------------------------------

public static class ToScrapeSample
{
    public static async Task RunAsync(SandboxSite _)
    {
        var settings = SampleRegistry.Defaults();
        settings.Set(SettingKeys.RobotsTxtObey, true);
        settings.Set(SettingKeys.DownloadDelay, 0.5);
        settings.Set(SettingKeys.CloseSpiderPageCount, 3);
        var (result, items) = await Scrapy.CrawlAndCollectAsync(new QuotesSpider("https://quotes.toscrape.com/"), settings);
        foreach (var q in items.Cast<Quote>().Take(3)) Console.WriteLine($"“{q.Text}” — {q.Author}");
        Console.WriteLine(result);
    }
}

internal static class SampleExtensions
{
    public static void LogErrorMessage(this Microsoft.Extensions.Logging.ILogger logger, string message) =>
        Microsoft.Extensions.Logging.LoggerExtensions.LogError(logger, "{Message}", message);
}
