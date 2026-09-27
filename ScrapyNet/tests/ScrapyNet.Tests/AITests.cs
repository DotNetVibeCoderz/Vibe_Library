using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using ScrapyNet.AI;
using ScrapyNet.Cmdline;
using ScrapyNet.Sandbox;
using Xunit;

namespace ScrapyNet.Tests;

public class AITests(SandboxFixture sandbox)
{
    private string ArticleUrl(int i) => sandbox.Url("/news/" + SandboxData.Articles[i].Slug);

    [Fact]
    public async Task Article_extractor_keeps_story_and_drops_boilerplate()
    {
        var response = await Fetcher.FetchAsync(ArticleUrl(2), cancellationToken: TestContext.Current.CancellationToken);
        var article = ArticleExtractor.Extract(response);
        Assert.Equal(SandboxData.Articles[2].Title, article.Title);
        Assert.Contains(SandboxData.Articles[2].Paragraphs[1], article.Text);
        Assert.DoesNotContain("Advertisement", article.Text);
        Assert.DoesNotContain("Trending", article.Text);
        Assert.DoesNotContain("Share on social media", article.Text);
        Assert.Equal(SandboxData.Articles[2].Author, article.Metadata.Author);
        Assert.Equal("NewsArticle", article.Metadata.Type);
        Assert.NotNull(article.Metadata.Published);
        Assert.True(article.WordCount > 100);
        // The response's own DOM must be untouched.
        Assert.NotEmpty(response.Css("script[type='application/ld+json']"));
    }

    [Fact]
    public void Chunker_respects_size_and_overlap()
    {
        var text = string.Join("\n\n", Enumerable.Range(1, 30).Select(i => $"Paragraph {i} talks about crawling. It has a second sentence with more words for the counter."));
        var chunks = TextChunker.Chunk(text, new ChunkingOptions { MaxWords = 60, OverlapWords = 10 });
        Assert.True(chunks.Count > 5);
        Assert.All(chunks, c => Assert.InRange(c.WordCount, 10, 60 + 20));
        var tail = string.Join(' ', chunks[0].Text.Split(' ')[^10..]);
        Assert.StartsWith(tail, chunks[1].Text);
        Assert.Contains("Paragraph 30", chunks[^1].Text);
    }

    [Fact]
    public void Markdown_chunks_carry_heading_path()
    {
        var md = "# Guide\nIntro text here that is long enough.\n## Install\nRun the installer on your machine now.\n### Windows\nUse the MSI package from the site.";
        var chunks = TextChunker.Chunk(md, new ChunkingOptions { Strategy = ChunkStrategy.Markdown, MaxWords = 50, MinWords = 1 });
        Assert.Contains(chunks, c => c.Section == "Guide › Install › Windows" && c.Text.Contains("MSI"));
    }

    [Fact]
    public void Simhash_finds_the_syndicated_copy_but_not_other_articles()
    {
        string Text(SandboxArticle a) => string.Join(" ", a.Paragraphs);
        var original = SimHash.Compute(Text(SandboxData.Articles[0]));
        var copy = SimHash.Compute(Text(SandboxData.Articles[^1]));
        var other = SimHash.Compute(Text(SandboxData.Articles[5]));
        Assert.True(SimHash.Distance(original, copy) < SimHash.Distance(original, other));

        var index = new NearDuplicateIndex(maxDistance: 10);
        Assert.Null(index.AddOrFindDuplicate("a", original));
        Assert.Equal("a", index.AddOrFindDuplicate("b", copy));
    }

