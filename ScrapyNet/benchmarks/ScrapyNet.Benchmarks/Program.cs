using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using ScrapyNet;
using ScrapyNet.AI;
using ScrapyNet.Exporters;
using ScrapyNet.Sandbox;

// dotnet run -c Release --project benchmarks/ScrapyNet.Benchmarks -- --filter '*'
// dotnet run -c Release --project benchmarks/ScrapyNet.Benchmarks -- crawl      # end-to-end pages/min
if (args is ["crawl", ..])
{
    await CrawlThroughput.RunAsync();
    return;
}
BenchmarkSwitcher.FromAssembly(typeof(SelectorBenchmarks).Assembly).Run(args);

/// <summary>Parsing and querying a realistic 20-product listing page.</summary>
[MemoryDiagnoser]
public class SelectorBenchmarks
{
    private string _html = "";

    [GlobalSetup]
    public async Task Setup()
    {
        await using var site = await SandboxSite.StartAsync();
        using var http = new HttpClient();
        _html = await http.GetStringAsync(site.Url("/books/"));
    }

    [Benchmark(Description = "parse + 3 CSS queries")]
    public int Css()
    {
        var doc = new HtmlResponse("https://books.example/", _html);
        return doc.Css("article.product_pod h3 a::attr(title)").Count
             + doc.Css("p.price_color::text").Count
             + doc.Css("p.star-rating::attr(class)").Count;
    }

    [Benchmark(Description = "parse + 3 XPath queries")]
    public int Xpath()
    {
        var doc = new HtmlResponse("https://books.example/", _html);
        return doc.Xpath("//article[@class='product_pod']//h3/a/@title").Count
             + doc.Xpath("//p[@class='price_color']/text()").Count
             + doc.Xpath("//p[has-class('star-rating')]/@class").Count;
    }

    [Benchmark(Description = "link extraction")]
    public int Links() => new LinkExtractor().ExtractLinks(new HtmlResponse("https://books.example/", _html)).Count;
}

/// <summary>Per-request and per-item hot paths.</summary>
[MemoryDiagnoser]
public class HotPathBenchmarks
{
    private readonly Request[] _requests = Enumerable.Range(0, 1000).Select(i => new Request($"https://shop.example/p?id={i}&sort=asc&page={i % 7}")).ToArray();
    private readonly object[] _items = Enumerable.Range(0, 1000).Select(i => (object)new { Id = i, Title = $"Book {i}", Price = 12.5m + i, Tags = new[] { "a", "b" } }).ToArray();
    private readonly RequestFingerprinter _fingerprinter = new();

    [Benchmark(Description = "fingerprint 1,000 requests")]
    public int Fingerprints()
    {
        var set = new HashSet<RequestFingerprint>();
        foreach (var r in _requests)
        {
            r.CachedFingerprint = null;
            set.Add(_fingerprinter.Fingerprint(r));
        }
        return set.Count;
    }

    [Benchmark(Description = "export 1,000 items as JSON Lines")]
    public long ExportJsonLines()
    {
        using var ms = new MemoryStream();
        var exporter = new JsonLinesItemExporter(ms);
        foreach (var item in _items) exporter.ExportItem(item);
        return ms.Length;
    }

    [Benchmark(Description = "export 1,000 items as CSV")]
    public long ExportCsv()
    {
        using var ms = new MemoryStream();
        var exporter = new CsvItemExporter(ms);
        exporter.StartExporting();
        foreach (var item in _items) exporter.ExportItem(item);
        exporter.FinishExporting();
        return ms.Length;
    }
}

/// <summary>AI ingestion building blocks on an article-sized text.</summary>
[MemoryDiagnoser]
public class AiBenchmarks
{
    private readonly string _text = string.Join("\n\n", SandboxData.Articles.SelectMany(a => a.Paragraphs));
    private readonly HashingEmbeddingGenerator _embeddings = new(512);
    private readonly InMemoryVectorStore _store = new();
    private float[] _query = [];

    [GlobalSetup]
    public async Task Setup()
    {
        var records = Enumerable.Range(0, 10_000).Select(i => new VectorRecord($"r{i}", _embeddings.Embed($"document {i} about topic {i % 97} and crawling {i % 13}"), "", new Dictionary<string, string>())).ToList();
        await _store.UpsertAsync(records);
        _query = _embeddings.Embed("topic 42 crawling");
    }

    [Benchmark(Description = "SimHash of all articles")]
    public ulong SimHashAll() => SimHash.Compute(_text);

    [Benchmark(Description = "chunk all articles")]
    public int Chunk() => TextChunker.Chunk(_text).Count;

    [Benchmark(Description = "embed one paragraph")]
    public float[] Embed() => _embeddings.Embed(SandboxData.Articles[0].Paragraphs[1]);

    [Benchmark(Description = "top-5 search over 10,000 vectors")]
    public async Task<int> Search() => (await _store.SearchAsync(_query, 5)).Count;
}

/// <summary>End-to-end crawl throughput against the in-process sandbox (not a BenchmarkDotNet job).</summary>
public static class CrawlThroughput
{
    public static async Task RunAsync()
    {
        await using var site = await SandboxSite.StartAsync();
        foreach (var concurrency in new[] { 8, 16, 32, 64 })
        {
            var settings = new Settings();
            settings.Set(SettingKeys.LogLevel, "ERROR");
            settings.Set(SettingKeys.StatsDump, false);
            settings.Set(SettingKeys.ConcurrentRequests, concurrency);
            settings.Set(SettingKeys.ConcurrentRequestsPerDomain, concurrency);
            settings.Set(SettingKeys.CloseSpiderPageCount, 5000);
            var result = await Scrapy.CrawlAsync(new BenchSpider(site.Url("/graph/0")), settings);
            Console.WriteLine($"CONCURRENT_REQUESTS={concurrency,-3} {result.ResponseCount,6} pages in {result.Elapsed.TotalSeconds,6:0.00}s = {result.ResponseCount / result.Elapsed.TotalMinutes,9:N0} pages/min");
        }
    }

    private sealed class BenchSpider(string start) : Spider
    {
        public override IReadOnlyList<string> StartUrls => [start];

        public override async IAsyncEnumerable<object> Parse(Response response)
        {
            yield return new { Node = response.Css("h1::text").Get() };
            foreach (var r in response.FollowAllCss("ul.links a", Parse)) yield return r with { DontFilter = response.Request.Depth < 6 };
            await Task.CompletedTask;
        }
    }
}
