# 8. Command line and shell

## The `scrapynet` tool

```bash
dotnet tool install -g Gravicode.ScrapyNet.Cli
```

| Command | What it does |
|---|---|
| `scrapynet startproject NAME [DIR]` | Creates a project: `.csproj`, `Program.cs`, `Items.cs`, `Pipelines.cs`, `Middlewares.cs`, `scrapy.json` (+ dev/prod), `Spiders/` |
| `scrapynet genspider [-t TEMPLATE] NAME DOMAIN` | Adds a spider from `basic`, `crawl`, `sitemap`, `xmlfeed` or `csvfeed` |
| `scrapynet crawl NAME [-a k=v] [-o FILE] [-O FILE] [-s K=V]` | Runs a spider of the project in the current folder |
| `scrapynet list` | Lists the project's spiders |
| `scrapynet parse URL [--spider NAME] [-c CALLBACK] [-d DEPTH]` | Shows what a callback extracts from one URL |
| `scrapynet fetch URL [--headers]` | Downloads through the full middleware stack and prints the body or headers |
| `scrapynet view URL` | Opens the page as Scrapy.Net received it in your browser |
| `scrapynet shell [URL\|FILE]` | Interactive selector console |
| `scrapynet settings --get KEY` | Prints a setting after all layers are applied |
| `scrapynet version [-v]` | Version and environment |
| `scrapynet sandbox [--port 8080]` | Runs the offline practice website |
| `scrapynet bench` | Crawls the local sandbox as fast as possible and reports pages/min |

Global options: `-s KEY=VALUE` (repeatable, JSON values allowed), `-L LEVEL`, `--nolog`,
`--logfile FILE`, `--env NAME`, `--settings FILE`.

`-o items.json:jsonlines` picks the format explicitly; otherwise it comes from the extension.

## Project layout

```
bookshop/
  bookshop.csproj          references Gravicode.ScrapyNet
  Program.cs               return await ScrapyCommandLine.RunAsync(args, typeof(Program).Assembly);
  scrapy.json              project settings
  scrapy.dev.json          HTTP cache on, DEBUG logging (SCRAPYNET_ENV=dev)
  scrapy.prod.json         AutoThrottle on, JOBDIR (SCRAPYNET_ENV=prod)
  Items.cs  Pipelines.cs  Middlewares.cs
  Spiders/
```

Commands that need your spiders (`crawl`, `list`, `parse`) are forwarded by the global tool to
`dotnet run` inside the project, so the project's own `Program.cs` runs them with its compiled
spiders. You can always call it directly: `dotnet run -- crawl books -o books.csv`.

## The shell

C# has no expression REPL, so the shell speaks a small command language:

```
$ scrapynet shell https://quotes.toscrape.com/
[s] <200 https://quotes.toscrape.com/> (11,053 bytes, HtmlResponse, 412 ms)
>>> css small.author::text
[0] Albert Einstein
[1] J.K. Rowling
...
>>> xpath count(//div[@class='quote'])
[0] 10
>>> re \w+ Einstein
>>> links li.next
[0] https://quotes.toscrape.com/page/2/  Next →
>>> headers
>>> view
>>> exit
```

Commands: `fetch`, `load`, `css`, `xpath`, `re`, `text`, `get`, `links`, `json`, `headers`, `status`,
`url`, `body`, `view`, `curl`, `help`, `exit`. For full C# interactivity, use `Fetcher` in a
[.NET Interactive notebook](12-gallery-samples-notebooks.md):

```csharp
var response = await Fetcher.FetchAsync("https://quotes.toscrape.com/");
response.Css("span.text::text").GetAll()
```

## Single-file spiders (.NET 10)

.NET 10 runs a single `.cs` file directly — the equivalent of `scrapy runspider`:

```csharp
// quotes_spider.cs
#:package Gravicode.ScrapyNet@0.1.0
using ScrapyNet;

var (result, items) = await Scrapy.CrawlAndCollectAsync(new QuotesSpider(), new Settings());
Console.WriteLine(result);

sealed class QuotesSpider : Spider
{
    public override IReadOnlyList<string> StartUrls => ["https://quotes.toscrape.com/"];
    public override async IAsyncEnumerable<object> Parse(Response response)
    {
        foreach (var q in response.Css("div.quote")) yield return new { Text = q.Css("span.text::text").Get() };
        await Task.CompletedTask;
    }
}
```

```bash
dotnet run quotes_spider.cs
```

File-based apps default to AOT-compatible settings with reflection-based JSON switched off.
Scrapy.Net's own JSON handling works regardless; if your script serializes its own types with
`System.Text.Json`, add `#:property JsonSerializerIsReflectionEnabledByDefault=true`.
