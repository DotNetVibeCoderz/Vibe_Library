using System.Text;
using ScrapyNet.Exporters;
using ScrapyNet.Loaders;
using Xunit;

namespace ScrapyNet.Tests;

public sealed class Book
{
    public string? Title { get; set; }
    public decimal Price { get; set; }
    public List<string> Tags { get; set; } = [];
    public int Rating { get; set; }
    public string? Url { get; set; }
}

public sealed class ProductItem() : Item("name", "price", new Field("url") { Serializer = v => v?.ToString()?.ToUpperInvariant() });

public class ComponentTests
{
    [Fact]
    public void Request_with_expression_copies_meta_and_headers()
    {
        var original = new Request("https://example.com/a") { Meta = { ["k"] = 1 } };
        original.Headers.Set("X", "1");
        var copy = original with { Priority = 5 };
        copy.Meta["k"] = 2;
        copy.Headers.Set("X", "2");
        Assert.Equal(1, original.Meta["k"]);
        Assert.Equal("1", original.Headers["X"]);
        Assert.Equal(5, copy.Priority);
        Assert.NotEqual(original, copy);
    }

    [Fact]
    public void Request_rejects_relative_urls()
    {
        Assert.Throws<ArgumentException>(() => new Request("/relative"));
        Assert.Throws<ArgumentException>(() => new Request("example.com/page"));
    }

    [Fact]
    public void Fingerprint_ignores_query_order_and_fragment()
    {
        var fp = new RequestFingerprinter();
        var a = fp.Fingerprint(new Request("http://example.com/p?b=2&a=1#top"));
        var b = fp.Fingerprint(new Request("http://EXAMPLE.com/p?a=1&b=2"));
        var c = fp.Fingerprint(new Request("http://example.com/p?a=1&b=3"));
        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.True(RequestFingerprint.TryParse(a.ToHex(), out var parsed));
        Assert.Equal(a, parsed);
    }

    [Fact]
    public void Url_utils_join_and_domain_match()
    {
        Assert.Equal("https://a.com/x/y", UrlUtils.Join("https://a.com/x/z", "y"));
        Assert.Equal("https://a.com/root", UrlUtils.Join("https://a.com/x/z", "/root"));
        Assert.Equal("https://b.com/", UrlUtils.Join("https://a.com/x", "//b.com/"));
        Assert.True(UrlUtils.IsFromAnyDomain("https://shop.example.com/a", ["example.com"]));
        Assert.False(UrlUtils.IsFromAnyDomain("https://badexample.com/a", ["example.com"]));
        Assert.True(UrlUtils.HasAnyExtension("https://a.com/file.PDF", UrlUtils.IgnoredExtensions));
    }

    [Fact]
    public void Typed_item_loader_applies_processors_and_converts_types()
    {
        var response = new HtmlResponse("https://books.example/book/1", """
            <h1> The  Silent
               Harbor </h1><p class="price">£51.77</p><a class="tag">travel</a><a class="tag">sea</a>
            <p class="star-rating Four"></p><a class="self" href="/book/1">self</a>
            """);
        var loader = new ItemLoader<Book>(response);
        loader.AddCss(b => b.Title, "h1::text", Transforms.Clean);
        loader.AddCss(b => b.Price, "p.price::text", Transforms.Price);
        loader.AddCss(b => b.Tags, "a.tag::text");
        loader.AddCss(b => b.Rating, "p.star-rating::attr(class)", Transforms.Str(s => s.Contains("Four") ? 4 : 0));
        loader.Field(b => b.Url).Input(new MapCompose(Transforms.UrlJoin));
        loader.AddCss(b => b.Url, "a.self::attr(href)");
        var book = loader.LoadItem();
        Assert.Equal("The Silent Harbor", book.Title);
        Assert.Equal(51.77m, book.Price);
        Assert.Equal(["travel", "sea"], book.Tags);
        Assert.Equal(4, book.Rating);
        Assert.Equal("https://books.example/book/1", book.Url);
    }

    [Fact]
    public void Dictionary_loader_with_nested_loader_and_output_processors()
    {
        var sel = Selector.FromHtml("<div class='p'><span class='n'>A</span><span class='n'>B</span></div><footer>x</footer>");
        var loader = new ItemLoader(sel) { DefaultOutputProcessor = new Join("|") };
        loader.NestedCss("div.p").AddCss("names", "span.n::text");
        loader.AddValue("static", "v");
        var item = (Dictionary<string, object?>)loader.LoadItem();
        Assert.Equal("A|B", item["names"]);
        Assert.Equal("v", item["static"]);
    }

    [Theory]
    [InlineData("£51.77", 51.77)]
    [InlineData("$1,299.00", 1299.00)]
    [InlineData("Rp 1.250.000", 1250000)]
    [InlineData("12,50 €", 12.50)]
    [InlineData("-3.5", -3.5)]
    public void Price_transform_handles_both_conventions(string input, double expected) =>
        Assert.Equal((decimal)expected, Transforms.Price(input));

