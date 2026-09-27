using System.Text;
using System.Text.Json;
using ScrapyNet.DownloaderMiddlewares;
using ScrapyNet.Extensions;
using ScrapyNet.Pipelines;
using ScrapyNet.Sandbox;
using Xunit;

namespace ScrapyNet.Tests;

public sealed record Quote(string Text, string Author, List<string> Tags);

public sealed class QuotesSpider(string baseUrl) : Spider
{
    public override string Name => "quotes";
    public override IReadOnlyList<string> StartUrls => [baseUrl + "/quotes/"];

    public override async IAsyncEnumerable<object> Parse(Response response)
    {
        foreach (var q in response.Css("div.quote"))
            yield return new Quote(q.Css("span.text::text").Get()!.Trim('“', '”'), q.Css("small.author::text").Get()!, q.Css("a.tag::text").GetAll());
        if (response.Css("li.next a::attr(href)").Get() is { } next)
            yield return response.Follow(next, Parse);
        await Task.CompletedTask;
    }
}

public sealed class BooksCrawlSpider(string baseUrl) : CrawlSpider
{
    public override IReadOnlyList<string> StartUrls => [baseUrl + "/books/"];
    public override IReadOnlyList<string> AllowedDomains => ["127.0.0.1"];

    protected override IReadOnlyList<Rule> Rules =>
    [
        new(new LinkExtractor { RestrictCss = ["li.next"] }),
        new(new LinkExtractor { RestrictCss = ["article.product_pod h3"] }, ParseBook),
    ];

    private IEnumerable<object> ParseBook(Response r)
    {
        yield return new Dictionary<string, object?>
        {
            ["title"] = r.Css("h1::text").Get(),
            ["price"] = Loaders.Transforms.Price(r.Css("p.price_color::text").Get()),
            ["upc"] = r.Xpath("//th[text()='UPC']/following-sibling::td/text()").Get(),
        };
    }
}

public class CrawlTests(SandboxFixture sandbox)
{
    private string Base => sandbox.Site.BaseUrl;

    [Fact]
    public async Task Quotes_spider_follows_pagination_and_scrapes_every_quote()
    {
        var (result, items) = await Scrapy.CrawlAndCollectAsync(new QuotesSpider(Base), TestSettings.Quiet, TestContext.Current.CancellationToken);
        Assert.Equal("finished", result.FinishReason);
        Assert.Equal(SandboxData.Quotes.Count, items.Count);
        Assert.Equal(SandboxData.Quotes.Count, result.ItemCount);
        Assert.Equal(SandboxData.QuotePages, result.ResponseCount);
        var first = Assert.IsType<Quote>(items[0]);
        Assert.Equal(SandboxData.Quotes[0].Author, first.Author);
        Assert.Equal(SandboxData.Quotes[0].Tags, first.Tags);
    }

    [Fact]
    public async Task Crawl_spider_rules_reach_every_book()
    {
        var (result, items) = await Scrapy.CrawlAndCollectAsync(new BooksCrawlSpider(Base), TestSettings.Quiet, TestContext.Current.CancellationToken);
        Assert.Equal(SandboxData.Books.Count, items.Count);
        var titles = items.Cast<Dictionary<string, object?>>().Select(d => (string)d["title"]!).ToHashSet();
        Assert.Contains(SandboxData.Books[5].Title, titles);
        Assert.All(items.Cast<Dictionary<string, object?>>(), d => Assert.IsType<decimal>(d["price"]));
        Assert.True(result.Stats.ContainsKey("dupefilter/filtered") || result.RequestCount > 0);
    }

    private sealed class SitemapTestSpider(string baseUrl) : SitemapSpider
    {
        public override IReadOnlyList<string> SitemapUrls => [baseUrl + "/robots.txt"];
        protected override IReadOnlyList<SitemapRule> SitemapRules =>
        [
            new("/books/catalogue/", ParseBook),
            new("/quotes/author/", ParseAuthor),
        ];

