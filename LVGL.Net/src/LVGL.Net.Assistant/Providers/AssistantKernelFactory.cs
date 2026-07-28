using Lvgl.Assistant.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.Google;
using Microsoft.SemanticKernel.Connectors.Ollama;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace Lvgl.Assistant.Providers;

/// <summary>The assembled kernel plus the details the UI needs to describe it.</summary>
/// <param name="Kernel">Configured kernel with plugins registered.</param>
/// <param name="Provider">Provider this kernel talks to.</param>
/// <param name="ModelId">Model id in use.</param>
/// <param name="Notice">
/// A non-fatal caveat worth showing the user - currently only a dropped temperature.
/// </param>
/// <param name="ToolCalls">Records which kernel functions ran, for display in the transcript.</param>
public sealed record AssistantKernel(
    Kernel Kernel,
    AssistantProvider Provider,
    string ModelId,
    string? Notice,
    ToolCallRecorder ToolCalls);

/// <summary>
/// Builds a Semantic Kernel for the selected provider and registers the assistant's plugins.
/// </summary>
/// <remarks>
/// Three of the four providers have first-party Semantic Kernel connectors; Anthropic does not, so
/// it is wired in through <see cref="AnthropicChatCompletionService"/> as a plain
/// <see cref="IChatCompletionService"/> registration. From the rest of the application's point of
/// view all four look identical.
/// </remarks>
public static class AssistantKernelFactory
{
    /// <summary>
    /// Creates a kernel for <paramref name="provider"/>.
    /// </summary>
    /// <exception cref="AssistantConfigurationException">
    /// The provider has no API key, or no endpoint in Ollama's case.
    /// </exception>
    public static AssistantKernel Create(AssistantOptions options, AssistantProvider provider)
    {
        ArgumentNullException.ThrowIfNull(options);

        var settings = options.For(provider);
        var builder = Kernel.CreateBuilder();
        string? notice = null;

        switch (provider)
        {
            case AssistantProvider.OpenAI:
                builder.AddOpenAIChatCompletion(
                    modelId: settings.Model,
                    apiKey: RequireKey(options, provider),
                    httpClient: EndpointClient(settings.Endpoint));
                break;

            case AssistantProvider.Anthropic:
            {
                var service = new AnthropicChatCompletionService(
                    apiKey: RequireKey(options, provider),
                    modelId: settings.Model,
                    maxTokens: options.MaxTokens,
                    temperature: options.Temperature,
                    maxToolIterations: options.MaxToolIterations);

                notice = service.TemperatureNotice;
                builder.Services.AddSingleton<IChatCompletionService>(service);
                break;
            }

            case AssistantProvider.Gemini:
                builder.AddGoogleAIGeminiChatCompletion(
                    modelId: settings.Model,
                    apiKey: RequireKey(options, provider));
                break;

            case AssistantProvider.Ollama:
            {
                var endpoint = settings.Endpoint ?? "http://localhost:11434";
                if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
                {
                    throw new AssistantConfigurationException(
                        $"Assistant:Ollama:Endpoint is not a valid URL: '{endpoint}'.");
                }

                builder.AddOllamaChatCompletion(modelId: settings.Model, endpoint: uri);
                break;
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(provider));
        }

        // Registered before Build so it is picked up as a filter for every function invocation,
        // including the ones Semantic Kernel's own connectors make internally.
        var recorder = new ToolCallRecorder();
        builder.Services.AddSingleton<IFunctionInvocationFilter>(recorder);

        var kernel = builder.Build();
        RegisterPlugins(kernel, options);

        return new AssistantKernel(kernel, provider, settings.Model, notice, recorder);
    }

    /// <summary>
    /// Registers the assistant's tools. Tavily is skipped when no key is configured rather than
    /// registered and failing at call time - an advertised tool that always errors wastes a
    /// round trip and teaches the model to distrust it.
    /// </summary>
    private static void RegisterPlugins(Kernel kernel, AssistantOptions options)
    {
        kernel.Plugins.AddFromObject(new TimePlugin(), "time");
        kernel.Plugins.AddFromObject(new MathPlugin(), "math");
        kernel.Plugins.AddFromObject(new WebPlugin(), "web");
        kernel.Plugins.AddFromObject(new LvglDesignPlugin(), "lvgl_design");

        if (!string.IsNullOrWhiteSpace(options.TavilyApiKey))
        {
            kernel.Plugins.AddFromObject(new TavilySearchPlugin(options.TavilyApiKey), "tavily");
        }
    }

    /// <summary>Execution settings that turn on automatic function calling where supported.</summary>
    public static PromptExecutionSettings ExecutionSettings(AssistantOptions options, AssistantProvider provider)
    {
        var behavior = options.EnableFunctionCalling
            ? FunctionChoiceBehavior.Auto()
            : FunctionChoiceBehavior.None();

        return provider switch
        {
            // The Anthropic connector runs its own tool loop, so Semantic Kernel's automatic
            // invocation must stay off or the tools would be executed twice.
            AssistantProvider.Anthropic => new PromptExecutionSettings(),

            AssistantProvider.OpenAI => new OpenAIPromptExecutionSettings
            {
                MaxTokens = options.MaxTokens,
                Temperature = options.Temperature,
                FunctionChoiceBehavior = behavior,
            },

            AssistantProvider.Gemini => new GeminiPromptExecutionSettings
            {
                MaxTokens = options.MaxTokens,
                Temperature = options.Temperature,
                FunctionChoiceBehavior = behavior,
            },

            AssistantProvider.Ollama => new OllamaPromptExecutionSettings
            {
                Temperature = (float)options.Temperature,
                FunctionChoiceBehavior = behavior,
            },

            _ => new PromptExecutionSettings(),
        };
    }

    private static string RequireKey(AssistantOptions options, AssistantProvider provider)
    {
        var settings = options.For(provider);
        var key = settings.ResolveApiKey();

        if (string.IsNullOrWhiteSpace(key))
        {
            throw new AssistantConfigurationException(
                $"No API key for {provider}. Set the {settings.ApiKeyVariable} environment variable, " +
                $"or add <add key=\"{AssistantOptions.Prefix}{provider}:ApiKey\" value=\"...\" /> " +
                "to appSettings in app.config.");
        }

        return key;
    }

    /// <summary>An HttpClient pinned to a custom endpoint, or null to use the provider default.</summary>
    private static HttpClient? EndpointClient(string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint)) return null;
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)) return null;

        return new HttpClient { BaseAddress = uri };
    }
}

/// <summary>Thrown when the assistant cannot start because of missing or invalid configuration.</summary>
public sealed class AssistantConfigurationException(string message) : Exception(message);
