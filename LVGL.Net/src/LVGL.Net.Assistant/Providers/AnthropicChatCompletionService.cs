using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Lvgl.Assistant.Providers;

/// <summary>
/// A Semantic Kernel <see cref="IChatCompletionService"/> backed by the official Anthropic SDK.
/// </summary>
/// <remarks>
/// <para>
/// Semantic Kernel ships connectors for OpenAI, Google and Ollama but not for Anthropic, so this
/// adapter fills the gap. It calls the official <c>Anthropic</c> package rather than pointing an
/// OpenAI connector at a compatibility endpoint: Claude's request shape genuinely differs - tool
/// results live in a user turn, thinking blocks must be echoed back unmodified, and several model
/// families reject <c>temperature</c> outright - and a shim would paper over exactly the parts
/// that matter.
/// </para>
/// <para>
/// Tool calling is handled here rather than by Semantic Kernel's automatic invocation, which is
/// implemented per-connector. The loop advertises the kernel's functions as Claude tools, executes
/// the ones Claude asks for, and feeds the results back until it answers or the iteration budget
/// runs out.
/// </para>
/// </remarks>
public sealed class AnthropicChatCompletionService : IChatCompletionService
{
    private readonly AnthropicClient _client;
    private readonly string _modelId;
    private readonly int _maxTokens;
    private readonly double? _temperature;
    private readonly int _maxToolIterations;

