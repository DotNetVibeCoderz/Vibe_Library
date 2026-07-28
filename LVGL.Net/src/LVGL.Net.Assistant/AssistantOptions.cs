using System.Collections.Specialized;
using System.Globalization;

namespace Lvgl.Assistant;

/// <summary>Which chat model provider the assistant talks to.</summary>
public enum AssistantProvider
{
    OpenAI,
    Anthropic,
    Gemini,
    Ollama,
}

/// <summary>
/// Everything the assistant reads from <c>app.config</c>.
/// </summary>
/// <remarks>
/// <para>
/// Settings live in the host application's <c>appSettings</c> section under the <c>Assistant:</c>
/// prefix, so a user can retune the persona, model or provider without a rebuild. The class is a
/// plain POCO with a static loader rather than an options-pattern binding: the designer is a
/// desktop app with no DI container, and one file of explicit parsing is easier to follow than a
/// configuration pipeline.
/// </para>
/// <para>
/// API keys are read from <c>app.config</c> too, but an environment variable of the same name wins.
/// That keeps secrets out of a file that tends to get committed.
/// </para>
/// </remarks>
public sealed class AssistantOptions
{
    /// <summary>Prefix for every key in <c>appSettings</c>.</summary>
    public const string Prefix = "Assistant:";

    /// <summary>The assistant's name, shown throughout the UI.</summary>
    public string Name { get; set; } = "Jack The Code Bender";

    /// <summary>Provider used for new sessions.</summary>
    public AssistantProvider Provider { get; set; } = AssistantProvider.OpenAI;

    /// <summary>The persona / system prompt.</summary>
    public string SystemPrompt { get; set; } = PromptTemplates.DefaultPersona;

    /// <summary>
    /// Sampling temperature, 0 to 2.
    /// </summary>
    /// <remarks>
    /// Not every model accepts it. Claude Opus 5, Opus 4.8 and Opus 4.7 removed the sampling
    /// parameters entirely and return HTTP 400 if one is sent, so the Anthropic connector drops
    /// this value for those models rather than failing the request. See
    /// <see cref="Providers.AnthropicModelTraits"/>.
    /// </remarks>
    public double Temperature { get; set; } = 0.7;

    /// <summary>Upper bound on tokens generated per reply.</summary>
    public int MaxTokens { get; set; } = 4096;

    /// <summary>How many previous turns are replayed to the model.</summary>
    public int HistoryTurnLimit { get; set; } = 40;

    /// <summary>Let the model call kernel functions automatically.</summary>
    public bool EnableFunctionCalling { get; set; } = true;

    /// <summary>Maximum automatic tool-call rounds before the assistant answers regardless.</summary>
    public int MaxToolIterations { get; set; } = 8;

    /// <summary>Per-provider model id and endpoint configuration.</summary>
    public ProviderOptions OpenAI { get; } = new("gpt-4o", "OPENAI_API_KEY");

    public ProviderOptions Anthropic { get; } = new("claude-opus-5", "ANTHROPIC_API_KEY");

    public ProviderOptions Gemini { get; } = new("gemini-2.0-flash", "GEMINI_API_KEY");

    /// <summary>Ollama runs locally and needs no key, only an endpoint.</summary>
    public ProviderOptions Ollama { get; } = new("llama3.2", ApiKeyVariable: null)
    {
        Endpoint = "http://localhost:11434",
    };

    /// <summary>API key for Tavily web search. Search is disabled when unset.</summary>
    public string? TavilyApiKey { get; set; }

    /// <summary>Where chat sessions are stored.</summary>
    public string SessionDirectory { get; set; } = DefaultSessionDirectory();

    /// <summary>Where uploaded attachments are stored and served from.</summary>
    public string AttachmentDirectory { get; set; } = Path.Combine(DefaultSessionDirectory(), "attachments");

    /// <summary>Port for the local attachment host. 0 picks a free port at startup.</summary>
    public int AttachmentPort { get; set; }

    /// <summary>Largest attachment accepted, in megabytes.</summary>
    public int MaxAttachmentMegabytes { get; set; } = 20;

    /// <summary>Settings for one provider.</summary>
    /// <param name="DefaultModel">Model id used when the config does not override it.</param>
    /// <param name="ApiKeyVariable">
    /// Environment variable consulted for the key; null for providers that need none.
    /// </param>
    public sealed record ProviderOptions(string DefaultModel, string? ApiKeyVariable)
    {
        /// <summary>Model id to request.</summary>
        public string Model { get; set; } = DefaultModel;

        /// <summary>API key. An environment variable of the configured name takes precedence.</summary>
        public string? ApiKey { get; set; }

