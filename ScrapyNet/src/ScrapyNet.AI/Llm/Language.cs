using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ScrapyNet.AI;

/// <summary>A text-in, text-out language model.</summary>
public interface ILanguageModel
{
    Task<string> CompleteAsync(string prompt, string? system = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// Chat completions from any OpenAI-compatible endpoint (<c>/v1/chat/completions</c>): OpenAI, Azure
/// OpenAI, Ollama, LM Studio, vLLM, OpenRouter, and so on.
/// </summary>
public sealed class OpenAiChatModel : ILanguageModel, IDisposable
{
    private readonly HttpClient _http;
    private readonly string _model;

    /// <param name="apiKeyHeader">
    /// <c>null</c> sends <c>Authorization: Bearer KEY</c> (OpenAI, DeepSeek, Ollama, ...); Azure OpenAI keys
    /// go in <c>api-key</c> — use <see cref="ForAzure"/>.
    /// </param>
    public OpenAiChatModel(string model, string? apiKey = null, string baseUrl = "https://api.openai.com/v1/", HttpMessageHandler? handler = null, string? apiKeyHeader = null)
    {
        _model = model;
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.BaseAddress = new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/");
        _http.Timeout = TimeSpan.FromMinutes(5);
        if (string.IsNullOrEmpty(apiKey)) return;
        if (apiKeyHeader is null) _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        else _http.DefaultRequestHeaders.Add(apiKeyHeader, apiKey);
    }

    /// <summary>
    /// Azure OpenAI through its OpenAI-compatible v1 API (<c>{endpoint}/openai/v1/</c>). <paramref name="deployment"/>
    /// is the deployment name. Reasoning models (gpt-5, o-series) are configured without temperature.
    /// </summary>
    public static OpenAiChatModel ForAzure(string endpoint, string apiKey, string deployment, bool reasoningModel = true, HttpMessageHandler? handler = null) =>
        new(deployment, apiKey, endpoint.TrimEnd('/') + "/openai/v1/", handler, "api-key")
        {
            Temperature = reasoningModel ? null : 0.2,
            TokenLimitParameter = reasoningModel ? "max_completion_tokens" : "max_tokens",
            MaxTokens = reasoningModel ? 4000 : 800,
        };

    /// <summary>Sampling temperature; <c>null</c> omits it (required by reasoning models, which only allow the default).</summary>
    public double? Temperature { get; init; } = 0.2;

    /// <summary>Output token limit. Reasoning models count hidden reasoning tokens here too, so give them room.</summary>
    public int MaxTokens { get; init; } = 800;

    /// <summary><c>max_tokens</c> for classic chat models, <c>max_completion_tokens</c> for reasoning models.</summary>
    public string TokenLimitParameter { get; init; } = "max_tokens";

    public async Task<string> CompleteAsync(string prompt, string? system = null, CancellationToken cancellationToken = default)
    {
        var messages = new List<object>();
        if (system is not null) messages.Add(new { role = "system", content = system });
        messages.Add(new { role = "user", content = prompt });
        var payload = new Dictionary<string, object> { ["model"] = _model, ["messages"] = messages, [TokenLimitParameter] = MaxTokens };
        if (Temperature is { } temperature) payload["temperature"] = temperature;
        var body = JsonSerializer.Serialize(payload, ScrapyJson.Default);
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync("chat/completions", content, cancellationToken).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"Chat request failed ({(int)response.StatusCode}): {json}");
        return (string?)JsonNode.Parse(json)!["choices"]![0]!["message"]!["content"] ?? "";
    }

    public void Dispose() => _http.Dispose();
}

/// <summary>Summarizes text.</summary>
public interface ISummarizer
{
    Task<string> SummarizeAsync(string text, int maxSentences = 3, CancellationToken cancellationToken = default);
}

/// <summary>
/// Extractive summaries with TextRank: sentences are graph nodes weighted by word-overlap similarity,
/// ranked by power iteration, and the top ones are returned in their original order. No model needed.
/// </summary>
public sealed class ExtractiveSummarizer : ISummarizer
{
    private static readonly HashSet<string> StopWords =
    [
        "the", "a", "an", "and", "or", "of", "to", "in", "on", "for", "is", "are", "was", "were", "be", "by", "with", "that", "this", "it", "as", "at", "from",
        "yang", "dan", "di", "ke", "dari", "untuk", "dengan", "ini", "itu", "pada", "adalah", "atau", "dalam",
    ];

    public Task<string> SummarizeAsync(string text, int maxSentences = 3, CancellationToken cancellationToken = default) =>
        Task.FromResult(string.Join(" ", RankSentences(text).Take(maxSentences).OrderBy(s => s.Position).Select(s => s.Sentence)));

    /// <summary>Sentences with their TextRank scores, best first.</summary>
    public static List<(string Sentence, double Score, int Position)> RankSentences(string text, int iterations = 30, double damping = 0.85)
    {
        var sentences = TextNormalizer.SplitSentences(TextNormalizer.Normalize(text).Replace("\n\n", " "));
        var n = sentences.Count;
        if (n == 0) return [];
        var bags = sentences.Select(s => TextNormalizer.Tokenize(s).Where(w => !StopWords.Contains(w) && w.Length > 2).ToHashSet()).ToArray();

        var weights = new double[n, n];
        var rowSums = new double[n];
        for (var i = 0; i < n; i++)
            for (var j = i + 1; j < n; j++)
            {
                if (bags[i].Count < 2 || bags[j].Count < 2) continue;
                var overlap = bags[i].Count(bags[j].Contains);
                if (overlap == 0) continue;
                var w = overlap / (Math.Log(bags[i].Count) + Math.Log(bags[j].Count));
                weights[i, j] = weights[j, i] = w;
                rowSums[i] += w;
                rowSums[j] += w;
            }

        var scores = Enumerable.Repeat(1.0 / n, n).ToArray();
        for (var iter = 0; iter < iterations; iter++)
        {
            var next = new double[n];
            for (var i = 0; i < n; i++)
            {
                double sum = 0;
                for (var j = 0; j < n; j++)
                    if (weights[j, i] > 0) sum += weights[j, i] / rowSums[j] * scores[j];
                next[i] = (1 - damping) / n + damping * sum;
            }
            scores = next;
        }
        return [.. sentences.Select((s, i) => (s, scores[i], i)).OrderByDescending(x => x.Item2)];
    }
}

/// <summary>Abstractive summaries from a language model.</summary>
public sealed class LlmSummarizer(ILanguageModel model) : ISummarizer
{
    public string Language { get; init; } = "the same language as the text";