    /// <summary>Creates the service.</summary>
    /// <param name="apiKey">Anthropic API key.</param>
    /// <param name="modelId">Model id, e.g. <c>claude-opus-5</c>.</param>
    /// <param name="maxTokens">Upper bound on generated tokens per reply.</param>
    /// <param name="temperature">
    /// Requested sampling temperature. Silently omitted from the request for model families that
    /// reject it - see <see cref="AnthropicModelTraits"/> and <see cref="TemperatureNotice"/>.
    /// </param>
    /// <param name="maxToolIterations">How many tool rounds before answering regardless.</param>
    public AnthropicChatCompletionService(
        string apiKey,
        string modelId,
        int maxTokens = 4096,
        double? temperature = null,
        int maxToolIterations = 8)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);

        _client = new AnthropicClient { ApiKey = apiKey };
        _modelId = modelId;
        _maxTokens = maxTokens;
        _maxToolIterations = Math.Max(1, maxToolIterations);

        // Drop the temperature rather than let the request 400. The caller can surface
        // TemperatureNotice to explain why their slider had no effect.
        _temperature = AnthropicModelTraits.SupportsTemperature(modelId) ? temperature : null;
        TemperatureNotice = temperature is null ? null : AnthropicModelTraits.ExplainIgnoredTemperature(modelId);
    }

    /// <summary>
    /// Set when a configured temperature could not be sent, explaining why. Null when the value
    /// was honoured or none was configured.
    /// </summary>
    public string? TemperatureNotice { get; }

    public IReadOnlyDictionary<string, object?> Attributes { get; } = new Dictionary<string, object?>();

    public async Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
        ChatHistory chatHistory,
        PromptExecutionSettings? executionSettings = null,
        Kernel? kernel = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chatHistory);

        var (system, messages) = Translate(chatHistory);
        var tools = BuildTools(kernel);

        for (var iteration = 0; iteration < _maxToolIterations; iteration++)
        {
            var response = await _client.Messages.Create(
                CreateParams(system, messages, tools),
                cancellationToken: cancellationToken).ConfigureAwait(false);

            var toolUses = CollectToolUses(response);
            if (toolUses.Count == 0 || kernel is null)
            {
                return [new ChatMessageContent(AuthorRole.Assistant, ExtractText(response))];
            }

            // Claude asked for tools: echo its turn back verbatim, then answer every tool_use
            // with a matching tool_result in a single user turn. A missing result is rejected.
            messages.Add(new MessageParam { Role = Role.Assistant, Content = EchoAssistant(response) });
            messages.Add(new MessageParam
            {
                Role = Role.User,
                Content = await InvokeToolsAsync(kernel, toolUses, cancellationToken).ConfigureAwait(false),
            });
        }

        return
        [
            new ChatMessageContent(
                AuthorRole.Assistant,
                $"I stopped after {_maxToolIterations} tool rounds without reaching an answer. " +
                "Try narrowing the request, or raise Assistant:MaxToolIterations."),
        ];
    }

    public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
        ChatHistory chatHistory,
        PromptExecutionSettings? executionSettings = null,
        Kernel? kernel = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chatHistory);

        var (system, messages) = Translate(chatHistory);
        var tools = BuildTools(kernel);

        for (var iteration = 0; iteration < _maxToolIterations; iteration++)
        {
            // Tool rounds run non-streaming: the loop needs the complete tool_use blocks before it
            // can do anything, so streaming them would add complexity with nothing to show for it.
            var probe = await _client.Messages.Create(
                CreateParams(system, messages, tools),
                cancellationToken: cancellationToken).ConfigureAwait(false);

            var toolUses = CollectToolUses(probe);

            if (toolUses.Count == 0 || kernel is null)
            {
                // No tools pending, so this turn is the answer. Emit it in chunks so the UI
                // renders progressively even though the text is already complete.
                foreach (var chunk in Chunk(ExtractText(probe)))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    yield return new StreamingChatMessageContent(AuthorRole.Assistant, chunk);
                }

                yield break;
            }

            yield return new StreamingChatMessageContent(
                AuthorRole.Assistant,
                content: null,
                metadata: new Dictionary<string, object?>
                {
                    ["ToolCalls"] = toolUses.Select(t => t.Name).ToArray(),
                });

            messages.Add(new MessageParam { Role = Role.Assistant, Content = EchoAssistant(probe) });
            messages.Add(new MessageParam
            {
                Role = Role.User,
                Content = await InvokeToolsAsync(kernel, toolUses, cancellationToken).ConfigureAwait(false),
            });
        }

        yield return new StreamingChatMessageContent(
            AuthorRole.Assistant,
            $"I stopped after {_maxToolIterations} tool rounds without reaching an answer.");
    }

    private MessageCreateParams CreateParams(string? system, List<MessageParam> messages, List<ToolUnion>? tools)
    {
        // Every optional field is init-only, so they are all decided here rather than assigned
        // afterwards. Temperature stays null for model families that reject it - see the
        // constructor and AnthropicModelTraits.
        // The implicit string conversion is not null-tolerant, so the null case is kept separate
        // rather than folded into a ternary.
        MessageCreateParamsSystem? systemPrompt = null;
        if (!string.IsNullOrWhiteSpace(system)) systemPrompt = system;

        // CS0618: Temperature is marked obsolete because current Claude models reject it. This
        // connector still sets it for older families that accept it, and AnthropicModelTraits has
        // already forced _temperature to null for the ones that do not - so the deprecation is
        // handled rather than ignored.
#pragma warning disable CS0618
        return new MessageCreateParams
        {
            Model = _modelId,
            MaxTokens = _maxTokens,
            Messages = messages,
            System = systemPrompt,
            Temperature = _temperature,
            Tools = tools is { Count: > 0 } ? tools : null,
        };
#pragma warning restore CS0618
    }

    /// <summary>
    /// Flattens a Semantic Kernel history into Claude's shape: system text is hoisted out of the
    /// message list, and consecutive same-role turns are left as-is (the API merges them).
    /// </summary>
    private static (string? System, List<MessageParam> Messages) Translate(ChatHistory history)
    {
        var system = new StringBuilder();
        var messages = new List<MessageParam>();

        foreach (var message in history)
        {
            if (message.Role == AuthorRole.System)
            {
                if (system.Length > 0) system.AppendLine().AppendLine();
                system.Append(message.Content);
                continue;
            }

            var role = message.Role == AuthorRole.Assistant ? Role.Assistant : Role.User;
            var blocks = TranslateContent(message);
            if (blocks.Count == 0) continue;

            messages.Add(new MessageParam { Role = role, Content = blocks });
        }

        // Claude requires the conversation to open with a user turn.
        if (messages.Count > 0 && messages[0].Role == Role.Assistant)
        {
            messages.Insert(0, new MessageParam
            {
                Role = Role.User,
                Content = new List<ContentBlockParam> { new TextBlockParam { Text = "(continuing)" } },
            });
        }

        return (system.Length == 0 ? null : system.ToString(), messages);
    }

    private static List<ContentBlockParam> TranslateContent(ChatMessageContent message)
    {
        var blocks = new List<ContentBlockParam>();

        foreach (var item in message.Items)
        {
            switch (item)
            {
                case TextContent { Text.Length: > 0 } text:
                    blocks.Add(new TextBlockParam { Text = text.Text });
                    break;

                case ImageContent image when image.Data is { Length: > 0 } data:
                    blocks.Add(new ImageBlockParam
                    {
                        Source = new Base64ImageSource
                        {
                            Data = Convert.ToBase64String(data.ToArray()),
                            MediaType = MediaTypeFor(image.MimeType),
                        },
                    });
                    break;

                case ImageContent { Uri: not null } image:
                    blocks.Add(new ImageBlockParam
                    {
                        Source = new UrlImageSource { Url = image.Uri.ToString() },
                    });
                    break;
            }
        }

        // A message whose items were all unsupported still has its plain text.
        if (blocks.Count == 0 && !string.IsNullOrWhiteSpace(message.Content))
        {
            blocks.Add(new TextBlockParam { Text = message.Content });
        }

        return blocks;
    }

    private static MediaType MediaTypeFor(string? mimeType) => mimeType?.ToLowerInvariant() switch
    {
        "image/png" => MediaType.ImagePng,
        "image/gif" => MediaType.ImageGif,
        "image/webp" => MediaType.ImageWebP,
        _ => MediaType.ImageJpeg,
    };

    /// <summary>Advertises the kernel's functions as Claude tools.</summary>
    private static List<ToolUnion>? BuildTools(Kernel? kernel)
    {
        if (kernel is null) return null;

        var tools = new List<ToolUnion>();

        foreach (var plugin in kernel.Plugins)
        {
            foreach (var function in plugin)
            {
                tools.Add(new Tool
                {
                    // Claude tool names allow [a-zA-Z0-9_-], which is exactly what Semantic
                    // Kernel's plugin-function naming produces.
                    Name = ToolName(plugin.Name, function.Name),
                    Description = function.Description,
                    InputSchema = BuildSchema(function),
                });
            }
        }

        return tools.Count == 0 ? null : tools;
    }

    private static string ToolName(string plugin, string function) => $"{plugin}-{function}";

    private static InputSchema BuildSchema(KernelFunction function)
    {
        var properties = new Dictionary<string, JsonElement>();
        var required = new List<string>();

        foreach (var parameter in function.Metadata.Parameters)
        {
            properties[parameter.Name] = JsonSerializer.SerializeToElement(new
            {
                type = JsonTypeFor(parameter.ParameterType),
                description = parameter.Description ?? parameter.Name,
            });

            if (parameter.IsRequired) required.Add(parameter.Name);
        }

        // InputSchema.Type is auto-set to "object" by the constructor - do not set it here.
        return new InputSchema
        {
            Properties = properties,
            Required = required,
        };
    }

    // System.Type is spelled out: the SDK's Anthropic.Models.Messages namespace also defines a
    // `Type`, and importing both makes the bare name ambiguous.
    private static string JsonTypeFor(System.Type? type)
    {
        if (type is null) return "string";

        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        if (underlying == typeof(bool)) return "boolean";
        if (underlying == typeof(int) || underlying == typeof(long)) return "integer";
        if (underlying == typeof(double) || underlying == typeof(float) || underlying == typeof(decimal)) return "number";

        return "string";
    }

    private static List<ToolUseBlock> CollectToolUses(Message response)
    {
        var uses = new List<ToolUseBlock>();

        foreach (var block in response.Content)
        {
            if (block.TryPickToolUse(out var toolUse)) uses.Add(toolUse);
        }

        return uses;
    }

    /// <summary>
    /// Rebuilds the assistant turn as request blocks. Thinking blocks carry a signature the API
    /// validates, so they are copied through unmodified rather than dropped.
    /// </summary>
    private static List<ContentBlockParam> EchoAssistant(Message response)
    {
        var blocks = new List<ContentBlockParam>();

        foreach (var block in response.Content)
        {
            if (block.TryPickText(out var text))
            {
                blocks.Add(new TextBlockParam { Text = text.Text });
            }
            else if (block.TryPickThinking(out var thinking))
            {
                blocks.Add(new ThinkingBlockParam
                {
                    Thinking = thinking.Thinking,
                    Signature = thinking.Signature,
                });
            }
            else if (block.TryPickRedactedThinking(out var redacted))
            {
                blocks.Add(new RedactedThinkingBlockParam { Data = redacted.Data });
            }
            else if (block.TryPickToolUse(out var toolUse))
            {
                blocks.Add(new ToolUseBlockParam
                {
                    ID = toolUse.ID,
                    Name = toolUse.Name,
                    Input = toolUse.Input,
                });
            }
        }

        return blocks;
    }

    private static async Task<List<ContentBlockParam>> InvokeToolsAsync(
        Kernel kernel,
        List<ToolUseBlock> toolUses,
        CancellationToken cancellationToken)
    {
        var results = new List<ContentBlockParam>();

        foreach (var toolUse in toolUses)
        {
            var (text, isError) = await InvokeOneAsync(kernel, toolUse, cancellationToken).ConfigureAwait(false);

            results.Add(new ToolResultBlockParam
            {
                ToolUseID = toolUse.ID,
                Content = text,
                IsError = isError,
            });
        }

        return results;
    }

    private static async Task<(string Text, bool IsError)> InvokeOneAsync(
        Kernel kernel,
        ToolUseBlock toolUse,
        CancellationToken cancellationToken)
    {
        var separator = toolUse.Name.IndexOf('-');
        if (separator <= 0)
        {
            return ($"'{toolUse.Name}' is not a known tool.", true);
        }

        var pluginName = toolUse.Name[..separator];
        var functionName = toolUse.Name[(separator + 1)..];

        if (!kernel.Plugins.TryGetFunction(pluginName, functionName, out var function))
        {
            return ($"'{toolUse.Name}' is not a known tool.", true);
        }

        var arguments = new KernelArguments();
        foreach (var (key, value) in toolUse.Input)
        {
            arguments[key] = value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.TryGetInt64(out var l) ? l : value.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => value.GetRawText(),
            };
        }

        try
        {
            var result = await function.InvokeAsync(kernel, arguments, cancellationToken).ConfigureAwait(false);
            return (result.ToString() ?? string.Empty, false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Returned as a tool result rather than thrown: Claude can read the error and adapt,
            // which is far more useful than failing the whole turn.
            return ($"The tool failed: {ex.Message}", true);
        }
    }

    private static string ExtractText(Message response)
    {
        var builder = new StringBuilder();

        foreach (var block in response.Content)
        {
            if (block.TryPickText(out var text)) builder.Append(text.Text);
        }

        return builder.ToString();
    }

    /// <summary>Splits a completed reply into UI-sized chunks so rendering looks progressive.</summary>
    private static IEnumerable<string> Chunk(string text, int size = 48)
    {
        for (var offset = 0; offset < text.Length; offset += size)
        {
            yield return text.Substring(offset, Math.Min(size, text.Length - offset));
        }
    }
}
