# Scrapy.Net

**Scrapy for .NET 10 — the web crawling and scraping framework, rewritten in C#.**

*Dibuat oleh Gravicode Studios dipimpin oleh Kang Fadhil. · Made by Gravicode Studios, led by Kang Fadhil.*

![Scrapy Gallery](https://raw.githubusercontent.com/DotNetVibeCoderz/Vibe_Library/main/ScrapyNet/docs/images/gallery-crawlspider-items.png)

Same engine architecture, component names and setting keys as [Scrapy](https://scrapy.org/), with
async callbacks, typed items and compile-time checked components.

```csharp
using ScrapyNet;

public sealed class QuotesSpider : Spider
{
    public override IReadOnlyList<string> StartUrls => ["https://quotes.toscrape.com/"];

    public override async IAsyncEnumerable<object> Parse(Response response)
    {
        foreach (var quote in response.Css("div.quote"))
            yield return new { Text = quote.Css("span.text::text").Get(), Author = quote.Css("small.author::text").Get() };

        if (response.Css("li.next a::attr(href)").Get() is { } next)
            yield return response.Follow(next, Parse);
        await Task.CompletedTask;
    }
}

var settings = new Settings();
settings.Feeds.Add("quotes.jsonl");
Console.WriteLine(await Scrapy.CrawlAsync(new QuotesSpider(), settings));
```

## Packages

| Package | Adds |
|---|---|
| `Gravicode.ScrapyNet` | Engine, spiders (Crawl/Sitemap/XML/CSV), CSS/XPath selectors, items, loaders, middlewares (retry, redirect, cookies, proxies, robots.txt, cache, AutoThrottle), pipelines (files, images, databases), feed exports, pause/resume, signals, stats, CLI runner |
| `Gravicode.ScrapyNet.Playwright` | JavaScript rendering with page actions |
| `Gravicode.ScrapyNet.AI` | Extraction, chunking, dedup, embeddings, vector search, RAG with OpenAI/Azure/DeepSeek/Ollama |
| `Gravicode.ScrapyNet.Platform` | Declarative spiders, jobs, cron, secrets, monitoring, alerts |
| `Gravicode.ScrapyNet.Server` | REST API with roles (scrapyd-style) |
| `Gravicode.ScrapyNet.Sandbox` | Offline practice website for learning and tests |
| `Gravicode.ScrapyNet.Cli` | `scrapynet` tool: startproject, genspider, crawl, shell, fetch, parse, bench |

## Documentation

- English: https://github.com/DotNetVibeCoderz/Vibe_Library/tree/main/ScrapyNet/docs/en
- Bahasa Indonesia: https://github.com/DotNetVibeCoderz/Vibe_Library/tree/main/ScrapyNet/docs/id
- Scrapy Gallery (desktop demo app), samples and notebooks: https://github.com/DotNetVibeCoderz/Vibe_Library/tree/main/ScrapyNet

MIT License · © 2026 Gravicode Studios
