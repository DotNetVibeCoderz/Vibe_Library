using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace ScrapyNet.AI;

/// <summary>A document to ingest: text plus metadata.</summary>
public sealed record IngestDocument(string Id, string Text, string? Title = null, string? Url = null)
{
    public Dictionary<string, string> Metadata { get; init; } = new(StringComparer.Ordinal);
}

/// <summary>Counters of an ingestion run.</summary>
public sealed class IngestionStats
{
    public int Documents;
    public int Chunks;
    public int ExactDuplicates;
    public int NearDuplicates;
    public int Empty;
}

/// <summary>
/// A searchable knowledge base: documents are normalized, deduplicated (exact and near), chunked,
/// embedded and stored; questions are answered from the most relevant chunks, with sources, and
/// optionally phrased by a language model.
/// </summary>
/// <example>
/// <code>
/// var kb = new KnowledgeBase(new HashingEmbeddingGenerator());
/// await kb.AddAsync(new IngestDocument("doc1", articleText, "Title", url));
/// var hits = await kb.SearchAsync("how does autothrottle work?");
/// </code>
/// </example>
public sealed class KnowledgeBase(IEmbeddingGenerator embeddings, IVectorStore? store = null, ChunkingOptions? chunking = null)
{
    private readonly NearDuplicateIndex _near = new();
    private readonly HashSet<string> _exact = [];

    public IVectorStore Store { get; } = store ?? new InMemoryVectorStore();

    public IEmbeddingGenerator Embeddings { get; } = embeddings;

    public ChunkingOptions Chunking { get; } = chunking ?? new ChunkingOptions();

    public IngestionStats Stats { get; } = new();

    /// <summary>Skip documents that are near-duplicates (SimHash) of one already ingested.</summary>
    public bool DeduplicateNear { get; init; } = true;

    /// <summary>Optional model for <see cref="AskAsync"/>.</summary>
    public ILanguageModel? LanguageModel { get; init; }

    /// <summary>Ingests one document. Returns the number of chunks stored (0 if skipped).</summary>
    public async Task<int> AddAsync(IngestDocument document, CancellationToken cancellationToken = default)
    {
        var text = TextNormalizer.Normalize(document.Text);
        if (TextNormalizer.CountWords(text) < 5)
        {
            Interlocked.Increment(ref Stats.Empty);
            return 0;
        }
        var hash = ContentHasher.Hash(text);
        lock (_exact)
        {
            if (!_exact.Add(hash))
            {
                Interlocked.Increment(ref Stats.ExactDuplicates);
                return 0;
            }
        }
        if (DeduplicateNear && _near.AddOrFindDuplicate(document.Id, text) is not null)
        {
            Interlocked.Increment(ref Stats.NearDuplicates);
            return 0;
        }

        var chunks = TextChunker.Chunk(text, Chunking);
        if (chunks.Count == 0) return 0;
        var vectors = await Embeddings.EmbedAsync(chunks.Select(c => (document.Title is null ? "" : document.Title + "\n") + c.Text).ToList(), cancellationToken).ConfigureAwait(false);
        var records = chunks.Select((c, i) =>
        {
            var metadata = new Dictionary<string, string>(document.Metadata, StringComparer.Ordinal)
            {
                ["document_id"] = document.Id,
                ["chunk"] = c.Index.ToString(CultureInfo.InvariantCulture),
                ["content_hash"] = hash,
            };
            if (document.Title is not null) metadata["title"] = document.Title;
            if (document.Url is not null) metadata["url"] = document.Url;
            if (c.Section is not null) metadata["section"] = c.Section;
            return new VectorRecord($"{document.Id}#{c.Index}", vectors[i], c.Text, metadata);
        }).ToList();
        await Store.UpsertAsync(records, cancellationToken).ConfigureAwait(false);
        Interlocked.Increment(ref Stats.Documents);
        Interlocked.Add(ref Stats.Chunks, records.Count);
        return records.Count;
    }

