using System.Net;
using System.Numerics.Tensors;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using ScrapyNet.AI;
using Xunit;

namespace ScrapyNet.Tests;

/// <summary>A tiny in-process stand-in for the Azure AI Search REST API (indexes, docs/index, docs/search, $count).</summary>
internal sealed class FakeAzureSearch : HttpMessageHandler
{
    public JsonObject? Index { get; private set; }
    public Dictionary<string, JsonObject> Docs { get; } = [];
    public List<string> Calls { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls.Add($"{request.Method} {request.RequestUri!.AbsolutePath}");
        Assert.Equal("secret", request.Headers.GetValues("api-key").Single());
        Assert.Contains("api-version=2024-07-01", request.RequestUri.Query);
        var path = request.RequestUri.AbsolutePath;
        var body = request.Content is null ? null : JsonNode.Parse(await request.Content.ReadAsStringAsync(cancellationToken));

        if (path == "/indexes/pages" && request.Method == HttpMethod.Get)
            return Index is null ? new HttpResponseMessage(HttpStatusCode.NotFound) : Ok(Index);
        if (path == "/indexes/pages" && request.Method == HttpMethod.Put)
        {
            Index = body!.AsObject();
            return new HttpResponseMessage(HttpStatusCode.Created);
        }
        if (Index is null) return new HttpResponseMessage(HttpStatusCode.NotFound);
        if (path == "/indexes/pages/docs/index")
        {
            foreach (var action in body!["value"]!.AsArray())
            {
                var key = action!["id"]!.GetValue<string>();
                Assert.Matches("^[A-Za-z0-9_=-]+$", key);
                if (action["@search.action"]!.GetValue<string>() == "delete") Docs.Remove(key);
                else Docs[key] = action.AsObject();
            }
            return Ok(new JsonObject { ["value"] = new JsonArray() });
        }
        if (path == "/indexes/pages/docs/$count")
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("﻿" + Docs.Count) };
        if (path == "/indexes/pages/docs/search")
        {
            var vq = body!["vectorQueries"]![0]!;
            var query = vq["vector"]!.AsArray().Select(v => v!.GetValue<float>()).ToArray();
            var hits = Docs.Values
                .Select(d => (Doc: d, Score: TensorPrimitives.CosineSimilarity(query, d["embedding"]!.AsArray().Select(v => v!.GetValue<float>()).ToArray())))
                .OrderByDescending(h => h.Score)
                .Take(vq["k"]!.GetValue<int>())
                .Select(h => (JsonNode)new JsonObject
                {
                    ["@search.score"] = h.Score,
                    ["recordId"] = h.Doc["recordId"]!.DeepClone(),
                    ["text"] = h.Doc["text"]!.DeepClone(),
                    ["metadata"] = h.Doc["metadata"]!.DeepClone(),
                });
            return Ok(new JsonObject { ["value"] = new JsonArray([.. hits]) });
        }
        return new HttpResponseMessage(HttpStatusCode.BadRequest);
    }

    private static HttpResponseMessage Ok(JsonNode node) =>
        new(HttpStatusCode.OK) { Content = new StringContent(node.ToJsonString(), Encoding.UTF8, "application/json") };
}

/// <summary>A Microsoft.Extensions.AI chat client that answers with what it was asked, for adapter tests.</summary>
internal sealed class EchoChatClient : IChatClient
{
    public List<ChatMessage> LastMessages { get; } = [];

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        LastMessages.Clear();
        LastMessages.AddRange(messages);
        var system = LastMessages.FirstOrDefault(m => m.Role == ChatRole.System)?.Text;
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, $"[{system}] {LastMessages[^1].Text}")));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}

public class VectorStoreTests
{
    private static readonly HashingEmbeddingGenerator Embeddings = new(256);

    private static async Task<List<VectorRecord>> RecordsAsync(params (string Id, string Text, string Topic)[] docs)
    {
        var vectors = await Embeddings.EmbedAsync([.. docs.Select(d => d.Text)]);
        return [.. docs.Select((d, i) => new VectorRecord(d.Id, vectors[i], d.Text, new Dictionary<string, string> { ["topic"] = d.Topic }))];
    }

    private static readonly (string, string, string)[] Corpus =
    [
        ("https://example.com/a?x=1", "Scrapy crawls websites with spiders and selectors", "crawling"),
        ("https://example.com/b", "Vector databases store embeddings for semantic search", "search"),
        ("https://example.com/c", "Spiders follow links and extract items with CSS selectors", "crawling"),
    ];

