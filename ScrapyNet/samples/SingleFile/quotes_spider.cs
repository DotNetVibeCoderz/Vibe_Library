// A complete Scrapy.Net spider in one file — .NET 10 runs it directly:
//
//     dotnet run quotes_spider.cs
//     dotnet run quotes_spider.cs -- -o quotes.jsonl
//
// Outside this repository, replace the #:project line with:
//     #:package Gravicode.ScrapyNet@0.1.0
#:project ../../src/ScrapyNet.Core/ScrapyNet.Core.csproj

using ScrapyNet;

var settings = new Settings();
settings.Set(SettingKeys.RobotsTxtObey, true);
settings.Set(SettingKeys.DownloadDelay, 0.5);
settings.Set(SettingKeys.CloseSpiderPageCount, 3);   // be gentle: the first three pages only
if (args is ["-o", var output]) settings.Feeds.Add(output, new FeedOptions { Overwrite = true });

var (result, items) = await Scrapy.CrawlAndCollectAsync(new QuotesSpider(), settings);
foreach (var item in items.Take(5)) Console.WriteLine(ItemAdapter.For(item));
Console.WriteLine(result);

sealed class QuotesSpider : Spider
{
    public override string Name => "quotes";
    public override IReadOnlyList<string> StartUrls => ["https://quotes.toscrape.com/"];

    public override async IAsyncEnumerable<object> Parse(Response response)
    {
        foreach (var quote in response.Css("div.quote"))
            yield return new
            {
                Text = quote.Css("span.text::text").Get(),
                Author = quote.Css("small.author::text").Get(),
                Tags = quote.Css("a.tag::text").GetAll(),
            };

        if (response.Css("li.next a::attr(href)").Get() is { } next)
            yield return response.Follow(next, Parse);
        await Task.CompletedTask;
    }
}
