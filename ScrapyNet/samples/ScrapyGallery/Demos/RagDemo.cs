using ScrapyGallery.Infrastructure;
using ScrapyNet;
using ScrapyNet.AI;

namespace ScrapyGallery.Demos;

public sealed class RagDemo : Demo
{
    public override DemoGroup Group => DemoGroup.Intelligence;
    public override string Title => "Crawl to knowledge base";
    public override string TitleId => "Dari crawl ke basis pengetahuan";
    public override string Summary => "Website → crawl → extract the article → drop near-duplicates → chunk → embed → vector store → answer. Ask anything about the newsroom; with SCRAPYNET_DEEPSEEK_KEY set, a language model writes the answer with citations.";
    public override string SummaryId => "Situs → crawl → ekstrak artikel → buang duplikat → chunk → embed → vector store → jawab. Tanyakan apa saja tentang ruang berita; dengan SCRAPYNET_DEEPSEEK_KEY, model bahasa menulis jawaban beserta sitasi.";
    public override IReadOnlyList<string> Concepts => ["RagIngestionPipeline", "ArticleExtractor", "TextChunker", "SimHash", "KnowledgeBase"];

    public override IReadOnlyList<DemoInput> Inputs =>
        [new("question", "Question", "Pertanyaan", "How do crawlers avoid getting their proxies banned?")];

    // <demo>
    public sealed class NewsSpider(string url) : Spider
    {
        public override IReadOnlyList<string> StartUrls => [url];

        public override async IAsyncEnumerable<object> Parse(Response response)
        {
            foreach (var request in response.FollowAllCss("article.teaser h2 a", Article)) yield return request;
            await Task.CompletedTask;
        }

        // Raw HTML in; the pipeline extracts the story and strips navigation, ads and sidebars.
        private IEnumerable<object> Article(Response r)
        {
            yield return new Dictionary<string, object?> { ["url"] = r.Url, ["html"] = r.Text };
        }
    }

    public static KnowledgeBase CreateKnowledgeBase()
    {
        var kb = new KnowledgeBase(new HashingEmbeddingGenerator(512), chunking: new ChunkingOptions { MaxWords = 110, OverlapWords = 20 })
        {
            LanguageModel = Environment.GetEnvironmentVariable("SCRAPYNET_DEEPSEEK_KEY") is { } key
                ? new OpenAiChatModel("deepseek-v4-flash", key, "https://api.deepseek.com/v1/")
                : null,
        };
        return kb;
    }
    // </demo>

    public override async Task RunAsync(DemoContext ctx)
    {
        var kb = CreateKnowledgeBase();
        ctx.Observer.CaptureCrawlItems = false;   // the Items tab shows the ranked answers instead
        var settings = ctx.NewSettings();
        settings.ItemPipelines.Add(new RagIngestionPipeline(kb) { Summarizer = new ExtractiveSummarizer() }, 800);
        await ctx.CrawlAsync(new NewsSpider(ctx.Site.Url("/news/")), settings);
        ctx.Print($"{kb.Stats.Documents} articles → {kb.Stats.Chunks} chunks · {kb.Stats.NearDuplicates} near-duplicate skipped · model: {(kb.LanguageModel is null ? "none (showing best passage)" : "deepseek-v4-flash")}");

        var question = ctx.Input("question");
        var answer = await kb.AskAsync(question, top: 4, cancellationToken: ctx.CancellationToken);
        var rank = 1;
        foreach (var hit in answer.Sources)
        {
            ctx.Observer.AddItem(new { Rank = rank, Score = Math.Round(hit.Score, 3), Title = hit.Record.Metadata.GetValueOrDefault("title"), Passage = hit.Record.Text });
            ctx.Observer.AddPoint("similarity", rank++, hit.Score);
        }
        ctx.Print($"\nQ: {question}\n\nA: {answer.Answer}");
    }
}