    [Fact]
    public async Task Azure_ai_search_store_creates_index_upserts_searches_counts_and_deletes()
    {
        var fake = new FakeAzureSearch();
        using var store = new AzureAiSearchVectorStore("https://demo.search.windows.net", "pages", 256, "secret", fake);
        await store.UpsertAsync(await RecordsAsync(Corpus), TestContext.Current.CancellationToken);

        Assert.Equal(256, fake.Index!["fields"]!.AsArray().Single(f => (string)f!["name"]! == "embedding")!["dimensions"]!.GetValue<int>());
        Assert.Equal(3, await store.CountAsync(TestContext.Current.CancellationToken));

        var query = (await Embeddings.EmbedAsync(["spiders extract items with selectors"], TestContext.Current.CancellationToken))[0];
        var hits = await store.SearchAsync(query, 2, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("https://example.com/c", hits[0].Record.Id);
        Assert.Equal("crawling", hits[0].Record.Metadata["topic"]);
        var filtered = await store.SearchAsync(query, 1, r => r.Metadata["topic"] == "search", TestContext.Current.CancellationToken);
        Assert.Equal("https://example.com/b", Assert.Single(filtered).Record.Id);

        await store.DeleteAsync(["https://example.com/a?x=1"], TestContext.Current.CancellationToken);
        Assert.Equal(2, await store.CountAsync(TestContext.Current.CancellationToken));
        Assert.Single(fake.Calls, c => c == "PUT /indexes/pages");
    }

    [Fact]
    public void Pgvector_literal_is_invariant_and_round_trips()
    {
        var literal = PgVectorStore.ToLiteral([0.1f, -2.5f, 1e-7f]);
        Assert.Equal("[0.1,-2.5,1E-07]", literal);
        Assert.Throws<ArgumentException>(() => new PgVectorStore(() => null!, "pages; drop table x", 3));
    }

    /// <summary>Runs against a real PostgreSQL with pgvector when SCRAPYNET_PGVECTOR is a connection string (CI sets one).</summary>
    [Fact]
    public async Task Pgvector_store_round_trip_against_postgres()
    {
        var connectionString = Environment.GetEnvironmentVariable("SCRAPYNET_PGVECTOR");
        Assert.SkipWhen(string.IsNullOrEmpty(connectionString), "Set SCRAPYNET_PGVECTOR to a PostgreSQL+pgvector connection string.");
        var table = "scrapynet_test_" + Guid.NewGuid().ToString("N")[..8];
        var store = new PgVectorStore(() => new Npgsql.NpgsqlConnection(connectionString), table, 256);
        var ct = TestContext.Current.CancellationToken;

        await store.UpsertAsync(await RecordsAsync(Corpus), ct);
        await store.UpsertAsync(await RecordsAsync(Corpus[0]), ct);
        Assert.Equal(3, await store.CountAsync(ct));

        var query = (await Embeddings.EmbedAsync(["spiders extract items with selectors"], ct))[0];
        var hits = await store.SearchAsync(query, 2, cancellationToken: ct);
        Assert.Equal("https://example.com/c", hits[0].Record.Id);
        Assert.InRange(hits[0].Score, 0.1f, 1.0001f);
        Assert.Equal("crawling", hits[0].Record.Metadata["topic"]);

        await store.DeleteAsync(["https://example.com/a?x=1", "https://example.com/b"], ct);
        Assert.Equal(1, await store.CountAsync(ct));
    }

    [Fact]
    public async Task Extensions_ai_adapters_round_trip_embeddings()
    {
        var meai = Embeddings.AsEmbeddingGenerator("hashing");
        var metadata = (EmbeddingGeneratorMetadata)meai.GetService(typeof(EmbeddingGeneratorMetadata))!;
        Assert.Equal(256, metadata.DefaultModelDimensions);

        var generated = await meai.GenerateAsync(["hello world"], cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal((await Embeddings.EmbedAsync(["hello world"], TestContext.Current.CancellationToken))[0], generated[0].Vector.ToArray());

        var back = meai.AsScrapyNetEmbeddings();
        Assert.Equal(256, back.Dimensions);
        // A whole RAG round trip on Microsoft.Extensions.AI components.
        var kb = new KnowledgeBase(back) { LanguageModel = new EchoChatClient().AsLanguageModel() };
        await kb.AddAsync(new IngestDocument("doc-1", "Scrapy.Net spiders follow links and extract items with CSS selectors.", "Spiders"), TestContext.Current.CancellationToken);
        await kb.AddAsync(new IngestDocument("doc-2", "Vector databases store embeddings for semantic search.", "Vectors"), TestContext.Current.CancellationToken);
        var answer = await kb.AskAsync("How do spiders extract items?", 1, TestContext.Current.CancellationToken);
        Assert.Contains("doc-1", answer.Sources[0].Record.Id);
        Assert.False(string.IsNullOrWhiteSpace(answer.Answer));
    }

    [Fact]
    public async Task Extensions_ai_chat_client_drives_scrapynet_summarizer_and_vice_versa()
    {
        var echo = new EchoChatClient();
        var model = echo.AsLanguageModel();
        var answer = await model.CompleteAsync("What is Scrapy?", "Be brief", TestContext.Current.CancellationToken);
        Assert.Equal("[Be brief] What is Scrapy?", answer);
        Assert.Equal([ChatRole.System, ChatRole.User], echo.LastMessages.Select(m => m.Role));

        var summary = await new LlmSummarizer(model).SummarizeAsync("Scrapy is a crawling framework. It is fast.", 1, TestContext.Current.CancellationToken);
        Assert.Contains("Scrapy is a crawling framework.", summary);

        // And the other way: Scrapy.Net's model behind IChatClient.
        IChatClient client = model.AsChatClient("echo");
        var response = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.System, "sys"), new ChatMessage(ChatRole.User, "hi"), new ChatMessage(ChatRole.Assistant, "hello"), new ChatMessage(ChatRole.User, "more")],
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("[sys] user: hi\nassistant: hello\nuser: more", response.Text.Replace("\r\n", "\n"));
        var streamed = new List<string>();
        await foreach (var update in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "x")], cancellationToken: TestContext.Current.CancellationToken))
            streamed.Add(update.Text);
        Assert.Equal(["[] x"], streamed);
    }
}
