# 3. Spider

## Spider

```csharp
public sealed class ProductsSpider : Spider
{
    public override string Name => "products";                        // default: nama kelas tanpa "Spider", huruf kecil
    public override IReadOnlyList<string> AllowedDomains => ["shop.example"];
    public override IReadOnlyList<string> StartUrls => ["https://shop.example/catalog"];

    public override void ConfigureSettings(Settings settings)        // custom_settings milik Scrapy
    {
        settings.Set(SettingKeys.DownloadDelay, 0.5);
        settings.ItemPipelines.Add<PricePipeline>(300);
    }

    public override async IAsyncEnumerable<object> Parse(Response response) { ... }
}
```

| Anggota | Kegunaan |
|---|---|
| `Name` | Dipakai oleh `crawl NAME`, log, dan statistik |
| `AllowedDomains` | Request ke domain lain dibuang oleh middleware offsite |
| `StartUrls` | Dipakai oleh `StartAsync` bawaan |
| `StartAsync()` | Override untuk start request khusus (async, dibaca secara malas) |
| `Parse(Response)` | Callback default |
| `ConfigureSettings(Settings)` | Setting per spider pada prioritas spider |
| `HandleHttpStatusList` | Status non-2xx yang ingin diterima callback |
| `Arguments` / properti yang bisa di-set | Argumen spider (`-a key=value`) |
| `State` | Dictionary yang dipertahankan antar-run dengan `JOBDIR` |
| `Logger` | `ILogger` bernama sesuai spider |
| `OnOpenedAsync()` / `OnClosedAsync(reason)` | Hook siklus hidup |

### Callback

Callback menerima `Response` dan meng-yield item serta request. Tulis dengan salah satu cara:

```csharp
// async iterator — bisa await (database, API, jeda)
private async IAsyncEnumerable<object> ParseProduct(Response response)
{
    var stock = await _inventory.LookupAsync(response.Css("#sku::text").Get());
    yield return new Product(response.Css("h1::text").Get(), stock);
}

// iterator biasa
private IEnumerable<object> ParseCategory(Response response)
{
    foreach (var link in response.Css("a.product::attr(href)").GetAll())
        yield return response.Follow(link, ParseProduct);
}
```

Kirim data ke callback berikutnya lewat `CbKwargs` atau `Meta`, atau lewat closure:

```csharp
var request = response.Follow(url, ParseProduct);
request.CbKwargs["category"] = name;
yield return request;
// di ParseProduct: var category = (string)response.CbKwargs["category"]!;

yield return response.Follow(url, r => ParseProduct(r, name));   // closure (tidak disimpan oleh JOBDIR)
```

Hanya callback berupa method bernama milik spider yang bertahan saat jeda/lanjut, karena scheduler
menyimpan nama method-nya.

### Request

`Request` adalah record: gunakan `with` untuk menyalin dengan perubahan.

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

`FormRequest` mengirim formulir (`FormRequest.FromResponse` mengisi formulir dari halaman, termasuk token
CSRF tersembunyi) dan `JsonRequest` mengirim body JSON.

### Errback

```csharp
yield return new Request(url, Parse).WithErrback(failure =>
{
    if (failure.Is<HttpErrorException>()) Logger.LogWarning("HTTP {Status} untuk {Url}", failure.Response!.Status, failure.Request.Url);
    else if (failure.Is<TimeoutException>()) Logger.LogWarning("Timeout: {Url}", failure.Request.Url);
});
```

Errback juga bisa meng-yield item atau request baru (`WithErrback(Func<Failure, IEnumerable<object>>)`).

## CrawlSpider

Deklarasikan tautan mana yang diikuti, alih-alih menuliskan kodenya:

```csharp
public sealed class BooksSpider : CrawlSpider
{
    public override IReadOnlyList<string> StartUrls => ["https://books.toscrape.com/"];

    protected override IReadOnlyList<Rule> Rules =>
    [
        new(new LinkExtractor { RestrictCss = ["ul.nav-list"], Allow = ["/category/"] }),      // ikuti
        new(new LinkExtractor { RestrictCss = ["article.product_pod h3"] }, ParseBook),        // parse
    ];

    private IEnumerable<object> ParseBook(Response r) { ... }
}
```

Aturan tanpa callback mengikuti tautan secara default; dengan callback tidak, kecuali Anda
men-set `Follow = true`. `ProcessLinks` dan `ProcessRequest` memungkinkan menyaring atau mengubah hasil
aturan. Override `ParseStartUrl` untuk mengekstrak dari halaman awal juga.

Opsi `LinkExtractor`: `Allow`, `Deny` (regex), `AllowDomains`, `DenyDomains`, `RestrictCss`,
`RestrictXpaths`, `RestrictText`, `Tags`, `Attrs`, `Canonicalize`, `Unique`, `DenyExtensions`
(default: daftar ekstensi biner milik Scrapy), `ProcessValue`, `KeepFragments`.

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

Spider ini membaca baris `Sitemap:` dari robots.txt, mengikuti indeks sitemap (`SitemapFollow`),
mendekompresi sitemap ber-gzip (dengan batas ukuran, sehingga "bom dekompresi" tidak menghabiskan
memori), opsional menyertakan alternatif `hreflang` (`SitemapAlternateLinks`), dan bisa menyaring entri
berdasarkan tanggal dengan `SitemapFilter`. Pembaca XML melarang DTD, sehingga sitemap berbahaya tidak
bisa memicu XXE.

## XmlFeedSpider dan CsvFeedSpider

```csharp
public sealed class FeedSpider : XmlFeedSpider
{
    public override IReadOnlyList<string> StartUrls => ["https://shop.example/feed.xml"];
    public override string IterTag => "item";                      // g:item juga bisa
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

`XmlFeedSpider` membaca dokumen node demi node (`Iterator = "iternodes"`, default) sehingga memori tetap
datar pada feed besar; `Iterator = "xml"` membangun DOM penuh bila Anda butuh XPath bebas.

## Argumen spider

```bash
dotnet run -- crawl products -a category=books -a max_pages=5
```

Argumen masuk ke `spider.Arguments` dan ke properti publik yang namanya cocok (`category`,
`Category`, `max_pages` → `MaxPages`), dikonversi ke tipe properti.

## Menutup spider dari callback

```csharp
if (response.Css(".captcha").Any()) throw new CloseSpiderException("blocked_by_captcha");
```
