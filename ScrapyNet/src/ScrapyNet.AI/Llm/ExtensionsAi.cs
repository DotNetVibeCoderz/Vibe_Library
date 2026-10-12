using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.AI;

namespace ScrapyNet.AI;

/// <summary>
/// Bridges to Microsoft.Extensions.AI. Any <see cref="IChatClient"/> or
/// <see cref="IEmbeddingGenerator{TInput, TEmbedding}"/> (OpenAI, Azure AI Inference, Ollama, ONNX, ...)
/// plugs into Scrapy.Net's summarizers, categorizers and RAG pipeline, and Scrapy.Net's own models plug
/// into anything built on Microsoft.Extensions.AI.
/// </summary>
public static class ExtensionsAiAdapters
{
    /// <summary>Uses a Microsoft.Extensions.AI chat client wherever Scrapy.Net takes an <see cref="ILanguageModel"/>.</summary>
    public static ILanguageModel AsLanguageModel(this IChatClient client, ChatOptions? options = null) => new ChatClientLanguageModel(client, options);

    /// <summary>
    /// Uses a Microsoft.Extensions.AI embedding generator wherever Scrapy.Net takes an
    /// <see cref="IEmbeddingGenerator"/>. <paramref name="dimensions"/> defaults to the generator's metadata.
    /// </summary>
    public static IEmbeddingGenerator AsScrapyNetEmbeddings(this IEmbeddingGenerator<string, Embedding<float>> generator, int? dimensions = null) =>
        new EmbeddingGeneratorAdapter(generator, dimensions);

    /// <summary>Exposes a Scrapy.Net language model as a Microsoft.Extensions.AI chat client.</summary>
    public static IChatClient AsChatClient(this ILanguageModel model, string? modelId = null) => new LanguageModelChatClient(model, modelId);

    /// <summary>Exposes a Scrapy.Net embedding generator as a Microsoft.Extensions.AI one.</summary>
    public static IEmbeddingGenerator<string, Embedding<float>> AsEmbeddingGenerator(this IEmbeddingGenerator generator, string? modelId = null) =>
        new ScrapyNetEmbeddingGenerator(generator, modelId);
}

/// <summary><see cref="ILanguageModel"/> over an <see cref="IChatClient"/>.</summary>
public sealed class ChatClientLanguageModel(IChatClient client, ChatOptions? options = null) : ILanguageModel
{
    public IChatClient Client { get; } = client;

    public async Task<string> CompleteAsync(string prompt, string? system = null, CancellationToken cancellationToken = default)
    {
        List<ChatMessage> messages = system is null ? [] : [new ChatMessage(ChatRole.System, system)];
        messages.Add(new ChatMessage(ChatRole.User, prompt));
        var response = await Client.GetResponseAsync(messages, options?.Clone(), cancellationToken).ConfigureAwait(false);
        return response.Text;
    }
}

/// <summary>Scrapy.Net <see cref="IEmbeddingGenerator"/> over a Microsoft.Extensions.AI generator.</summary>
public sealed class EmbeddingGeneratorAdapter : IEmbeddingGenerator
{
    private readonly IEmbeddingGenerator<string, Embedding<float>> _generator;
    private int _dimensions;

    public EmbeddingGeneratorAdapter(IEmbeddingGenerator<string, Embedding<float>> generator, int? dimensions = null)
    {
        _generator = generator;
        _dimensions = dimensions
            ?? (generator.GetService(typeof(EmbeddingGeneratorMetadata)) as EmbeddingGeneratorMetadata)?.DefaultModelDimensions
            ?? 0;
    }

    /// <summary>Known dimensions; 0 until the first batch when neither the caller nor the metadata said.</summary>
    public int Dimensions => _dimensions;

    public async Task<float[][]> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
    {
        if (texts.Count == 0) return [];
        var embeddings = await _generator.GenerateAsync(texts, cancellationToken: cancellationToken).ConfigureAwait(false);
        var vectors = embeddings.Select(e => e.Vector.ToArray()).ToArray();
        if (_dimensions == 0 && vectors.Length > 0) _dimensions = vectors[0].Length;
        return vectors;
    }
}

/// <summary><see cref="IChatClient"/> over a Scrapy.Net <see cref="ILanguageModel"/>.</summary>
/// <remarks>
/// System messages become the system prompt; the remaining conversation is flattened into one prompt
/// (<c>role: text</c> per line, the last user message unlabeled when it is the only one). Streaming
/// yields the whole completion as a single update. Tools and response formats are not supported.
/// </remarks>
public sealed class LanguageModelChatClient(ILanguageModel model, string? modelId = null) : IChatClient
{
    private readonly ChatClientMetadata _metadata = new("scrapynet", null, modelId);

    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var (system, prompt) = Flatten(messages);
        var text = await model.CompleteAsync(prompt, system, cancellationToken).ConfigureAwait(false);
        return new ChatResponse(new ChatMessage(ChatRole.Assistant, text)) { ModelId = options?.ModelId ?? modelId };
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        yield return new ChatResponseUpdate(ChatRole.Assistant, response.Text) { ModelId = response.ModelId };
    }

    internal static (string? System, string Prompt) Flatten(IEnumerable<ChatMessage> messages)
    {
        var list = messages.ToList();
        var system = string.Join("\n\n", list.Where(m => m.Role == ChatRole.System).Select(m => m.Text));
        var rest = list.Where(m => m.Role != ChatRole.System).ToList();
        string prompt;
        if (rest.Count == 1 && rest[0].Role == ChatRole.User)
        {
            prompt = rest[0].Text;
        }
        else
        {
            var sb = new StringBuilder();
            foreach (var m in rest) sb.Append(m.Role.Value).Append(": ").AppendLine(m.Text);
            prompt = sb.ToString().TrimEnd();
        }
        return (system.Length == 0 ? null : system, prompt);
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is not null ? null
        : serviceType == typeof(ChatClientMetadata) ? _metadata
        : serviceType.IsInstanceOfType(model) ? model
        : serviceType.IsInstanceOfType(this) ? this
        : null;

    public void Dispose() => (model as IDisposable)?.Dispose();
}

/// <summary>Microsoft.Extensions.AI generator over a Scrapy.Net <see cref="IEmbeddingGenerator"/>.</summary>
public sealed class ScrapyNetEmbeddingGenerator(IEmbeddingGenerator generator, string? modelId = null) : IEmbeddingGenerator<string, Embedding<float>>
{
    private readonly EmbeddingGeneratorMetadata _metadata = new("scrapynet", null, modelId, generator.Dimensions);

    public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
    {
        var vectors = await generator.EmbedAsync([.. values], cancellationToken).ConfigureAwait(false);
        return new GeneratedEmbeddings<Embedding<float>>(vectors.Select(v => new Embedding<float>(v) { ModelId = modelId }));
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is not null ? null
        : serviceType == typeof(EmbeddingGeneratorMetadata) ? _metadata
        : serviceType.IsInstanceOfType(generator) ? generator
        : serviceType.IsInstanceOfType(this) ? this
        : null;

    public void Dispose() => (generator as IDisposable)?.Dispose();
}