    [Fact]
    public void Declared_item_rejects_unknown_fields()
    {
        var item = new ProductItem { ["name"] = "Book" };
        Assert.Throws<KeyNotFoundException>(() => item["nmae"] = "typo");
        Assert.Equal("Book", ItemAdapter.For(item).Get("name"));
    }

    [Fact]
    public void Item_adapter_matches_snake_case_to_properties()
    {
        var book = new Book();
        var adapter = ItemAdapter.For(book);
        Assert.Equal("Title", adapter.ResolveFieldName("title"));
        adapter.Set("price", "12.5");
        Assert.Equal(12.5m, book.Price);
        Assert.True(ItemAdapter.For(new { A = 1 }).HasField("a"));
    }

    [Fact]
    public void Settings_priorities_and_components()
    {
        var s = new Settings();
        Assert.Equal(16, s.GetInt(SettingKeys.ConcurrentRequests));
        s.Set(SettingKeys.ConcurrentRequests, 4, SettingPriority.Cmdline);
        s.Set(SettingKeys.ConcurrentRequests, 8, SettingPriority.Project);
        Assert.Equal(4, s.GetInt(SettingKeys.ConcurrentRequests));
        Assert.True(s.DownloaderMiddlewares.IsEnabled(typeof(DownloaderMiddlewares.RetryMiddleware)));
        s.DownloaderMiddlewares.Disable<DownloaderMiddlewares.RetryMiddleware>();
        Assert.False(s.DownloaderMiddlewares.IsEnabled(typeof(DownloaderMiddlewares.RetryMiddleware)));
        s.Set(SettingKeys.DownloaderMiddlewares, new Dictionary<string, object?> { ["RetryMiddleware"] = 551 });
        Assert.Equal(551, s.DownloaderMiddlewares.GetOrder(typeof(DownloaderMiddlewares.RetryMiddleware)));
        s.Freeze();
        Assert.Throws<InvalidOperationException>(() => s.Set("X", 1));
        Assert.Equal(["a", "b"], new Settings { ["L"] = "a, b" }.GetList("L"));
    }

    [Fact]
    public void Settings_load_json_with_feeds_and_components()
    {
        var path = Path.Combine(TestSettings.TempDir(), "scrapy.json");
        File.WriteAllText(path, """
            {
              // comments are allowed
              "DOWNLOAD_DELAY": 0.5,
              "ITEM_PIPELINES": { "ScrapyNet.Pipelines.DuplicatesPipeline": 300 },
              "FEEDS": { "out.csv": { "format": "csv", "overwrite": true } }
            }
            """);
        var s = new Settings().LoadJsonFile(path);
        Assert.Equal(0.5, s.GetDouble(SettingKeys.DownloadDelay));
        Assert.Equal(300, s.ItemPipelines.GetOrder(typeof(Pipelines.DuplicatesPipeline)));
        var feed = Assert.Single(s.Feeds);
        Assert.Equal("csv", feed.Value.Format);
        Assert.True(feed.Value.Overwrite);
    }

    [Fact]
    public void Robots_txt_longest_match_and_wildcards()
    {
        var robots = RobotsTxt.Parse("""
            User-agent: *
            Disallow: /private/
            Allow: /private/public
            Disallow: /*.pdf$
            Crawl-delay: 2

            User-agent: BadBot
            Disallow: /

            Sitemap: https://x.com/sitemap.xml
            """);
        Assert.True(robots.CanFetch("https://x.com/", "ScrapyNet"));
        Assert.False(robots.CanFetch("https://x.com/private/a", "ScrapyNet"));
        Assert.True(robots.CanFetch("https://x.com/private/public.html", "ScrapyNet"));
        Assert.False(robots.CanFetch("https://x.com/docs/a.pdf", "ScrapyNet"));
        Assert.True(robots.CanFetch("https://x.com/docs/a.pdf?x", "ScrapyNet"));
        Assert.False(robots.CanFetch("https://x.com/", "BadBot/1.0"));
        Assert.Equal(2, robots.CrawlDelay("ScrapyNet"));
        Assert.Equal(["https://x.com/sitemap.xml"], robots.Sitemaps);
    }

    [Fact]
    public void Sitemap_parsing_handles_index_and_alternates()
    {
        var urlset = Sitemap.Parse(Encoding.UTF8.GetBytes("""
            <urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9" xmlns:xhtml="http://www.w3.org/1999/xhtml">
              <url><loc>https://x.com/a</loc><lastmod>2026-01-02</lastmod><priority>0.8</priority>
                <xhtml:link rel="alternate" hreflang="id" href="https://x.com/id/a"/></url>
              <url><loc>https://x.com/b</loc></url>
            </urlset>
            """));
        Assert.False(urlset.IsIndex);
        Assert.Equal(2, urlset.Entries.Count);
        Assert.Equal(0.8, urlset.Entries[0].Priority);
        Assert.Equal(["https://x.com/id/a"], urlset.Entries[0].Alternates);
        var index = Sitemap.Parse(Encoding.UTF8.GetBytes("<sitemapindex><sitemap><loc>https://x.com/s1.xml</loc></sitemap></sitemapindex>"));
        Assert.True(index.IsIndex);
    }