        private IEnumerable<object> ParseBook(Response r) { yield return new { Kind = "book", Title = r.Css("h1::text").Get() }; }
        private IEnumerable<object> ParseAuthor(Response r) { yield return new { Kind = "author", Title = r.Css("h3.author-title::text").Get() }; }
    }

    [Fact]
    public async Task Sitemap_spider_reads_robots_index_and_gzipped_sitemaps()
    {
        var (_, items) = await Scrapy.CrawlAndCollectAsync(new SitemapTestSpider(Base), TestSettings.Quiet, TestContext.Current.CancellationToken);
        var kinds = items.Select(i => (string)i.GetType().GetProperty("Kind")!.GetValue(i)!).ToList();
        Assert.Equal(SandboxData.Books.Count, kinds.Count(k => k == "book"));
        Assert.Equal(SandboxData.Authors.Count, kinds.Count(k => k == "author"));
    }

    private sealed class LoginSpider(string baseUrl) : Spider
    {
        public override IReadOnlyList<string> StartUrls => [baseUrl + "/login"];

        public override async IAsyncEnumerable<object> Parse(Response response)
        {
            yield return FormRequest.FromResponse(response, [new("username", SandboxSite.Username), new("password", SandboxSite.Password)], callback: AfterLogin);
            await Task.CompletedTask;
        }

        private IEnumerable<object> AfterLogin(Response r)
        {
            yield return new { Welcome = r.Css("h1.welcome::text").Get(), Orders = r.Css("li.order").Count, r.Url };
        }
    }

    [Fact]
    public async Task Login_with_form_request_keeps_session_cookie()
    {
        var (_, items) = await Scrapy.CrawlAndCollectAsync(new LoginSpider(Base), TestSettings.Quiet, TestContext.Current.CancellationToken);
        dynamic account = Assert.Single(items);
        Assert.Equal("Welcome, scrapy!", (string)account.Welcome);
        Assert.Equal(2, (int)account.Orders);
        Assert.EndsWith("/account", (string)account.Url);
    }

    private sealed class ApiSpider(string baseUrl) : Spider
    {
        public override IReadOnlyList<string> StartUrls => [baseUrl + "/api/secure/products?page=1"];

