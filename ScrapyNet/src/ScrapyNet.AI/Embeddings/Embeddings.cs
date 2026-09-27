using System.Net.Http.Headers;
using System.Numerics.Tensors;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ScrapyNet.AI;

/// <summary>Turns text into vectors for semantic search.</summary>
public interface IEmbeddingGenerator
{
    int Dimensions { get; }

    Task<float[][]> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default);
}

/// <summary>
/// A local, dependency-free embedding: signed feature hashing of word unigrams, bigrams and
/// character trigrams, with sublinear term frequency, L2-normalized. It captures lexical overlap
/// rather than meaning, but it is deterministic, instant, works offline and is a solid baseline for
/// keyword-heavy corpora (documentation, product catalogues). Swap in
/// <see cref="OpenAiEmbeddingGenerator"/> for semantic embeddings.
/// </summary>
public sealed class HashingEmbeddingGenerator(int dimensions = 512) : IEmbeddingGenerator
{
    public int Dimensions { get; } = dimensions;

    public Task<float[][]> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default) =>
        Task.FromResult(texts.Select(Embed).ToArray());

    public float[] Embed(string text)
    {
        var counts = new Dictionary<int, float>();
        // Function words carry no topic; left in, "how do crawlers..." would match every "how" article.
        var tokens = TextNormalizer.Tokenize(text).Where(t => !TextNormalizer.IsStopWord(t)).Select(TextNormalizer.Stem).ToArray();
        for (var i = 0; i < tokens.Length; i++)
        {
            Add(counts, tokens[i], 1.0f);
            if (i + 1 < tokens.Length) Add(counts, tokens[i] + " " + tokens[i + 1], 0.7f);
            var t = tokens[i];
            if (t.Length > 3)
                for (var c = 0; c + 3 <= t.Length; c++) Add(counts, "#" + t.AsSpan(c, 3).ToString(), 0.3f);
        }
        var vector = new float[Dimensions];
        foreach (var (key, weight) in counts)
        {
            var index = (int)((uint)key % (uint)Dimensions);
            var sign = ((uint)key >> 31) == 0 ? 1f : -1f;
            vector[index] += sign * MathF.Log(1 + weight);
        }
        var norm = TensorPrimitives.Norm(vector);
        if (norm > 0) TensorPrimitives.Divide(vector, norm, vector);
        return vector;
    }

    private static void Add(Dictionary<int, float> counts, string feature, float weight)
    {
        var h = StableHash(feature);
        counts[h] = counts.GetValueOrDefault(h) + weight;
    }

    /// <summary>FNV-1a, stable across processes (unlike string.GetHashCode), so stored vectors stay valid.</summary>
    private static int StableHash(string s)
    {
        var hash = 2166136261u;
        foreach (var c in s)
        {
            hash ^= c;
            hash *= 16777619u;
        }
        return (int)hash;
    }
}

/// <summary>
/// Embeddings from any OpenAI-compatible <c>/v1/embeddings</c> endpoint: OpenAI, Azure OpenAI, Ollama,
/// LM Studio, vLLM, LocalAI. Requests are batched.
/// </summary>
public sealed class OpenAiEmbeddingGenerator : IEmbeddingGenerator, IDisposable
{
    private readonly HttpClient _http;
    private readonly string _model;

    /// <param name="apiKeyHeader"><c>null</c> = <c>Authorization: Bearer</c>; <c>"api-key"</c> for Azure OpenAI.</param>
    public OpenAiEmbeddingGenerator(string model, int dimensions, string? apiKey = null, string baseUrl = "https://api.openai.com/v1/", HttpMessageHandler? handler = null, string? apiKeyHeader = null)
    {
        _model = model;
        Dimensions = dimensions;
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.BaseAddress = new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/");
        if (!string.IsNullOrEmpty(apiKey))
        {
            if (apiKeyHeader is null) _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            else _http.DefaultRequestHeaders.Add(apiKeyHeader, apiKey);
        }
    }

    public int Dimensions { get; }

    public int BatchSize { get; init; } = 64;

    public async Task<float[][]> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
    {
        var result = new List<float[]>(texts.Count);
        for (var i = 0; i < texts.Count; i += BatchSize)
        {
            var batch = texts.Skip(i).Take(BatchSize).ToArray();
            var body = JsonSerializer.Serialize(new { model = _model, input = batch }, ScrapyJson.Default);
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using var response = await _http.PostAsync("embeddings", content, cancellationToken).ConfigureAwait(false);
            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) throw new HttpRequestException($"Embedding request failed ({(int)response.StatusCode}): {json}");
            var data = JsonNode.Parse(json)!["data"]!.AsArray().OrderBy(d => (int)d!["index"]!);
            result.AddRange(data.Select(d => d!["embedding"]!.AsArray().Select(v => (float)v!).ToArray()));
        }
        return [.. result];
    }

    public void Dispose() => _http.Dispose();
}