    public async Task<IReadOnlyList<VectorMatch>> SearchAsync(string query, int top = 5, Func<VectorRecord, bool>? filter = null, CancellationToken cancellationToken = default)
    {
        var vector = (await Embeddings.EmbedAsync([query], cancellationToken).ConfigureAwait(false))[0];
        return await Store.SearchAsync(vector, top, filter, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieval-augmented answer: the top chunks become numbered context for the language model, which
    /// must cite them. Without a model, returns the best passages verbatim.
    /// </summary>
    public async Task<RagAnswer> AskAsync(string question, int top = 4, CancellationToken cancellationToken = default)
    {
        var matches = await SearchAsync(question, top, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (LanguageModel is null)
            return new RagAnswer(matches.Count == 0 ? "No relevant content found." : matches[0].Record.Text, matches);

        var context = new StringBuilder();
        for (var i = 0; i < matches.Count; i++)
            context.Append('[').Append(i + 1).Append("] ").Append(matches[i].Record.Metadata.GetValueOrDefault("title")).Append('\n').Append(matches[i].Record.Text).Append("\n\n");
        var answer = await LanguageModel.CompleteAsync(
            $"Answer the question using only the numbered sources. Cite sources like [1]. If the answer is not in them, say so.\n\nSources:\n{context}\nQuestion: {question}",
            "You answer questions from crawled documents, faithfully and concisely.", cancellationToken).ConfigureAwait(false);
        return new RagAnswer(answer, matches);
    }
}

public sealed record RagAnswer(string Answer, IReadOnlyList<VectorMatch> Sources);

/// <summary>
/// Item pipeline that feeds scraped content into a <see cref="KnowledgeBase"/>: Website → Scrapy.Net →
/// clean → extract → chunk → embed → vector store. Items provide text in a field (default <c>text</c>,
/// or <c>html</c> which is run through <see cref="ArticleExtractor"/>), or supply a custom
/// <see cref="DocumentSelector"/>. Optionally tags each document with a category and a summary.
/// </summary>
public sealed class RagIngestionPipeline(KnowledgeBase knowledgeBase) : ItemPipeline
{
    private ILogger? _logger;
    private IStatsCollector? _stats;

    public KnowledgeBase KnowledgeBase { get; } = knowledgeBase;

    /// <summary>Custom mapping from item to document (return <c>null</c> to skip).</summary>
    public Func<object, IngestDocument?>? DocumentSelector { get; init; }

    public string TextField { get; init; } = "text";

    public string HtmlField { get; init; } = "html";

    public ICategorizer? Categorizer { get; init; }

    public ISummarizer? Summarizer { get; init; }

    public override ValueTask OpenSpiderAsync(Spider spider)
    {
        _logger = spider.Logger;
        _stats = spider.Crawler.Stats;
        return ValueTask.CompletedTask;
    }

    public override async ValueTask<object> ProcessItemAsync(object item, Spider spider)
    {
        var document = DocumentSelector?.Invoke(item) ?? FromItem(item);
        if (document is null) return item;
        if (Categorizer is not null)
        {
            var (category, confidence) = await Categorizer.CategorizeAsync(document.Text).ConfigureAwait(false);
            document.Metadata["category"] = category;
            document.Metadata["category_confidence"] = confidence.ToString("0.00", CultureInfo.InvariantCulture);
        }
        if (Summarizer is not null) document.Metadata["summary"] = await Summarizer.SummarizeAsync(document.Text).ConfigureAwait(false);

        var before = KnowledgeBase.Stats.NearDuplicates + KnowledgeBase.Stats.ExactDuplicates;
        var chunks = await KnowledgeBase.AddAsync(document).ConfigureAwait(false);
        _stats?.Inc("rag/documents_seen");
        if (chunks > 0)
        {
            _stats?.Inc("rag/documents_ingested");
            _stats?.Inc("rag/chunks", chunks);
        }
        else if (KnowledgeBase.Stats.NearDuplicates + KnowledgeBase.Stats.ExactDuplicates > before)
        {
            _stats?.Inc("rag/duplicates");
            _logger?.LogDebug("Duplicate content skipped: {Id}", document.Id);
        }
        return item;
    }

    private IngestDocument? FromItem(object item)
    {
        var adapter = ItemAdapter.For(item);
        var url = adapter.ResolveFieldName("url") is { } u ? adapter.Get(u)?.ToString() : null;
        var title = adapter.ResolveFieldName("title") is { } t ? adapter.Get(t)?.ToString() : null;
        string? text = null;
        if (adapter.ResolveFieldName(TextField) is { } tf) text = adapter.Get(tf)?.ToString();
        if (string.IsNullOrWhiteSpace(text) && adapter.ResolveFieldName(HtmlField) is { } hf && adapter.Get(hf) is string html)
        {
            var article = ArticleExtractor.Extract(Selector.FromHtml(html), url ?? "about:blank");
            text = article.Text;
            title ??= article.Title;
        }
        if (string.IsNullOrWhiteSpace(text)) return null;
        var id = url ?? ContentHasher.Hash(text)[..16];
        var doc = new IngestDocument(id, text, title, url);
        foreach (var name in adapter.FieldNames)
        {
            if (name.Equals(TextField, StringComparison.OrdinalIgnoreCase) || name.Equals(HtmlField, StringComparison.OrdinalIgnoreCase)) continue;
            if (adapter.Get(name) is { } v and (string or IFormattable)) doc.Metadata[name.ToLowerInvariant()] = Convert.ToString(v, CultureInfo.InvariantCulture)!;
        }
        return doc;
    }
}
