# 8. Command line dan shell

## Tool `scrapynet`

```bash
dotnet tool install -g Gravicode.ScrapyNet.Cli
```

| Perintah | Fungsi |
|---|---|
| `scrapynet startproject NAMA [DIR]` | Membuat proyek: `.csproj`, `Program.cs`, `Items.cs`, `Pipelines.cs`, `Middlewares.cs`, `scrapy.json` (+ dev/prod), `Spiders/` |
| `scrapynet genspider [-t TEMPLATE] NAMA DOMAIN` | Menambah spider dari template `basic`, `crawl`, `sitemap`, `xmlfeed`, atau `csvfeed` |
| `scrapynet crawl NAMA [-a k=v] [-o FILE] [-O FILE] [-s K=V]` | Menjalankan spider milik proyek di folder saat ini |
| `scrapynet list` | Menampilkan daftar spider proyek |
| `scrapynet parse URL [--spider NAMA] [-c CALLBACK] [-d DEPTH]` | Menampilkan apa yang diekstrak callback dari satu URL |
| `scrapynet fetch URL [--headers]` | Mengunduh lewat seluruh middleware dan menampilkan body atau header |
| `scrapynet view URL` | Membuka halaman seperti yang diterima Scrapy.Net di browser |
| `scrapynet shell [URL\|FILE]` | Konsol selektor interaktif |
| `scrapynet settings --get KEY` | Menampilkan setting setelah semua lapisan diterapkan |
| `scrapynet version [-v]` | Versi dan lingkungan |
| `scrapynet sandbox [--port 8080]` | Menjalankan situs latihan offline |
| `scrapynet bench` | Mengcrawl sandbox lokal secepat mungkin dan melaporkan halaman/menit |

Opsi global: `-s KEY=VALUE` (bisa berulang, nilai JSON diperbolehkan), `-L LEVEL`, `--nolog`,
`--logfile FILE`, `--env NAMA`, `--settings FILE`.

`-o items.json:jsonlines` memilih format secara eksplisit; tanpa itu format diambil dari ekstensi.

## Struktur proyek

```
bookshop/
  bookshop.csproj          mereferensikan Gravicode.ScrapyNet
  Program.cs               return await ScrapyCommandLine.RunAsync(args, typeof(Program).Assembly);
  scrapy.json              setting proyek
  scrapy.dev.json          cache HTTP aktif, logging DEBUG (SCRAPYNET_ENV=dev)
  scrapy.prod.json         AutoThrottle aktif, JOBDIR (SCRAPYNET_ENV=prod)
  Items.cs  Pipelines.cs  Middlewares.cs
  Spiders/
```

Perintah yang membutuhkan spider Anda (`crawl`, `list`, `parse`) diteruskan oleh tool global ke
`dotnet run` di dalam proyek, sehingga `Program.cs` milik proyek menjalankannya bersama spider yang
sudah dikompilasi. Anda selalu bisa memanggilnya langsung: `dotnet run -- crawl books -o books.csv`.

## Shell

C# tidak punya REPL ekspresi, jadi shell memakai bahasa perintah sederhana:

```
$ scrapynet shell https://quotes.toscrape.com/
[s] <200 https://quotes.toscrape.com/> (11,053 bytes, HtmlResponse, 412 ms)
>>> css small.author::text
[0] Albert Einstein
[1] J.K. Rowling
...
>>> xpath count(//div[@class='quote'])
[0] 10
>>> links li.next
[0] https://quotes.toscrape.com/page/2/  Next →
>>> headers
>>> view
>>> exit
```

Perintah: `fetch`, `load`, `css`, `xpath`, `re`, `text`, `get`, `links`, `json`, `headers`, `status`,
`url`, `body`, `view`, `curl`, `help`, `exit`. Untuk interaktivitas C# penuh, gunakan `Fetcher` di
[notebook .NET Interactive](12-galeri-sampel-notebook.md):

```csharp
var response = await Fetcher.FetchAsync("https://quotes.toscrape.com/");
response.Css("span.text::text").GetAll()
```

## Spider satu file (.NET 10)

.NET 10 bisa menjalankan satu file `.cs` langsung — padanan `scrapy runspider`:

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

Aplikasi file-based memakai setting kompatibel AOT secara default, dengan JSON berbasis refleksi
dimatikan. Penanganan JSON milik Scrapy.Net tetap berjalan; bila skrip Anda menserialisasi tipe sendiri
dengan `System.Text.Json`, tambahkan `#:property JsonSerializerIsReflectionEnabledByDefault=true`.