    [Fact]
    public void Csv_parser_handles_quotes_and_newlines()
    {
        var rows = CsvParser.Parse(new StringReader("a,b\n\"x, y\",\"line1\nline2\"\n\"say \"\"hi\"\"\",z")).ToList();
        Assert.Equal(3, rows.Count);
        Assert.Equal(["x, y", "line1\nline2"], rows[1]);
        Assert.Equal("say \"hi\"", rows[2][0]);
    }

    [Theory]
    [InlineData("json")]
    [InlineData("jsonlines")]
    [InlineData("csv")]
    [InlineData("xml")]
    public void Exporters_write_every_format(string format)
    {
        using var ms = new MemoryStream();
        var exporter = ItemExporter.Create(format, ms, new FeedOptions());
        exporter.StartExporting();
        exporter.ExportItem(new Book { Title = "A, \"quoted\"", Price = 1.5m, Tags = ["x", "y"] });
        exporter.ExportItem(new Dictionary<string, object?> { ["Title"] = "B", ["Price"] = 2 });
        exporter.FinishExporting();
        var text = Encoding.UTF8.GetString(ms.ToArray());
        switch (format)
        {
            case "json":
                var doc = System.Text.Json.JsonDocument.Parse(text);
                Assert.Equal(2, doc.RootElement.GetArrayLength());
                Assert.Equal("A, \"quoted\"", doc.RootElement[0].GetProperty("Title").GetString());
                break;
            case "jsonlines":
                Assert.Equal(2, text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
                break;
            case "csv":
                Assert.StartsWith("Title,Price,Tags,Rating,Url\r\n\"A, \"\"quoted\"\"\",1.5,\"x,y\",0,", text);
                break;
            case "xml":
                var xml = System.Xml.Linq.XDocument.Parse(text);
                Assert.Equal(2, xml.Root!.Elements("item").Count());
                break;
        }
    }

    [Fact]
    public void Field_serializer_is_applied_on_export()
    {
        using var ms = new MemoryStream();
        var exporter = new JsonLinesItemExporter(ms);
        exporter.ExportItem(new ProductItem { ["name"] = "n", ["url"] = "http://a" });
        Assert.Contains("\"url\":\"HTTP://A\"", Encoding.UTF8.GetString(ms.ToArray()));
    }

    [Fact]
    public void Form_request_from_response_keeps_hidden_fields()
    {
        var response = new HtmlResponse("https://x.com/login", """
            <form action="/do-login" method="post"><input type="hidden" name="csrf" value="t0k">
            <input name="user"><input type="checkbox" name="remember" value="yes" checked>
            <select name="lang"><option value="en">EN</option><option value="id" selected>ID</option></select>
            <input type="submit" name="go" value="Login"></form>
            """);
        var request = FormRequest.FromResponse(response, [new("user", "ada")]);
        Assert.Equal("POST", request.Method);
        Assert.Equal("https://x.com/do-login", request.Url);
        var body = Encoding.UTF8.GetString(request.Body);
        Assert.Equal("csrf=t0k&remember=yes&lang=id&user=ada&go=Login", body);
        Assert.Equal("application/x-www-form-urlencoded", request.Headers["Content-Type"]);
    }

    [Fact]
    public void Link_extractor_filters()
    {
        var response = new HtmlResponse("https://shop.com/", """
            <a href="/catalogue/a.html">A</a> <a href="/catalogue/a.html">dup</a>
            <a href="/category/x">cat</a> <a href="https://other.com/p">other</a>
            <a href="/files/doc.pdf">pdf</a> <a href="mailto:x@y.z">mail</a> <a href="/n" rel="nofollow">n</a>
            <div class="menu"><a href="/menu/1">menu</a></div>
            """);
        var links = new LinkExtractor { AllowDomains = ["shop.com"], Deny = ["/category/"] }.ExtractLinks(response);
        Assert.Equal(["https://shop.com/catalogue/a.html", "https://shop.com/n", "https://shop.com/menu/1"], links.Select(l => l.Url));
        Assert.True(links[1].NoFollow);
        var restricted = new LinkExtractor { RestrictCss = ["div.menu"] }.ExtractLinks(response);
        Assert.Equal("menu", Assert.Single(restricted).Text);
    }

    [Fact]
    public void Encoding_detection_uses_meta_charset()
    {
        var body = Encoding.Latin1.GetBytes("<html><head><meta charset=\"iso-8859-1\"></head><body>café</body></html>");
        var response = new HtmlResponse("https://x.com/", body: body);
        Assert.Contains("café", response.Text);
    }
}
