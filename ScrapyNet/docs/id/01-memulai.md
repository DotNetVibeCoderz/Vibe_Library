# 1. Memulai

## Kebutuhan

- .NET 10 SDK
- Windows, Linux, atau macOS

## Instalasi

Di proyek yang sudah ada:

```bash
dotnet add package Gravicode.ScrapyNet
```

Atau pasang tool command line dan buat proyek baru:

```bash
dotnet tool install -g Gravicode.ScrapyNet.Cli
scrapynet startproject bookshop
cd bookshop
scrapynet genspider books books.toscrape.com
```

## Spider pertama

Spider menentukan dari mana crawl dimulai dan bagaimana setiap respons diubah menjadi item dan
request berikutnya. Spider ini menjelajahi situs latihan publik
[quotes.toscrape.com](https://quotes.toscrape.com):

```csharp
using ScrapyNet;

public sealed class QuotesSpider : Spider
{
    public override string Name => "quotes";
    public override IReadOnlyList<string> StartUrls => ["https://quotes.toscrape.com/"];

    public override async IAsyncEnumerable<object> Parse(Response response)
    {
        foreach (var quote in response.Css("div.quote"))
        {
            yield return new
            {
                Text = quote.Css("span.text::text").Get(),
                Author = quote.Css("small.author::text").Get(),
                Tags = quote.Css("a.tag::text").GetAll(),
            };
        }

        // Ikuti paginasi sampai tidak ada lagi tautan "Next".
        if (response.Css("li.next a::attr(href)").Get() is { } next)
            yield return response.Follow(next, Parse);
        await Task.CompletedTask;
    }
}
```

Perhatikan:

- Callback meng-**yield** item dan request. Objek apa pun selain `Request` adalah item: objek anonim,
  record, class, dictionary, atau `ScrapyNet.Item`.
- Callback boleh berupa `async IAsyncEnumerable<object>` (seperti di atas, bila perlu `await`) atau
  iterator biasa `IEnumerable<object>`.
- `response.Follow` menyelesaikan URL relatif dan memasang callback-nya.

## Menjalankan

Dari kode:

```csharp
var settings = new Settings();
settings.Feeds.Add("quotes.json", new FeedOptions { Overwrite = true });

var result = await Scrapy.CrawlAsync(new QuotesSpider(), settings);
Console.WriteLine(result);   // quotes: finished — 100 items, 10 responses in 1.2s
```

`Scrapy.CrawlAsync` punya overload yang menerima tipe spider, instance spider, `Action<Settings>`, atau
objek `Settings`. `Scrapy.CrawlAndCollectAsync` juga mengembalikan item yang terkumpul — praktis untuk
test dan notebook:

```csharp
var (result, items) = await Scrapy.CrawlAndCollectAsync(new QuotesSpider(), new Settings());
```

Dari proyek hasil `scrapynet startproject`:

```bash
dotnet run -- crawl quotes -o quotes.json
dotnet run -- crawl quotes -O quotes.csv          # -O menimpa, -o menambahkan
dotnet run -- crawl quotes -a tag=humor -s DOWNLOAD_DELAY=1
```

## Beberapa spider dalam satu proses

```csharp
var process = new CrawlerProcess(settings);   // Ctrl+C berhenti dengan rapi, dua kali memaksa
process.Crawl<QuotesSpider>();
process.Crawl<BooksSpider>();
var results = await process.StartAsync();
```

## Latihan offline

Paket `Gravicode.ScrapyNet.Sandbox` menjalankan situs latihan di dalam proses Anda — kutipan, toko
buku, formulir login, API JSON, sitemap, feed, dan endpoint yang sengaja gagal — sehingga Anda bisa
belajar dan menguji tanpa menyentuh situs sungguhan.

Berikan URL awal lewat konstruktor lalu arahkan ke sandbox:

```csharp
public sealed class QuotesSpider(string startUrl) : Spider
{
    public override IReadOnlyList<string> StartUrls => [startUrl];
    // Parse seperti di atas
}

await using var site = await SandboxSite.StartAsync();
var (_, items) = await Scrapy.CrawlAndCollectAsync(new QuotesSpider(site.Url("/quotes/")), new Settings());
```

atau dari command line: `scrapynet sandbox --port 8080`.

## Selanjutnya

- [Arsitektur](02-arsitektur.md) — apa yang terjadi pada sebuah request
- [Spider](03-spider.md) — aturan crawl, sitemap, login, dan API
- [Selektor, item, dan loader](04-selektor-item-loader.md)
