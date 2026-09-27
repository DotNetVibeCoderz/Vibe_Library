# 14. Dari Scrapy Python

Nama, setting, dan perilaku mengikuti Scrapy; perbedaannya adalah idiom C#.

| Scrapy (Python) | Scrapy.Net (C#) |
|---|---|
| `class QuotesSpider(scrapy.Spider)` | `class QuotesSpider : Spider` |
| `name = "quotes"` | `public override string Name => "quotes";` |
| `start_urls = [...]` | `public override IReadOnlyList<string> StartUrls => [...];` |
| `async def start(self)` / `start_requests` | `public override async IAsyncEnumerable<Request> StartAsync(...)` |
| `def parse(self, response): yield ...` | `public override async IAsyncEnumerable<object> Parse(Response response) { yield return ...; }` |
| `custom_settings = {...}` | `public override void ConfigureSettings(Settings s) { ... }` |
| `response.css("a::attr(href)").get()` | `response.Css("a::attr(href)").Get()` |
| `.getall()`, `.re()`, `.re_first()` | `.GetAll()`, `.Re()`, `.ReFirst()` |
| `response.follow(href, self.parse_item)` | `response.Follow(href, ParseItem)` |
| `response.follow_all(css="a.next")` | `response.FollowAllCss("a.next")` |
| `response.json()` | `((TextResponse)response).Json()` |
| `scrapy.Request(url, callback=..., meta=..., cb_kwargs=...)` | `new Request(url, Callback) { Meta = {...}, CbKwargs = {...} }` |
| `request.replace(url=...)` | `request with { Url = ... }` |
| `FormRequest.from_response(response, formdata={...})` | `FormRequest.FromResponse(response, [new("k", "v")])` |
| `errback=self.on_error` | `.WithErrback(OnError)` |
| `failure.check(HttpError)` | `failure.Is<HttpErrorException>()` |
| `raise DropItem("...")` | `throw new DropItemException("...")` |
| `raise CloseSpider("...")` | `throw new CloseSpiderException("...")` |
| `raise IgnoreRequest` | `throw new IgnoreRequestException()` |
| `class Product(scrapy.Item): name = scrapy.Field()` | record/class, atau `class Product() : Item("name")` |
| `ItemLoader(item=Product(), response=response)` | `new ItemLoader<Product>(response)` |
| `MapCompose(str.strip)`, `TakeFirst()`, `Join()` | `new MapCompose(Transforms.Strip)`, `new TakeFirst()`, `new Join()` |
| `def process_item(self, item, spider)` | `override ValueTask<object> ProcessItemAsync(object item, Spider spider)` |
| `def process_request(self, request, spider)` | `override ValueTask<DownloadResult> ProcessRequestAsync(Request request, Spider spider)` |
| `return None` / `return response` / `return request` | `DownloadResult.Continue` / `response` / `request` |
| `@classmethod from_crawler(cls, crawler)` | parameter konstruktor (`Crawler`, `Settings`, `IStatsCollector`, `ILogger`, ...) atau `static FromCrawler(Crawler)` |
| `raise NotConfigured` | `throw new NotConfiguredException()` |
| `DOWNLOADER_MIDDLEWARES = {"path.Class": 543}` | `settings.DownloaderMiddlewares.Add<Class>(543)` atau `scrapy.json` |
| `FEEDS = {"items.json": {"format": "json"}}` | `settings.Feeds.Add("items.json", "json")` |
| `crawler.signals.connect(fn, signal=signals.item_scraped)` | `crawler.Signals.Connect(Signals.ItemScraped, fn)` |
| `crawler.stats.get_value("item_scraped_count")` | `crawler.Stats.GetValue("item_scraped_count")` |
| `CrawlerProcess(settings).crawl(Spider); process.start()` | `var p = new CrawlerProcess(settings); p.Crawl<Spider>(); await p.StartAsync();` |
| `scrapy crawl quotes -o out.json` | `dotnet run -- crawl quotes -o out.json` |
| `scrapy shell URL` | `scrapynet shell URL` (bahasa perintah) atau `Fetcher` di notebook |
| `scrapy runspider spider.py` | `dotnet run spider.cs` (aplikasi file-based .NET 10) |
| `scrapy-playwright` | `Gravicode.ScrapyNet.Playwright` |
| `scrapyd` | `Gravicode.ScrapyNet.Server` + `Gravicode.ScrapyNet.Platform` |

## Perbedaan perilaku

- `LOG_LEVEL` default-nya `INFO`, bukan `DEBUG`.
- `JOBDIR` menyimpan antrean tertunda saat spider ditutup (berhenti dengan rapi), bukan terus-menerus.
- Selektor XML mengabaikan namespace secara default (`//url/loc` langsung jalan); daftarkan prefix untuk
  mematikannya.
- Hasil skalar XPath (`count()`, `boolean()`) dikembalikan sebagai string XPath: `"10"`, `"true"`.
- Callback boleh async dan bebas melakukan await; tidak ada reactor yang bisa terblokir.
- `DOMAIN_HEADERS`, `ProxyRotationMiddleware`, `RETRY_BACKOFF_*`, `RandomUserAgentMiddleware`,
  `CleaningPipeline`, `ValidationPipeline`, `DuplicatesPipeline`, dan pipeline database adalah tambahan
  yang biasanya didapat pengguna Scrapy dari plugin.