        public override async IAsyncEnumerable<object> Parse(Response response)
        {
            var json = ((TextResponse)response).Json()!;
            foreach (var p in json["products"]!.AsArray()) yield return new { Id = (int)p!["id"]!, Title = (string)p["title"]! };
            if (json["next"]?.GetValue<string>() is { } next) yield return response.Follow(next, Parse);
            await Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Domain_headers_authenticate_json_api()
    {
        var (_, items) = await Scrapy.CrawlAndCollectAsync(new ApiSpider(Base), s =>
        {
            TestSettings.Quiet(s);
            s.Set(SettingKeys.DomainHeaders, new Dictionary<string, object?> { ["127.0.0.1"] = new Dictionary<string, object?> { ["X-Api-Key"] = SandboxSite.ApiKey } });
        }, TestContext.Current.CancellationToken);
        Assert.Equal(SandboxData.Books.Count, items.Count);
    }

    private sealed class ListSpider(IEnumerable<string> urls, Func<Response, IEnumerable<object>>? parse = null) : Spider
    {
        public List<(string Url, int Status)> Seen { get; } = [];
        public List<Failure> Failures { get; } = [];

        public override async IAsyncEnumerable<Request> StartAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            foreach (var url in urls) yield return new Request(url).WithErrback(f => { lock (Failures) Failures.Add(f); });
            await Task.CompletedTask;
        }

        public override async IAsyncEnumerable<object> Parse(Response response)
        {
            lock (Seen) Seen.Add((response.Url, response.Status));
            if (parse is not null) foreach (var x in parse(response)) yield return x;
            await Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Retry_recovers_from_transient_errors()
    {
        var key = Guid.NewGuid().ToString("N");
        var spider = new ListSpider([sandbox.Url($"/flaky/{key}?fail=2")]);
        var result = await Scrapy.CrawlAsync(spider, TestSettings.Quiet, TestContext.Current.CancellationToken);
        Assert.Equal(200, Assert.Single(spider.Seen).Status);
        Assert.Equal(2L, result.Stats["retry/count"]);
    }

    [Fact]
    public async Task Retry_gives_up_and_errback_receives_http_error()
    {
        var key = Guid.NewGuid().ToString("N");
        var spider = new ListSpider([sandbox.Url($"/flaky/{key}?fail=10"), sandbox.Url("/status/404")]);
        var result = await Scrapy.CrawlAsync(spider, s => { TestSettings.Quiet(s); s.Set(SettingKeys.LogLevel, "CRITICAL"); }, TestContext.Current.CancellationToken);
        Assert.Empty(spider.Seen);
        Assert.Equal(2, spider.Failures.Count);
        Assert.All(spider.Failures, f => Assert.True(f.Is<HttpErrorException>()));
        Assert.Equal(1L, result.Stats["retry/max_reached"]);
    }

    [Fact]
    public async Task Dropped_connections_are_retried_then_reported()
    {
        var spider = new ListSpider([sandbox.Url("/drop")]);
        var result = await Scrapy.CrawlAsync(spider, s => { TestSettings.Quiet(s); s.Set(SettingKeys.LogLevel, "CRITICAL"); s.Set(SettingKeys.RetryTimes, 1); }, TestContext.Current.CancellationToken);
        var failure = Assert.Single(spider.Failures);
        Assert.False(failure.Is<HttpErrorException>());
        Assert.Equal(1L, result.Stats["retry/count"]);
    }

    [Fact]
    public async Task Redirects_and_meta_refresh_are_followed_and_recorded()
    {
        var spider = new ListSpider([sandbox.Url("/redirect/3"), sandbox.Url("/meta-refresh")], r =>
        [
            new { r.Url, Hops = r.Request.Meta.GetValueOrDefault(MetaKeys.RedirectUrls) as List<string> },
        ]);
        var (_, items) = await Scrapy.CrawlAndCollectAsync(spider, TestSettings.Quiet, TestContext.Current.CancellationToken);
        Assert.Contains(spider.Seen, s => s.Url.EndsWith("/redirect/0"));
        Assert.Contains(spider.Seen, s => s.Url.EndsWith("/quotes/"));
        dynamic redirected = items.First(i => ((string)((dynamic)i).Url).EndsWith("/redirect/0"));
        Assert.Equal(3, ((List<string>)redirected.Hops).Count);
    }

    [Fact]
    public async Task Timeouts_fail_fast()
    {
        var spider = new ListSpider([sandbox.Url("/slow?ms=3000")]);
        await Scrapy.CrawlAsync(spider, s =>
        {
            TestSettings.Quiet(s);
            s.Set(SettingKeys.LogLevel, "CRITICAL");
            s.Set(SettingKeys.DownloadTimeout, 0.3);
            s.Set(SettingKeys.RetryEnabled, false);
        }, TestContext.Current.CancellationToken);
        Assert.True(Assert.Single(spider.Failures).Is<TimeoutException>());
    }

    private sealed class GraphSpider(string baseUrl) : Spider
    {
        public override IReadOnlyList<string> StartUrls => [baseUrl + "/graph/0"];

        public override async IAsyncEnumerable<object> Parse(Response response)
        {
            yield return new { Node = response.Css("h1.node::text").Get(), response.Request.Depth };
            foreach (var r in response.FollowAllCss("ul.links a", Parse)) yield return r;
            await Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Dupe_filter_visits_each_node_once_and_depth_limit_stops_early()
    {
        var (full, fullItems) = await Scrapy.CrawlAndCollectAsync(new GraphSpider(Base), TestSettings.Quiet, TestContext.Current.CancellationToken);
        var nodes = fullItems.Select(i => (string)((dynamic)i).Node).ToList();
        // Every node once — except node 0: the start request is dont_filter, so (as in Scrapy) it is
        // never recorded as seen and node 0's link to itself fetches it a second time.
        Assert.Equal(200, nodes.Distinct().Count());
        Assert.Equal(201, nodes.Count);
        Assert.True((long)full.Stats["dupefilter/filtered"]! > 0);

        var (_, limited) = await Scrapy.CrawlAndCollectAsync(new GraphSpider(Base), s => { TestSettings.Quiet(s); s.Set(SettingKeys.DepthLimit, 2); }, TestContext.Current.CancellationToken);
        Assert.InRange(limited.Count, 2, 30);
        Assert.All(limited, i => Assert.True((int)((dynamic)i).Depth <= 2));
    }

    [Fact]
    public async Task Robots_txt_is_obeyed()
    {
        var spider = new ListSpider([sandbox.Url("/private/secret.html"), sandbox.Url("/private/public-note.html"), sandbox.Url("/quotes/")]);
        var result = await Scrapy.CrawlAsync(spider, s => { TestSettings.Quiet(s); s.Set(SettingKeys.RobotsTxtObey, true); }, TestContext.Current.CancellationToken);
        Assert.DoesNotContain(spider.Seen, x => x.Url.EndsWith("secret.html"));
        Assert.Contains(spider.Seen, x => x.Url.EndsWith("public-note.html"));
        Assert.Equal(1L, result.Stats["robotstxt/forbidden"]);
    }

    [Fact]
    public async Task Feed_exports_write_json_csv_and_batches()
    {
        var dir = TestSettings.TempDir();
        await Scrapy.CrawlAsync(new QuotesSpider(Base), s =>
        {
            TestSettings.Quiet(s);
            s.Feeds.Add(Path.Combine(dir, "%(name)s.json"), new FeedOptions { Overwrite = true });
            s.Feeds.Add(Path.Combine(dir, "quotes.csv"), new FeedOptions { Fields = ["Author", "Text"] });
            s.Feeds.Add(Path.Combine(dir, "batch-%(batch_id)d.jsonl"), new FeedOptions { BatchItemCount = 20 });
            s.Feeds.Add(Path.Combine(dir, "quotes.xml.gz"), "xml");
        }, TestContext.Current.CancellationToken);

        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "quotes.json")));
        Assert.Equal(SandboxData.Quotes.Count, json.RootElement.GetArrayLength());
        var csv = File.ReadAllLines(Path.Combine(dir, "quotes.csv"));
        Assert.Equal("Author,Text", csv[0]);
        Assert.Equal(SandboxData.Quotes.Count + 1, csv.Length);
        Assert.Equal(3, Directory.GetFiles(dir, "batch-*.jsonl").Length);
        using var gz = new System.IO.Compression.GZipStream(File.OpenRead(Path.Combine(dir, "quotes.xml.gz")), System.IO.Compression.CompressionMode.Decompress);
        var xml = System.Xml.Linq.XDocument.Load(gz);
        Assert.Equal(SandboxData.Quotes.Count, xml.Root!.Elements("item").Count());
    }

    [Fact]
    public async Task Close_spider_item_count_stops_the_crawl()
    {
        var result = await Scrapy.CrawlAsync(new QuotesSpider(Base), s =>
        {
            TestSettings.Quiet(s);
            s.Set(SettingKeys.CloseSpiderItemCount, 15);
            s.Set(SettingKeys.ConcurrentRequests, 1);
        }, TestContext.Current.CancellationToken);
        Assert.Equal("closespider_itemcount", result.FinishReason);
        Assert.InRange(result.ItemCount, 15, 30);
    }

    private sealed class ValidatingPipeline : ItemPipeline
    {
        public override ValueTask<object> ProcessItemAsync(object item, Spider spider) =>
            ((Quote)item).Author == "Rumi" ? throw new DropItemException("no poets") : ValueTask.FromResult(item);
    }

    [Fact]
    public async Task Pipelines_drop_and_transform_items_in_order()
    {
        var (result, items) = await Scrapy.CrawlAndCollectAsync(new QuotesSpider(Base), s =>
        {
            TestSettings.Quiet(s);
            s.Set(SettingKeys.LogLevel, "ERROR");
            s.ItemPipelines.Add<ValidatingPipeline>(100);
            s.ItemPipelines.Add(new LambdaPipeline(i => (Quote)i with { Author = ((Quote)i).Author.ToUpperInvariant() }), 200);
            s.ItemPipelines.Add(new DuplicatesPipeline("Text"), 300);
        }, TestContext.Current.CancellationToken);
        var rumi = SandboxData.Quotes.Count(q => q.Author == "Rumi");
        Assert.Equal(SandboxData.Quotes.Count - rumi, items.Count);
        Assert.All(items.Cast<Quote>(), q => Assert.Equal(q.Author.ToUpperInvariant(), q.Author));
        Assert.Equal((long)rumi, result.Stats["item_dropped_count"]);
    }

    public sealed class MediaItem
    {
        public string? Title { get; set; }
        public List<string> ImageUrls { get; set; } = [];
        public List<MediaFile> Images { get; set; } = [];
        public List<string> FileUrls { get; set; } = [];
        public List<MediaFile> Files { get; set; } = [];
    }

    [Fact]
    public async Task Files_and_images_pipelines_download_media()
    {
        var store = TestSettings.TempDir();
        var spider = new ListSpider([sandbox.Url("/files/")], r =>
        [
            new MediaItem
            {
                Title = "reports",
                FileUrls = r.Css("a.report::attr(href)").GetAll().Select(r.UrlJoin).ToList(),
                ImageUrls = [sandbox.Url("/books/media/1.png"), sandbox.Url("/books/media/2.png")],
            },
        ]);
        var (_, items) = await Scrapy.CrawlAndCollectAsync(spider, s =>
        {
            TestSettings.Quiet(s);
            s.Set(SettingKeys.FilesStore, Path.Combine(store, "files"));
            s.Set(SettingKeys.ImagesStore, Path.Combine(store, "images"));
            s.ItemPipelines.Add<FilesPipeline>(1).Add<ImagesPipeline>(2);
        }, TestContext.Current.CancellationToken);
        var item = Assert.IsType<MediaItem>(Assert.Single(items));
        Assert.Equal(5, item.Files.Count);
        Assert.All(item.Files, f => Assert.True(File.Exists(Path.Combine(store, "files", f.Path))));
        Assert.Equal(2, item.Images.Count);
        Assert.Equal(120 + 1 % 5 * 10, item.Images.First(i => i.Url.EndsWith("1.png")).Width);
    }

    [Fact]
    public async Task Jobdir_pause_and_resume_continues_where_it_stopped()
    {
        var jobDir = TestSettings.TempDir();
        void Configure(Settings s)
        {
            TestSettings.Quiet(s);
            s.Set(SettingKeys.JobDir, jobDir);
            s.Set(SettingKeys.ConcurrentRequests, 1);
        }

        var (first, firstItems) = await Scrapy.CrawlAndCollectAsync(new GraphSpider(Base), s => { Configure(s); s.Set(SettingKeys.CloseSpiderItemCount, 40); },
            TestContext.Current.CancellationToken);
        Assert.Equal("closespider_itemcount", first.FinishReason);
        Assert.True(File.Exists(Path.Combine(jobDir, "requests.queue")));

        // The resumed run must not start over: start URLs are dont_filter, but everything already
        // seen is skipped and the saved queue is picked up.
        var (second, secondItems) = await Scrapy.CrawlAndCollectAsync(new GraphSpider(Base), Configure, TestContext.Current.CancellationToken);
        Assert.Equal("finished", second.FinishReason);
        var all = firstItems.Concat(secondItems).Select(i => (string)((dynamic)i).Node).Distinct().Count();
        Assert.Equal(200, all);
        Assert.True(secondItems.Count < 200);
        Assert.True((long)second.Stats["scheduler/restored"]! > 0);
    }

    [Fact]
    public async Task Http_cache_replays_responses_without_network()
    {
        var dir = TestSettings.TempDir();
        void Configure(Settings s)
        {
            TestSettings.Quiet(s);
            s.Set(SettingKeys.HttpCacheEnabled, true);
            s.Set(SettingKeys.HttpCacheDir, Path.Combine(dir, "cache"));
        }
        var before = sandbox.Site.RequestCount;
        var first = await Scrapy.CrawlAsync(new QuotesSpider(Base), Configure, TestContext.Current.CancellationToken);
        var afterFirst = sandbox.Site.RequestCount;
        var second = await Scrapy.CrawlAsync(new QuotesSpider(Base), Configure, TestContext.Current.CancellationToken);
        Assert.True(afterFirst - before >= SandboxData.QuotePages);
        Assert.Equal((long)SandboxData.QuotePages, second.Stats["httpcache/hit"]);
        Assert.Equal(first.ItemCount, second.ItemCount);
    }

    [Fact]
    public async Task Proxy_rotation_benches_banned_proxies()
    {
        await using var good = await SandboxSite.StartAsync(new SandboxOptions { Name = "good" });
        await using var bad = await SandboxSite.StartAsync(new SandboxOptions { Name = "bad", BanProxyAfter = 1 });
        var spider = new ListSpider(Enumerable.Range(1, 12).Select(i => sandbox.Url($"/ip?i={i}")), r =>
            [new { Proxy = r.Headers.Get("X-Sandbox-Proxy"), r.Status }]);
        var (result, items) = await Scrapy.CrawlAndCollectAsync(spider, s =>
        {
            TestSettings.Quiet(s);
            s.Set(SettingKeys.ProxyList, new List<string> { good.ProxyUrl, bad.ProxyUrl });
            s.DownloaderMiddlewares.Add<ProxyRotationMiddleware>(740);
            s.Set(SettingKeys.ConcurrentRequests, 1);
        }, TestContext.Current.CancellationToken);
        Assert.Equal(12, items.Count);
        Assert.All(items, i => Assert.Equal(200, (int)((dynamic)i).Status));
        Assert.True((long)result.Stats["proxies/banned"]! >= 1);
        Assert.True(items.Count(i => (string?)((dynamic)i).Proxy == "good") >= 11);
    }

    [Fact]
    public async Task AutoThrottle_raises_delay_for_slow_server()
    {
        await using var slow = await SandboxSite.StartAsync(new SandboxOptions { LatencyMs = 120 });
        var crawler = new Crawler(new ListSpider(Enumerable.Range(1, 6).Select(i => slow.Url($"/quotes/page/{i % 5 + 1}/?x={i}"))), BuildSettings(s =>
        {
            s.Set(SettingKeys.AutoThrottleEnabled, true);
            s.Set(SettingKeys.AutoThrottleStartDelay, 0.01);
            s.Set(SettingKeys.AutoThrottleTargetConcurrency, 1.0);
        }));
        await crawler.CrawlAsync(cancellationToken: TestContext.Current.CancellationToken);
        var throttle = crawler.Extensions.OfType<AutoThrottle>().Single();
        Assert.NotEmpty(throttle.History);
        Assert.True(throttle.History[^1].Delay >= 0.1, $"delay {throttle.History[^1].Delay}");
    }

    private sealed class XmlSpider(string baseUrl) : XmlFeedSpider
    {
        public override IReadOnlyList<string> StartUrls => [baseUrl + "/feeds/products.xml"];
        protected override IEnumerable<object> ParseNode(Response response, Selector node)
        {
            yield return new { Id = node.Xpath("//id/text()").Get(), Price = node.Xpath("//price/text()").Get() };
        }
    }

    private sealed class CsvSpider(string baseUrl) : CsvFeedSpider
    {
        public override IReadOnlyList<string> StartUrls => [baseUrl + "/feeds/products.csv"];
        protected override IEnumerable<object> ParseRow(Response response, IReadOnlyDictionary<string, string> row)
        {
            yield return new { Title = row["title"], Price = decimal.Parse(row["price"], System.Globalization.CultureInfo.InvariantCulture) };
        }
    }

    [Fact]
    public async Task Xml_and_csv_feed_spiders_iterate_rows()
    {
        var (_, xmlItems) = await Scrapy.CrawlAndCollectAsync(new XmlSpider(Base), TestSettings.Quiet, TestContext.Current.CancellationToken);
        Assert.Equal(SandboxData.Books.Count, xmlItems.Count);
        Assert.Equal("1", (string)((dynamic)xmlItems[0]).Id);
        var (_, csvItems) = await Scrapy.CrawlAndCollectAsync(new CsvSpider(Base), TestSettings.Quiet, TestContext.Current.CancellationToken);
        Assert.Equal(SandboxData.Books.Count, csvItems.Count);
    }

    [Fact]
    public async Task Signals_report_lifecycle_in_order()
    {
        var events = new List<string>();
        var crawler = new Crawler(new QuotesSpider(Base), BuildSettings(s => s.Set(SettingKeys.CloseSpiderPageCount, 1)));
        crawler.Signals.Connect(Signals.SpiderOpened, _ => events.Add("opened"));
        crawler.Signals.Connect(Signals.ResponseReceived, _ => { lock (events) events.Add("response"); });
        crawler.Signals.Connect(Signals.ItemScraped, _ => { lock (events) events.Add("item"); });
        crawler.Signals.Connect(Signals.SpiderClosed, e => events.Add("closed:" + e.Reason));
        await crawler.CrawlAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("opened", events[0]);
        Assert.Equal("closed:closespider_pagecount", events[^1]);
        Assert.Contains("item", events);
    }

    [Fact]
    public async Task Cancellation_token_shuts_down_gracefully()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var spider = new ListSpider(Enumerable.Range(0, 50).Select(i => sandbox.Url($"/slow?ms=100&i={i}")));
        var task = Scrapy.CrawlAsync(spider, s => { TestSettings.Quiet(s); s.Set(SettingKeys.ConcurrentRequests, 2); }, cts.Token);
        await Task.Delay(300, TestContext.Current.CancellationToken);
        cts.Cancel();
        var result = await task;
        Assert.Equal("shutdown", result.FinishReason);
        Assert.True(spider.Seen.Count < 50);
    }

