# 14. Coming from Python Scrapy

Names, settings and behaviour follow Scrapy; the differences are C# idiom.

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
| `class Product(scrapy.Item): name = scrapy.Field()` | a record/class, or `class Product() : Item("name")` |
| `ItemLoader(item=Product(), response=response)` | `new ItemLoader<Product>(response)` |
| `MapCompose(str.strip)`, `TakeFirst()`, `Join()` | `new MapCompose(Transforms.Strip)`, `new TakeFirst()`, `new Join()` |
| `def process_item(self, item, spider)` | `override ValueTask<object> ProcessItemAsync(object item, Spider spider)` |
| `def process_request(self, request, spider)` | `override ValueTask<DownloadResult> ProcessRequestAsync(Request request, Spider spider)` |
| `return None` / `return response` / `return request` | `DownloadResult.Continue` / `response` / `request` |
| `@classmethod from_crawler(cls, crawler)` | constructor parameters (`Crawler`, `Settings`, `IStatsCollector`, `ILogger`, ...) or `static FromCrawler(Crawler)` |
| `raise NotConfigured` | `throw new NotConfiguredException()` |
| `DOWNLOADER_MIDDLEWARES = {"path.Class": 543}` | `settings.DownloaderMiddlewares.Add<Class>(543)` or `scrapy.json` |
| `FEEDS = {"items.json": {"format": "json"}}` | `settings.Feeds.Add("items.json", "json")` |
| `crawler.signals.connect(fn, signal=signals.item_scraped)` | `crawler.Signals.Connect(Signals.ItemScraped, fn)` |
| `crawler.stats.get_value("item_scraped_count")` | `crawler.Stats.GetValue("item_scraped_count")` |
| `CrawlerProcess(settings).crawl(Spider); process.start()` | `var p = new CrawlerProcess(settings); p.Crawl<Spider>(); await p.StartAsync();` |
| `scrapy crawl quotes -o out.json` | `dotnet run -- crawl quotes -o out.json` |
| `scrapy shell URL` | `scrapynet shell URL` (command language) or `Fetcher` in a notebook |
| `scrapy runspider spider.py` | `dotnet run spider.cs` (.NET 10 file-based app) |
| `scrapy-playwright` | `Gravicode.ScrapyNet.Playwright` |
| `scrapyd` | `Gravicode.ScrapyNet.Server` + `Gravicode.ScrapyNet.Platform` |

## Behavioural differences

- `LOG_LEVEL` defaults to `INFO` rather than `DEBUG`.
- `JOBDIR` saves the pending queue when the spider closes (graceful stop), not continuously.
- XML selectors ignore namespaces by default (`//url/loc` just works); register a prefix to opt out.
- XPath scalar results (`count()`, `boolean()`) come back as XPath strings: `"10"`, `"true"`.
- Callbacks can be async and await freely; there is no reactor to block.
- `DOMAIN_HEADERS`, `ProxyRotationMiddleware`, `RETRY_BACKOFF_*`, `RandomUserAgentMiddleware`,
  `CleaningPipeline`, `ValidationPipeline`, `DuplicatesPipeline` and the database pipelines are
  additions that Scrapy users usually get from plugins.