    [Fact]
    public async Task Hashing_embeddings_rank_relevant_text_first()
    {
        var store = new InMemoryVectorStore();
        var gen = new HashingEmbeddingGenerator(256);
        string[] docs = ["Proxy rotation avoids bans when crawling", "Vector databases store embeddings for search", "Recipe for chocolate cake with butter"];
        var vectors = await gen.EmbedAsync(docs, TestContext.Current.CancellationToken);
        await store.UpsertAsync(docs.Select((d, i) => new VectorRecord($"d{i}", vectors[i], d, new Dictionary<string, string> { ["i"] = i.ToString() })).ToList(), TestContext.Current.CancellationToken);
        var query = (await gen.EmbedAsync(["which database stores embeddings?"], TestContext.Current.CancellationToken))[0];
        var hits = await store.SearchAsync(query, 2, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("d1", hits[0].Record.Id);
        Assert.True(hits[0].Score > hits[1].Score);

        var path = Path.Combine(TestSettings.TempDir(), "store.jsonl");
        await store.SaveAsync(path, TestContext.Current.CancellationToken);
        var loaded = await InMemoryVectorStore.LoadAsync(path, TestContext.Current.CancellationToken);
        Assert.Equal(3, await loaded.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal("d1", (await loaded.SearchAsync(query, 1, cancellationToken: TestContext.Current.CancellationToken))[0].Record.Id);
    }

    [Fact]
    public async Task Textrank_summary_returns_sentences_in_order()
    {
        var text = string.Join(" ", SandboxData.Articles[1].Paragraphs);
        var summary = await new ExtractiveSummarizer().SummarizeAsync(text, 2, TestContext.Current.CancellationToken);
        var sentences = TextNormalizer.SplitSentences(summary);
        Assert.Equal(2, sentences.Count);
        Assert.True(text.IndexOf(sentences[0], StringComparison.Ordinal) < text.IndexOf(sentences[1], StringComparison.Ordinal));
    }

    [Fact]
    public async Task Classifiers_pick_the_right_category()
    {
        var nb = new NaiveBayesCategorizer()
            .Train("embedding vector model retrieval llm", "ai")
            .Train("language model prompt answer rag", "ai")
            .Train("proxy robots rate limit crawler politeness", "crawling")
            .Train("download delay concurrency spider scheduler", "crawling");
        Assert.Equal("ai", (await nb.CategorizeAsync("store the embedding in a vector index for the llm", TestContext.Current.CancellationToken)).Category);
        Assert.Equal("crawling", (await nb.CategorizeAsync("the spider respects robots and download delay", TestContext.Current.CancellationToken)).Category);

        var kw = new KeywordCategorizer(new Dictionary<string, string[]> { ["sport"] = ["football", "goal"], ["tech"] = ["software", "cloud"] });
        Assert.Equal("tech", (await kw.CategorizeAsync("cloud software release", TestContext.Current.CancellationToken)).Category);
        Assert.Equal("other", (await kw.CategorizeAsync("nothing matches", TestContext.Current.CancellationToken)).Category);
    }

    [Fact]
    public async Task Incremental_recrawl_skips_unchanged_pages_with_etags()
    {
        var state = Path.Combine(TestSettings.TempDir(), "changes.json");
        var url = sandbox.Url("/news/live");
        void Configure(Settings s)
        {
            TestSettings.Quiet(s);
            s.Set("CHANGE_STORE_PATH", state);
            s.DownloaderMiddlewares.Add<IncrementalRecrawlMiddleware>(950);
        }
        async Task<(CrawlResult, List<object>)> Run() =>
            await Scrapy.CrawlAndCollectAsync(new CrawlTestsHelpers.PageSpider(url), Configure, TestContext.Current.CancellationToken);

        var (first, firstItems) = await Run();
        Assert.Single(firstItems);
        Assert.Equal(1L, first.Stats["change_detection/new"]);
        var (second, secondItems) = await Run();
        Assert.Empty(secondItems);
        Assert.Equal(1L, second.Stats["change_detection/unchanged"]);
        Assert.Contains(sandbox.Site.Requests, r => r.Path == "/news/live" && r.Header("If-None-Match") is not null);
        sandbox.Site.BumpLiveVersion();
        var (third, thirdItems) = await Run();
        Assert.Single(thirdItems);
        Assert.Equal(1L, third.Stats["change_detection/changed"]);
    }

    [Fact]
    public async Task Rag_pipeline_ingests_crawl_and_answers_from_sources()
    {
        var kb = new KnowledgeBase(new HashingEmbeddingGenerator(), chunking: new ChunkingOptions { MaxWords = 80, OverlapWords = 15 })
        {
            LanguageModel = new FakeModel(),
        };
        var pipeline = new RagIngestionPipeline(kb) { Summarizer = new ExtractiveSummarizer() };
        var (result, _) = await Scrapy.CrawlAndCollectAsync(new CrawlTestsHelpers.NewsSpider(sandbox.Url("/news/")), s =>
        {
            TestSettings.Quiet(s);
            s.ItemPipelines.Add(pipeline, 800);
        }, TestContext.Current.CancellationToken);
        Assert.Equal(SandboxData.Articles.Count, (int)(long)result.Stats["rag/documents_seen"]!);
        Assert.Equal(1L, result.Stats["rag/duplicates"]);
        Assert.Equal(SandboxData.Articles.Count - 1, kb.Stats.Documents);

        var hits = await kb.SearchAsync("rotating proxies without being banned", 3, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains("Proxy rotation", hits[0].Record.Metadata["title"]);
        Assert.False(string.IsNullOrEmpty(hits[0].Record.Metadata["summary"]));
        var answer = await kb.AskAsync("How do I avoid proxy bans?", cancellationToken: TestContext.Current.CancellationToken);
        Assert.StartsWith("ANSWER", answer.Answer);
        Assert.NotEmpty(answer.Sources);
    }

    private sealed class FakeModel : ILanguageModel
    {
        public Task<string> CompleteAsync(string prompt, string? system = null, CancellationToken cancellationToken = default) =>
            Task.FromResult("ANSWER based on " + (prompt.Contains("[1]") ? "sources" : "nothing"));
    }

    [Fact]
    public async Task OpenAi_compatible_clients_send_expected_requests()
    {
        var handler = new RecordingHandler(req => req.RequestUri!.AbsolutePath.EndsWith("embeddings")
            ? """{"data":[{"index":1,"embedding":[0,1]},{"index":0,"embedding":[1,0]}]}"""
            : """{"choices":[{"message":{"content":"hello"}}]}""");
        using var embed = new OpenAiEmbeddingGenerator("text-embedding-3-small", 2, "key", "http://llm.local/v1", handler);
        var vectors = await embed.EmbedAsync(["a", "b"], TestContext.Current.CancellationToken);
        Assert.Equal([1f, 0f], vectors[0]);
        Assert.Equal("Bearer key", handler.Requests[0].Authorization);
        Assert.Equal("text-embedding-3-small", (string)JsonNode.Parse(handler.Requests[0].Body)!["model"]!);

        using var chat = new OpenAiChatModel("gpt-x", null, "http://llm.local/v1", handler);
        Assert.Equal("hello", await chat.CompleteAsync("hi", "sys", TestContext.Current.CancellationToken));
        Assert.Equal(2, JsonNode.Parse(handler.Requests[1].Body)!["messages"]!.AsArray().Count);
    }

    [Fact]
    public async Task Qdrant_store_uses_rest_api()
    {
        var handler = new RecordingHandler(req => req.RequestUri!.AbsolutePath switch
        {
            var p when p.EndsWith("/search") => """{"result":[{"id":"x","score":0.9,"payload":{"_id":"doc#0","_text":"hello","title":"T"}}]}""",
            _ => """{"result":{"count":1}}""",
        });
        using var store = new QdrantVectorStore("http://qdrant.local:6333", "docs", 2, handler: handler);
        await store.UpsertAsync([new VectorRecord("doc#0", [1, 0], "hello", new Dictionary<string, string> { ["title"] = "T" })], TestContext.Current.CancellationToken);
        var hits = await store.SearchAsync([1, 0], 1, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("doc#0", hits[0].Record.Id);
        Assert.Equal("T", hits[0].Record.Metadata["title"]);
        Assert.Contains(handler.Requests, r => r.Method == "PUT" && r.Path.EndsWith("/points"));
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, string> respond) : HttpMessageHandler
    {
        public List<(string Method, string Path, string Body, string? Authorization)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.Method.Method, request.RequestUri!.AbsolutePath, body, request.Headers.Authorization?.ToString()));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(respond(request), Encoding.UTF8, "application/json") };
        }
    }
}

public static class CrawlTestsHelpers
{
    public sealed class PageSpider(string url) : Spider
    {
        public override IReadOnlyList<string> StartUrls => [url];

        public override async IAsyncEnumerable<object> Parse(Response response)
        {
            yield return new { response.Url, Text = response.Css(".article-body ::text").GetText() };
            await Task.CompletedTask;
        }
    }

    public sealed class NewsSpider(string url) : Spider
    {
        public override IReadOnlyList<string> StartUrls => [url];

        public override async IAsyncEnumerable<object> Parse(Response response)
        {
            foreach (var r in response.FollowAllCss("article.teaser h2 a", ParseArticle)) yield return r;
            await Task.CompletedTask;
        }

        private IEnumerable<object> ParseArticle(Response response)
        {
            yield return new Dictionary<string, object?>
            {
                ["url"] = response.Url,
                ["title"] = response.Css("h1.headline::text").Get(),
                ["html"] = response.Text,
            };
        }
    }
}
