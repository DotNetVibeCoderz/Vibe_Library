// Notebook definitions. Markdown is bilingual: English first, Bahasa Indonesia in italics.

export const notebooks = [
  {
    file: "01-getting-started",
    title: "01 · Getting started with Scrapy.Net",
    intro: "Fetch a page, try selectors interactively, then write and run a first spider against quotes.toscrape.com.\n\n*Ambil halaman, coba selector secara interaktif, lalu tulis dan jalankan spider pertama.*",
    packages: ["core"],
    cells: [
      { md: "## Fetch one page\n\n`Fetcher` downloads through the full engine (headers, cookies, retries), like `scrapy shell`.\n\n*`Fetcher` mengunduh lewat mesin lengkap, seperti `scrapy shell`.*" },
      {
        code: `using ScrapyNet;
using ScrapyNet.Cmdline;

var response = await Fetcher.FetchAsync("https://quotes.toscrape.com/");
display($"{response.Status} {response.Url} ({response.Body.Length:N0} bytes)");`,
      },
      { md: "## Selectors\n\nCSS with Scrapy's `::text` and `::attr()`, XPath, and regular expressions.\n\n*CSS dengan `::text` dan `::attr()`, XPath, serta regex.*" },
      {
        code: `display(response.Css("title::text").Get());
display(response.Css("small.author::text").GetAll().Distinct());
display(response.Xpath("//div[@class='quote'][1]//a[@class='tag']/text()").GetAll());
display(response.Css("span.text::text").ReFirst(@"“(\\w+)"));`,
      },
      { md: "## A first spider\n\nCallbacks yield items (any object) and follow-up requests.\n\n*Callback menghasilkan item (objek apa pun) dan request lanjutan.*" },
      {
        decl: true,
        code: `public sealed class QuotesSpider : Spider
{
    public override string Name => "quotes";
    public override IReadOnlyList<string> StartUrls => ["https://quotes.toscrape.com/"];

    public override async IAsyncEnumerable<object> Parse(Response response)
    {
        foreach (var quote in response.Css("div.quote"))
            yield return new
            {
                Text = quote.Css("span.text::text").Get(),
                Author = quote.Css("small.author::text").Get(),
                Tags = quote.Css("a.tag::text").GetAll(),
            };

        if (response.Css("li.next a::attr(href)").Get() is { } next)
            yield return response.Follow(next, Parse);
        await Task.CompletedTask;
    }
}`,
      },
      {
        code: `var settings = new Settings();
settings.Set(SettingKeys.LogLevel, "WARNING");
settings.Set(SettingKeys.CloseSpiderPageCount, 2);   // be polite: two pages are enough here
settings.Set(SettingKeys.DownloadDelay, 0.5);

var (result, items) = await Scrapy.CrawlAndCollectAsync(new QuotesSpider(), settings);
display(result);
display(items.Take(3).Select(i => ItemAdapter.For(i).ToString()));`,
      },
    ],
  },
  {
    file: "02-selectors-and-loaders",
    title: "02 · Selectors, items and item loaders",
    intro: "Everything offline, on inline HTML: CSS/XPath details, typed items, and ItemLoader processors that clean data as it is extracted.\n\n*Semua offline dengan HTML inline: detail CSS/XPath, item bertipe, dan processor ItemLoader.*",
    packages: ["core"],
    cells: [
      {
        code: `using ScrapyNet;
using ScrapyNet.Loaders;

var html = """
    <div class="product" data-sku="A-1">
      <h2> Mechanical   Keyboard </h2>
      <span class="price">Rp 1.250.000</span>
      <ul class="specs"><li>Hot-swap</li><li>RGB</li><li>USB-C</li></ul>
      <a href="/p/a-1">details</a>
    </div>
    <div class="product" data-sku="B-2">
      <h2>Wireless Mouse</h2><span class="price">Rp 350.000</span>
      <ul class="specs"><li>Bluetooth</li></ul><a href="/p/b-2">details</a>
    </div>
    """;
var page = new HtmlResponse("https://shop.example/catalog", html);
display(page.Css("div.product::attr(data-sku)").GetAll());
display(page.Xpath("//div[has-class('product')]/h2/text()").GetAll());
display(page.Css("span.price::text").Re(@"[\\d.]+"));`,
      },
      { md: "## Typed items with a loader\n\nProcessors turn messy strings into clean typed values: `Clean` collapses whitespace, `Price` understands both `1,299.00` and `1.250.000`.\n\n*Processor mengubah teks berantakan menjadi nilai bertipe yang bersih.*" },
      {
        decl: true,
        code: `public sealed class Product
{
    public string? Sku { get; set; }
    public string? Name { get; set; }
    public decimal Price { get; set; }
    public List<string> Specs { get; set; } = [];
    public string? Url { get; set; }
}`,
      },
      {
        code: `var products = page.Css("div.product").Select(node =>
{
    var loader = new ItemLoader<Product>(node, response: page);
    loader.AddCss(p => p.Sku, "::attr(data-sku)");
    loader.AddCss(p => p.Name, "h2::text", Transforms.Clean);
    loader.AddCss(p => p.Price, "span.price::text", Transforms.Price);
    loader.AddCss(p => p.Specs, "ul.specs li::text");
    loader.Field(p => p.Url).Input(new MapCompose(Transforms.UrlJoin));
    loader.AddCss(p => p.Url, "a::attr(href)");
    return loader.LoadItem();
}).ToList();

display(products.Select(p => $"{p.Sku} | {p.Name} | {p.Price:N0} | {string.Join("/", p.Specs)} | {p.Url}"));`,
      },
    ],
  },
  {
    file: "03-crawlspider-and-exports",
    title: "03 · CrawlSpider rules and feed exports",
    intro: "Crawl books.toscrape.com with link-extraction rules and export the results to CSV and JSON Lines.\n\n*Crawl books.toscrape.com dengan aturan link extractor lalu ekspor ke CSV dan JSON Lines.*",
    packages: ["core"],
    cells: [
      {
        decl: true,
        code: `using ScrapyNet;

public sealed class BooksSpider : CrawlSpider
{
    public override string Name => "books";
    public override IReadOnlyList<string> AllowedDomains => ["books.toscrape.com"];
    public override IReadOnlyList<string> StartUrls => ["https://books.toscrape.com/"];

    protected override IReadOnlyList<Rule> Rules =>
    [
        new(new LinkExtractor { RestrictCss = ["article.product_pod h3"] }, ParseBook),
    ];

    private IEnumerable<object> ParseBook(Response r)
    {
        yield return new
        {
            Title = r.Css("div.product_main h1::text").Get(),
            Price = ScrapyNet.Loaders.Transforms.Price(r.Css("p.price_color::text").Get()),
            Stock = ScrapyNet.Loaders.Transforms.ToInt(r.Css("p.availability").GetText()),
            Upc = r.Xpath("//th[text()='UPC']/following-sibling::td/text()").Get(),
        };
    }
}`,
      },
      {
        code: `using ScrapyNet;

var output = Path.Combine(Path.GetTempPath(), "scrapynet-books");
Directory.CreateDirectory(output);
var settings = new Settings();
settings.Set(SettingKeys.LogLevel, "WARNING");
settings.Set(SettingKeys.CloseSpiderItemCount, 10);   // a small sample of the catalogue
settings.Set(SettingKeys.DownloadDelay, 0.25);
settings.Feeds.Add(Path.Combine(output, "books.csv"), new FeedOptions { Overwrite = true });
settings.Feeds.Add(Path.Combine(output, "books.jsonl"), new FeedOptions { Overwrite = true });

var result = await Scrapy.CrawlAsync(new BooksSpider(), settings);
display(result);
display(File.ReadLines(Path.Combine(output, "books.csv")).Take(4));`,
      },
    ],
  },
  {
    file: "04-sandbox-practice",
    title: "04 · Practice offline with the sandbox site",
    intro: "Start the bundled practice website in-process and try logins with CSRF tokens, authenticated JSON APIs, sitemaps and retries — no internet needed.\n\n*Jalankan situs latihan bawaan dan coba login dengan CSRF, API JSON berautentikasi, sitemap, dan retry — tanpa internet.*",
    packages: ["core", "sandbox"],
    cells: [
      {
        code: `using ScrapyNet;
using ScrapyNet.Sandbox;

var site = await SandboxSite.StartAsync();
display(site.BaseUrl);

var quiet = new Settings();
quiet.Set(SettingKeys.LogLevel, "WARNING");
quiet.Set(SettingKeys.StatsDump, false);`,
      },
      { md: "## Log in with a form\n\n`FormRequest.FromResponse` keeps hidden fields such as the CSRF token; the cookie middleware keeps the session.\n\n*`FormRequest.FromResponse` mempertahankan field tersembunyi seperti token CSRF; middleware cookie menjaga sesi.*" },
      {
        decl: true,
        code: `public sealed class LoginSpider(string url) : Spider
{
    public override IReadOnlyList<string> StartUrls => [url];

    public override async IAsyncEnumerable<object> Parse(Response response)
    {
        yield return FormRequest.FromResponse(response,
            [new("username", SandboxSite.Username), new("password", SandboxSite.Password)],
            callback: Account);
        await Task.CompletedTask;
    }

    private IEnumerable<object> Account(Response r)
    {
        yield return new { Welcome = r.Css("h1.welcome::text").Get(), Orders = r.Css("li.order").Count };
    }
}`,
      },
      {
        code: `var (_, account) = await Scrapy.CrawlAndCollectAsync(new LoginSpider(site.Url("/login")), quiet);
display(ItemAdapter.For(account[0]));`,
      },
      { md: "## Retries on a flaky endpoint\n\n`/flaky/{key}?fail=2` answers 503 twice before succeeding; the retry middleware handles it.\n\n*Endpoint ini gagal dua kali sebelum berhasil; middleware retry menanganinya.*" },
      {
        code: `var flaky = await ScrapyNet.Cmdline.Fetcher.FetchAsync(site.Url($"/flaky/{Guid.NewGuid():N}?fail=2"));
display($"{flaky.Status} after {flaky.Request.Meta.GetValueOrDefault(MetaKeys.RetryTimes)} retries");
await site.DisposeAsync();`,
      },
    ],
  },
  {
    file: "05-ai-rag",
    title: "05 · From crawl to knowledge base (RAG)",
    intro: "Crawl the sandbox newsroom, extract articles, drop near-duplicates, chunk, embed and search — then optionally let a language model answer with citations.\n\n*Crawl berita sandbox, ekstrak artikel, buang duplikat, chunk, embed, dan cari — lalu opsional jawab dengan LLM beserta sitasi.*",
    packages: ["core", "sandbox", "ai"],
    cells: [
      {
        code: `using ScrapyNet;
using ScrapyNet.AI;
using ScrapyNet.Sandbox;

var site = await SandboxSite.StartAsync();
var kb = new KnowledgeBase(new HashingEmbeddingGenerator(512), chunking: new ChunkingOptions { MaxWords = 120, OverlapWords = 20 });`,
      },
      {
        decl: true,
        code: `public sealed class NewsSpider(string url) : Spider
{
    public override IReadOnlyList<string> StartUrls => [url];

    public override async IAsyncEnumerable<object> Parse(Response response)
    {
        foreach (var r in response.FollowAllCss("article.teaser h2 a", Article)) yield return r;
        await Task.CompletedTask;
    }

    private IEnumerable<object> Article(Response r)
    {
        yield return new Dictionary<string, object?> { ["url"] = r.Url, ["html"] = r.Text };
    }
}`,
      },
      {
        code: `var settings = new Settings();
settings.Set(SettingKeys.LogLevel, "WARNING");
settings.ItemPipelines.Add(new RagIngestionPipeline(kb) { Summarizer = new ExtractiveSummarizer() }, 800);
await Scrapy.CrawlAsync(new NewsSpider(site.Url("/news/")), settings);
display($"{kb.Stats.Documents} documents, {kb.Stats.Chunks} chunks, {kb.Stats.NearDuplicates} near-duplicate skipped");`,
      },
      {
        code: `var hits = await kb.SearchAsync("how do crawlers avoid proxy bans?", top: 3);
display(hits.Select(h => $"{h.Score:0.000}  {h.Record.Metadata["title"]}"));`,
      },
      { md: "## Optional: answers from a language model\n\nAny OpenAI-compatible endpoint works (OpenAI, Azure OpenAI, DeepSeek, Ollama). Set the key in an environment variable — never in the notebook.\n\n*Endpoint apa pun yang kompatibel OpenAI bisa dipakai. Simpan kunci di environment variable, jangan di notebook.*" },
      {
        code: `if (Environment.GetEnvironmentVariable("SCRAPYNET_DEEPSEEK_KEY") is { } key)
{
    var withModel = new KnowledgeBase(kb.Embeddings, kb.Store, kb.Chunking)
    {
        LanguageModel = new OpenAiChatModel("deepseek-v4-flash", key, "https://api.deepseek.com/v1/"),
    };
    var answer = await withModel.AskAsync("What does proxy rotation do when a proxy gets banned?");
    display(answer.Answer);
}
await site.DisposeAsync();`,
      },
    ],
  },
  {
    file: "06-platform-jobs",
    title: "06 · Running crawlers as a platform",
    intro: "Define a spider as data, run it as a job, schedule it with cron, watch live metrics and receive alerts.\n\n*Definisikan spider sebagai data, jalankan sebagai job, jadwalkan dengan cron, pantau metrik, dan terima alert.*",
    packages: ["core", "sandbox", "platform"],
    cells: [
      {
        code: `using ScrapyNet;
using ScrapyNet.Platform;
using ScrapyNet.Sandbox;

var site = await SandboxSite.StartAsync();
var catalog = new SpiderCatalog();
catalog.Create(new SpiderDefinition
{
    Id = "books",
    Name = "books",
    Target = new TargetConfig { StartUrls = [site.Url("/books/")] },
    Rules = new ScrapingRules
    {
        DetailLinks = ["article.product_pod h3 a"],
        FollowLinks = ["li.next a"],
        Fields = new()
        {
            ["title"] = new FieldRule { Css = "h1::text", Processors = ["clean"] },
            ["price"] = new FieldRule { Css = "p.price_color::text", Processors = ["price"] },
        },
    },
});
display(catalog.List().Select(d => $"{d.Id} v{d.Version} declarative={d.IsDeclarative}"));`,
      },
      {
        code: `var jobs = new JobManager(catalog, new JobManagerOptions
{
    BaseSettings = () => { var s = new Settings(); s.Set(SettingKeys.LogLevel, "WARNING"); return s; },
});
var monitor = new CrawlMonitor(jobs);
var alerts = new List<Alert>();
new NotificationDispatcher(jobs).Route(new CallbackNotifier(a => { alerts.Add(a); return Task.CompletedTask; }), AlertSeverity.Info);

var job = await jobs.WaitAsync(jobs.RunNow("books").Id);
display($"{job.Status}: {job.ItemCount} items, {job.ResponseCount} responses in {job.Duration?.TotalSeconds:0.00}s");
display(alerts);`,
      },
      {
        code: `var cron = new CronExpression(Schedules.Weekly(DayOfWeek.Monday, 6, 30));
display(cron.GetOccurrences(DateTimeOffset.UtcNow, 3).Select(t => t.ToString("ddd yyyy-MM-dd HH:mm")));

var metrics = monitor.Get(job.Id)!;
display($"requests={metrics.Requests} responses={metrics.Responses} items={metrics.Items} errorRate={metrics.ErrorRate:P1}");
await site.DisposeAsync();`,
      },
    ],
  },
];
