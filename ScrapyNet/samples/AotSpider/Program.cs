// Scrapy.Net compiled ahead of time. Run it with "dotnet run", or publish it as a single native
// executable with "dotnet publish -c Release" and run the binary from bin/Release/net10.0/<rid>/publish.
using ScrapyNet;
using ScrapyNet.Loaders;
using ScrapyNet.Sandbox;

await using var site = await SandboxSite.StartAsync();
var output = Path.Combine(Path.GetTempPath(), "scrapynet-aot", "books.jsonl");

var settings = new Settings();
settings.Set(SettingKeys.LogLevel, "WARNING");
settings.Feeds.Add(output, new FeedOptions { Format = "jsonlines", Overwrite = true });

var result = await Scrapy.CrawlAsync(new BooksSpider(site.Url("/books/")), settings);
Console.WriteLine(result);
Console.WriteLine($"Generated accessors: {ItemAccessors.IsRegistered(typeof(BookItem))}");
Console.WriteLine($"First line of {output}:");
Console.WriteLine(File.ReadLines(output).First());

[ScrapyItem]
public sealed record BookItem
{
    public string? Title { get; init; }
    public decimal Price { get; init; }
    public string? Url { get; init; }
}

public sealed class BooksSpider(string startUrl) : Spider
{
    public override string Name => "aot-books";
    public override IReadOnlyList<string> StartUrls => [startUrl];

    public override async IAsyncEnumerable<object> Parse(Response response)
    {
        foreach (var href in response.Css("article.product_pod h3 a::attr(href)").GetAll())
            yield return response.Follow(href, ParseBook);
        if (response.Css("li.next a::attr(href)").Get() is { } next)
            yield return response.Follow(next, Parse);
        await Task.CompletedTask;
    }

    private async IAsyncEnumerable<object> ParseBook(Response response)
    {
        var loader = new ItemLoader<BookItem>(response);
        loader.AddCss(b => b.Title, "h1::text", Transforms.Clean);
        loader.AddCss(b => b.Price, "p.price_color::text", Transforms.Price);
        loader.AddValue(b => b.Url, response.Url);
        yield return loader.LoadItem();
        await Task.CompletedTask;
    }
}
