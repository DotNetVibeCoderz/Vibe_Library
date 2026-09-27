using ScrapyGallery.Infrastructure;
using ScrapyNet;
using ScrapyNet.AI;

namespace ScrapyGallery.Demos;

public sealed class DedupDemo : Demo
{
    public override DemoGroup Group => DemoGroup.Intelligence;
    public override string Title => "Near-duplicates and summaries";
    public override string TitleId => "Hampir-duplikat dan ringkasan";
    public override string Summary => "Syndicated copies of the same story are common. SimHash fingerprints every article; pairs a few bits apart are the same story. Each article also gets a TextRank summary and a keyword category — no model needed.";
    public override string SummaryId => "Salinan sindikasi dari berita yang sama itu umum. SimHash membuat sidik jari tiap artikel; pasangan yang hanya berbeda beberapa bit adalah berita yang sama. Setiap artikel juga diringkas TextRank dan dikategorikan.";
    public override IReadOnlyList<string> Concepts => ["SimHash", "NearDuplicateIndex", "ExtractiveSummarizer", "KeywordCategorizer"];

    // <demo>
    public sealed class ArticleSpider(string url) : Spider
    {
        private readonly NearDuplicateIndex _index = new(maxDistance: 7);
        private readonly KeywordCategorizer _categorizer = new(new Dictionary<string, string[]>
        {
            ["AI"] = ["embedding", "embeddings", "vector", "model", "language", "retrieval"],
            ["Crawling"] = ["crawler", "crawlers", "proxy", "robots", "spider", "sitemaps"],
            ["Operations"] = ["monitoring", "alerts", "schedule", "cron", "statistics"],
        });

        public override IReadOnlyList<string> StartUrls => [url];

        public override async IAsyncEnumerable<object> Parse(Response response)
        {
            foreach (var request in response.FollowAllCss("article.teaser h2 a", Article)) yield return request;
            await Task.CompletedTask;
        }

        private async IAsyncEnumerable<object> Article(Response response)
        {
            var article = ArticleExtractor.Extract(response);
            var fingerprint = SimHash.Compute(article.Text);
            var duplicateOf = _index.AddOrFindDuplicate(response.Url, fingerprint);
            var (category, _) = await _categorizer.CategorizeAsync(article.Text);
            yield return new
            {
                article.Title,
                SimHash = fingerprint.ToString("x16"),
                DuplicateOf = duplicateOf is null ? "" : duplicateOf[(duplicateOf.LastIndexOf('/') + 1)..],
                Category = category,
                article.WordCount,
                Summary = await new ExtractiveSummarizer().SummarizeAsync(article.Text, 1),
            };
        }
    }
    // </demo>

    public override async Task RunAsync(DemoContext ctx)
    {
        var settings = ctx.NewSettings();
        settings.Set(SettingKeys.ConcurrentRequests, 1);   // keep article order stable
        var result = await ctx.CrawlAsync(new ArticleSpider(ctx.Site.Url("/news/")), settings);
        ctx.Print(result.ToString());
        ctx.Print("The syndicated copy shows which original it duplicates in the DuplicateOf column.");
    }
}