    public Task<string> SummarizeAsync(string text, int maxSentences = 3, CancellationToken cancellationToken = default) =>
        model.CompleteAsync($"Summarize the following text in at most {maxSentences} sentences, in {Language}. Reply with the summary only.\n\n{Truncate(text, 12000)}",
            "You write faithful, concise summaries. Never add facts that are not in the text.", cancellationToken);

    internal static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}

/// <summary>Assigns a category to text.</summary>
public interface ICategorizer
{
    Task<(string Category, double Confidence)> CategorizeAsync(string text, CancellationToken cancellationToken = default);
}

/// <summary>Rule-based categorization: the category whose keywords appear most often wins.</summary>
public sealed class KeywordCategorizer(IReadOnlyDictionary<string, string[]> categories, string fallback = "other") : ICategorizer
{
    public Task<(string Category, double Confidence)> CategorizeAsync(string text, CancellationToken cancellationToken = default)
    {
        var tokens = TextNormalizer.Tokenize(text).ToList();
        var joined = " " + string.Join(' ', tokens) + " ";
        var scores = categories.ToDictionary(c => c.Key, c => c.Value.Sum(k => CountOccurrences(joined, " " + k.ToLowerInvariant() + " ")));
        var total = scores.Values.Sum();
        if (total == 0) return Task.FromResult((fallback, 0.0));
        var best = scores.MaxBy(kv => kv.Value);
        return Task.FromResult((best.Key, (double)best.Value / total));
    }

    private static int CountOccurrences(string text, string term)
    {
        var count = 0;
        for (var i = text.IndexOf(term, StringComparison.Ordinal); i >= 0; i = text.IndexOf(term, i + 1, StringComparison.Ordinal)) count++;
        return count;
    }
}

/// <summary>
/// Multinomial Naive Bayes text classifier with Laplace smoothing. Train it on a few labelled examples
/// per category (e.g. from a first crawl) and it categorizes the rest in microseconds.
/// </summary>
public sealed class NaiveBayesCategorizer : ICategorizer
{
    private readonly Dictionary<string, Dictionary<string, int>> _wordCounts = [];
    private readonly Dictionary<string, int> _docCounts = [];
    private readonly Dictionary<string, int> _totalWords = [];
    private readonly HashSet<string> _vocabulary = [];
    private int _documents;

    public IReadOnlyCollection<string> Categories => _docCounts.Keys;

    public NaiveBayesCategorizer Train(string text, string category)
    {
        _documents++;
        _docCounts[category] = _docCounts.GetValueOrDefault(category) + 1;
        if (!_wordCounts.TryGetValue(category, out var counts)) _wordCounts[category] = counts = [];
        foreach (var w in TextNormalizer.Tokenize(text))
        {
            counts[w] = counts.GetValueOrDefault(w) + 1;
            _totalWords[category] = _totalWords.GetValueOrDefault(category) + 1;
            _vocabulary.Add(w);
        }
        return this;
    }

    public Task<(string Category, double Confidence)> CategorizeAsync(string text, CancellationToken cancellationToken = default)
    {
        if (_documents == 0) throw new InvalidOperationException("Train the classifier first.");
        var tokens = TextNormalizer.Tokenize(text).ToList();
        var logScores = new Dictionary<string, double>();
        foreach (var category in _docCounts.Keys)
        {
            var score = Math.Log((double)_docCounts[category] / _documents);
            var counts = _wordCounts[category];
            var denominator = _totalWords.GetValueOrDefault(category) + _vocabulary.Count;
            foreach (var t in tokens) score += Math.Log((counts.GetValueOrDefault(t) + 1.0) / denominator);
            logScores[category] = score;
        }
        // Softmax over log scores for a calibrated-ish confidence.
        var max = logScores.Values.Max();
        var exp = logScores.ToDictionary(kv => kv.Key, kv => Math.Exp(kv.Value - max));
        var sum = exp.Values.Sum();
        var best = exp.MaxBy(kv => kv.Value);
        return Task.FromResult((best.Key, best.Value / sum));
    }
}

/// <summary>Zero-shot categorization with a language model, constrained to a fixed label set.</summary>
public sealed class LlmCategorizer(ILanguageModel model, IReadOnlyList<string> categories) : ICategorizer
{
    public async Task<(string Category, double Confidence)> CategorizeAsync(string text, CancellationToken cancellationToken = default)
    {
        var answer = await model.CompleteAsync(
            $"Categories: {string.Join(", ", categories)}\n\nText:\n{LlmSummarizer.Truncate(text, 6000)}\n\nReply with exactly one category from the list and nothing else.",
            "You are a precise text classifier.", cancellationToken).ConfigureAwait(false);
        var match = categories.FirstOrDefault(c => answer.Contains(c, StringComparison.OrdinalIgnoreCase));
        return (match ?? categories[0], match is null ? 0.0 : 1.0);
    }
}
