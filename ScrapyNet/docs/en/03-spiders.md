# 3. Spiders

## Spider

```csharp
public sealed class ProductsSpider : Spider
{
    public override string Name => "products";                        // default: class name minus "Spider", lower-case
    public override IReadOnlyList<string> AllowedDomains => ["shop.example"];
    public override IReadOnlyList<string> StartUrls => ["https://shop.example/catalog"];

    public override void ConfigureSettings(Settings settings)        // Scrapy's custom_settings
    {
        settings.Set(SettingKeys.DownloadDelay, 0.5);
        settings.ItemPipelines.Add<PricePipeline>(300);
    }

    public override async IAsyncEnumerable<object> Parse(Response response) { ... }
}
```

| Member | Purpose |
|---|---|
| `Name` | Used by `crawl NAME`, logs and stats |
| `AllowedDomains` | Requests elsewhere are dropped by the offsite middlewares |
| `StartUrls` | Used by the default `StartAsync` |
| `StartAsync()` | Override for custom start requests (async, consumed lazily) |
| `Parse(Response)` | Default callback |
| `ConfigureSettings(Settings)` | Per-spider settings at spider priority |
| `HandleHttpStatusList` | Non-2xx statuses your callbacks want to receive |
| `Arguments` / settable properties | Spider arguments (`-a key=value`) |
| `State` | Dictionary persisted across runs with `JOBDIR` |
| `Logger` | An `ILogger` named after the spider |
| `OnOpenedAsync()` / `OnClosedAsync(reason)` | Lifecycle hooks |

### Callbacks

A callback receives a `Response` and yields items and requests. Write it either way:

```csharp
// async iterator — can await (databases, APIs, delays)
private async IAsyncEnumerable<object> ParseProduct(Response response)
{
    var stock = await _inventory.LookupAsync(response.Css("#sku::text").Get());
    yield return new Product(response.Css("h1::text").Get(), stock);
}

// plain iterator
private IEnumerable<object> ParseCategory(Response response)
{
    foreach (var link in response.Css("a.product::attr(href)").GetAll())
        yield return response.Follow(link, ParseProduct);
}
```

Pass data to the next callback with `CbKwargs` or `Meta`, or with a closure:

```csharp
var request = response.Follow(url, ParseProduct);
request.CbKwargs["category"] = name;
yield return request;
// in ParseProduct: var category = (string)response.CbKwargs["category"]!;

yield return response.Follow(url, r => ParseProduct(r, name));   // closure (not saved by JOBDIR)
```

Only callbacks that are named methods of the spider survive a pause/resume, because the scheduler
stores the method name.

### Requests

`Request` is a record: use `with` to copy it with changes.

```csharp
var request = new Request("https://shop.example/search", ParseResults)
{
    Method = "POST",
    Body = Encoding.UTF8.GetBytes("q=books"),
    Headers = { ["Content-Type"] = "application/x-www-form-urlencoded" },
    Priority = 10,
    DontFilter = true,
    Meta = { ["download_timeout"] = 30 },
};
var retry = request with { Priority = 0 };
```

`FormRequest` submits forms (`FormRequest.FromResponse` pre-fills a form from the page, hidden CSRF
tokens included) and `JsonRequest` sends JSON bodies.

### Errbacks

```csharp
yield return new Request(url, Parse).WithErrback(failure =>
{
    if (failure.Is<HttpErrorException>()) Logger.LogWarning("HTTP {Status} for {Url}", failure.Response!.Status, failure.Request.Url);
    else if (failure.Is<TimeoutException>()) Logger.LogWarning("Timed out: {Url}", failure.Request.Url);
});
```

An errback can also yield items or new requests (`WithErrback(Func<Failure, IEnumerable<object>>)`).

## CrawlSpider

Declare which links to follow instead of coding it:

```csharp
public sealed class BooksSpider : CrawlSpider
{
    public override IReadOnlyList<string> StartUrls => ["https://books.toscrape.com/"];

    protected override IReadOnlyList<Rule> Rules =>
    [
        new(new LinkExtractor { RestrictCss = ["ul.nav-list"], Allow = ["/category/"] }),      // follow
        new(new LinkExtractor { RestrictCss = ["article.product_pod h3"] }, ParseBook),        // parse
    ];

    private IEnumerable<object> ParseBook(Response r) { ... }
}
```

A rule without a callback follows links by default; with a callback it doesn't, unless you set
`Follow = true`. `ProcessLinks` and `ProcessRequest` let you filter or modify what a rule produces.
Override `ParseStartUrl` to extract from the start pages too.

`LinkExtractor` options: `Allow`, `Deny` (regexes), `AllowDomains`, `DenyDomains`, `RestrictCss`,
`RestrictXpaths`, `RestrictText`, `Tags`, `Attrs`, `Canonicalize`, `Unique`, `DenyExtensions`
(defaults to Scrapy's list of binary extensions), `ProcessValue`, `KeepFragments`.

## SitemapSpider

```csharp
public sealed class SiteSpider : SitemapSpider
{
    public override IReadOnlyList<string> SitemapUrls => ["https://shop.example/robots.txt"];
    protected override IReadOnlyList<SitemapRule> SitemapRules =>
    [
        new("/product/", ParseProduct),
        new("/blog/", ParseArticle),
    ];
}
```

It reads `Sitemap:` lines from robots.txt, follows sitemap indexes (`SitemapFollow`), decompresses
gzipped sitemaps (bounded, so a decompression bomb can't exhaust memory), optionally includes
`hreflang` alternates (`SitemapAlternateLinks`), and lets you filter entries by date with
`SitemapFilter`. The XML reader prohibits DTDs, so hostile sitemaps can't trigger XXE.

## XmlFeedSpider and CsvFeedSpider

```csharp
public sealed class FeedSpider : XmlFeedSpider
{
    public override IReadOnlyList<string> StartUrls => ["https://shop.example/feed.xml"];
    public override string IterTag => "item";                      // g:item works too
    protected override IEnumerable<object> ParseNode(Response response, Selector node)
    {
        yield return new { Id = node.Xpath("//id/text()").Get(), Price = node.Xpath("//price/text()").Get() };
    }
}

public sealed class PricesSpider : CsvFeedSpider
{
    public override IReadOnlyList<string> StartUrls => ["https://shop.example/prices.csv"];
    public override char Delimiter => ';';
    protected override IEnumerable<object> ParseRow(Response response, IReadOnlyDictionary<string, string> row)
    {
        yield return new { Sku = row["sku"], Price = decimal.Parse(row["price"]) };
    }
}
```

`XmlFeedSpider` streams the document node by node (`Iterator = "iternodes"`, the default) so memory
stays flat on large feeds; `Iterator = "xml"` builds the full DOM when you need arbitrary XPath.

## Spider arguments

```bash
dotnet run -- crawl products -a category=books -a max_pages=5
```

Arguments land in `spider.Arguments` and in any public settable property whose name matches
(`category`, `Category`, `max_pages` → `MaxPages`), converted to the property's type.

## Closing a spider from a callback

```csharp
if (response.Css(".captcha").Any()) throw new CloseSpiderException("blocked_by_captcha");
```