    [Fact]
    public async Task Cookies_middleware_keeps_separate_jars()
    {
        var spider = new ListSpider([sandbox.Url("/cookies/set?a=1")], r =>
        {
            var seen = ((TextResponse)r).Json()!.AsObject().Select(kv => kv.Key).ToList();
            var jar = r.Request.Meta.GetValueOrDefault(MetaKeys.CookieJar);
            var output = new List<object> { new { Jar = jar?.ToString() ?? "default", Cookies = string.Join(",", seen) } };
            if (jar is null) output.Add(new Request(sandbox.Url("/cookies")) { DontFilter = true, Meta = { [MetaKeys.CookieJar] = "other" } });
            return output;
        });
        var (_, items) = await Scrapy.CrawlAndCollectAsync(spider, TestSettings.Quiet, TestContext.Current.CancellationToken);
        Assert.Equal("a", items.Select(i => (dynamic)i).First(i => i.Jar == "default").Cookies);
        Assert.Equal("", items.Select(i => (dynamic)i).First(i => i.Jar == "other").Cookies);
    }

    [Fact]
    public async Task Sqlite_pipeline_persists_items()
    {
        var db = Path.Combine(TestSettings.TempDir(), "quotes.db");
        var pipeline = new AdoNetPipeline(() => new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={db}"), "quotes")
        {
            CreateTableSql = "CREATE TABLE IF NOT EXISTS quotes (Text TEXT, Author TEXT, Tags TEXT)",
            BatchSize = 7,
        };
        await Scrapy.CrawlAsync(new QuotesSpider(Base), s => { TestSettings.Quiet(s); s.ItemPipelines.Add(pipeline, 500); }, TestContext.Current.CancellationToken);
        Assert.Equal(SandboxData.Quotes.Count, pipeline.Written);
        using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={db}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM quotes WHERE Tags LIKE '[%'";
        Assert.Equal((long)SandboxData.Quotes.Count, (long)cmd.ExecuteScalar()!);
    }

    private static Settings BuildSettings(Action<Settings> configure)
    {
        var s = new Settings();
        TestSettings.Quiet(s);
        configure(s);
        return s;
    }
}