        /// <summary>Base endpoint override; null uses the provider default.</summary>
        public string? Endpoint { get; set; }

        /// <summary>True when the provider has everything it needs to run.</summary>
        public bool IsConfigured =>
            ApiKeyVariable is null || !string.IsNullOrWhiteSpace(ResolveApiKey());

        /// <summary>Environment variable first, then the configured value.</summary>
        public string? ResolveApiKey()
        {
            if (ApiKeyVariable is not null)
            {
                var fromEnvironment = Environment.GetEnvironmentVariable(ApiKeyVariable);
                if (!string.IsNullOrWhiteSpace(fromEnvironment)) return fromEnvironment;
            }

            return string.IsNullOrWhiteSpace(ApiKey) ? null : ApiKey;
        }
    }

    /// <summary>Provider settings for the currently selected provider.</summary>
    public ProviderOptions For(AssistantProvider provider) => provider switch
    {
        AssistantProvider.OpenAI => OpenAI,
        AssistantProvider.Anthropic => Anthropic,
        AssistantProvider.Gemini => Gemini,
        AssistantProvider.Ollama => Ollama,
        _ => throw new ArgumentOutOfRangeException(nameof(provider)),
    };

    /// <summary>Providers that currently have a usable key or endpoint.</summary>
    public IEnumerable<AssistantProvider> ConfiguredProviders() =>
        Enum.GetValues<AssistantProvider>().Where(p => For(p).IsConfigured);

    /// <summary>
    /// Reads settings from a name/value collection - normally
    /// <c>ConfigurationManager.AppSettings</c>. Unknown keys are ignored and missing keys keep
    /// their defaults, so a partial config is valid.
    /// </summary>
    public static AssistantOptions Load(NameValueCollection settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var options = new AssistantOptions();

        string? Get(string key) => settings[Prefix + key];

        if (Get("Name") is { Length: > 0 } name) options.Name = name;
        if (Get("SystemPrompt") is { Length: > 0 } prompt) options.SystemPrompt = prompt;

        if (Enum.TryParse<AssistantProvider>(Get("Provider"), ignoreCase: true, out var provider))
        {
            options.Provider = provider;
        }

        if (TryDouble(Get("Temperature"), out var temperature))
        {
            options.Temperature = Math.Clamp(temperature, 0, 2);
        }

        if (TryInt(Get("MaxTokens"), out var maxTokens) && maxTokens > 0) options.MaxTokens = maxTokens;
        if (TryInt(Get("HistoryTurnLimit"), out var history) && history > 0) options.HistoryTurnLimit = history;
        if (TryInt(Get("MaxToolIterations"), out var iterations) && iterations > 0) options.MaxToolIterations = iterations;
        if (TryBool(Get("EnableFunctionCalling"), out var functions)) options.EnableFunctionCalling = functions;

        LoadProvider(options.OpenAI, "OpenAI", Get);
        LoadProvider(options.Anthropic, "Anthropic", Get);
        LoadProvider(options.Gemini, "Gemini", Get);
        LoadProvider(options.Ollama, "Ollama", Get);

        if (Get("TavilyApiKey") is { Length: > 0 } tavily) options.TavilyApiKey = tavily;
        options.TavilyApiKey ??= Environment.GetEnvironmentVariable("TAVILY_API_KEY");

        if (Get("SessionDirectory") is { Length: > 0 } sessions)
        {
            options.SessionDirectory = Environment.ExpandEnvironmentVariables(sessions);
            options.AttachmentDirectory = Path.Combine(options.SessionDirectory, "attachments");
        }

        if (Get("AttachmentDirectory") is { Length: > 0 } attachments)
        {
            options.AttachmentDirectory = Environment.ExpandEnvironmentVariables(attachments);
        }

        if (TryInt(Get("AttachmentPort"), out var port) && port is >= 0 and <= 65535)
        {
            options.AttachmentPort = port;
        }

        if (TryInt(Get("MaxAttachmentMegabytes"), out var megabytes) && megabytes > 0)
        {
            options.MaxAttachmentMegabytes = megabytes;
        }

        return options;
    }

    private static void LoadProvider(ProviderOptions target, string name, Func<string, string?> get)
    {
        if (get($"{name}:Model") is { Length: > 0 } model) target.Model = model;
        if (get($"{name}:ApiKey") is { Length: > 0 } key) target.ApiKey = key;
        if (get($"{name}:Endpoint") is { Length: > 0 } endpoint) target.Endpoint = endpoint;
    }

    private static bool TryDouble(string? text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static bool TryInt(string? text, out int value) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    private static bool TryBool(string? text, out bool value) => bool.TryParse(text, out value);

    private static string DefaultSessionDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "LVGL.Net",
        "assistant");
}
