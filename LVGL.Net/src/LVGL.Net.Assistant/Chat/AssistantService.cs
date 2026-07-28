using System.Text;
using Lvgl.Assistant.Plugins;
using Lvgl.Assistant.Providers;
using Lvgl.Ui;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Lvgl.Assistant.Chat;

/// <summary>
/// The assistant itself: owns sessions, attachments and the kernel, and answers messages.
/// </summary>
/// <remarks>
/// <para>
/// One kernel is built per provider and cached, because building one is not free and a user
/// switching back and forth between two models should not pay for it each time.
/// </para>
/// <para>
/// This class is UI-framework agnostic on purpose - it is what makes the assistant testable and
/// what would let a second front end (a CLI, say) reuse it without dragging in WPF.
/// </para>
/// </remarks>
public sealed class AssistantService : IDisposable
{
    private readonly Dictionary<AssistantProvider, AssistantKernel> _kernels = [];
    private readonly LvglDesignPlugin _designPlugin = new();
    private bool _disposed;

    public AssistantService(AssistantOptions options)
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));

        Sessions = new ChatSessionStore(options.SessionDirectory);
        Attachments = new AttachmentService(
            options.AttachmentDirectory,
            options.AttachmentPort,
            options.MaxAttachmentMegabytes);

        _designPlugin.DocumentProduced += (_, document) => LayoutProduced?.Invoke(this, document);
    }

    /// <summary>Configuration, as loaded from app.config.</summary>
    public AssistantOptions Options { get; }

    /// <summary>Session storage.</summary>
    public ChatSessionStore Sessions { get; }

    /// <summary>Attachment storage and local host.</summary>
    public AttachmentService Attachments { get; }

    /// <summary>Raised when the model produces a valid layout the designer could open.</summary>
    public event EventHandler<UiDocument>? LayoutProduced;

    /// <summary>The most recent layout the model produced, if any.</summary>
    public UiDocument? LastLayout => _designPlugin.LastDocument;

    /// <summary>Providers that have a key or endpoint configured.</summary>
    public IReadOnlyList<AssistantProvider> AvailableProviders() => Options.ConfiguredProviders().ToList();

    /// <summary>
    /// Sends a message and streams the reply.
    /// </summary>
    /// <remarks>
    /// The user's message is appended to the session before the call and the assistant's reply
    /// afterwards, so a cancelled or failed request still leaves a coherent transcript.
    /// </remarks>
    /// <param name="session">Conversation to continue.</param>
    /// <param name="text">What the user typed.</param>
    /// <param name="attachments">Files attached to this message.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>Reply fragments as they arrive.</returns>
    public async IAsyncEnumerable<string> SendAsync(
        ChatSession session,
        string text,
        IReadOnlyList<ChatAttachment>? attachments = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        attachments ??= [];

        var userMessage = new ChatMessageRecord
        {
            Author = ChatAuthor.User,
            Text = text ?? string.Empty,
            Attachments = attachments.ToList(),
        };

        session.Messages.Add(userMessage);
        session.UpdateTitleFromFirstMessage();
        Sessions.Save(session);

        AssistantKernel? kernel = null;
        string? setupError = null;

        try
        {
            kernel = GetKernel(session.Provider);
        }
        catch (AssistantConfigurationException ex)
        {
            // Captured rather than yielded here: C# does not allow yield inside a catch block.
            setupError = ex.Message;
        }

        if (kernel is null)
        {
            yield return Fail(session, setupError ?? "The assistant could not start.");
            yield break;
        }

        session.ModelId = kernel.ModelId;

        // Cleared per request so the transcript shows what this turn did, not the whole session.
        kernel.ToolCalls.Reset();

        var history = BuildHistory(session);
        var settings = AssistantKernelFactory.ExecutionSettings(Options, session.Provider);
        var chat = kernel.Kernel.GetRequiredService<IChatCompletionService>();

        var reply = new StringBuilder();
        var toolCalls = new List<string>();
        string? error = null;

        // The enumerator is stepped manually so an exception from the provider can be turned into
        // a message in the transcript - `yield return` is not allowed inside a try/catch.
        var stream = chat.GetStreamingChatMessageContentsAsync(history, settings, kernel.Kernel, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);

        try
        {
            while (true)
            {
                bool moved;

                try
                {
                    moved = await stream.MoveNextAsync().ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    error = Describe(ex);
                    break;
                }

                if (!moved) break;

                var chunk = stream.Current;

                if (chunk.Metadata?.TryGetValue("ToolCalls", out var raw) == true && raw is string[] names)
                {
                    toolCalls.AddRange(names);
                }

                if (string.IsNullOrEmpty(chunk.Content)) continue;

                reply.Append(chunk.Content);
                yield return chunk.Content;
            }
        }
        finally
        {
            await stream.DisposeAsync().ConfigureAwait(false);
        }

        if (error is not null)
        {
            var failure = Fail(session, error);
            yield return failure;
            yield break;
        }

        // Two sources: the streaming metadata the Anthropic connector emits mid-turn, and the
        // filter, which is the only way to see the invocations SK's own connectors make.
        toolCalls.AddRange(kernel.ToolCalls.Calls);

        session.Messages.Add(new ChatMessageRecord
        {
            Author = ChatAuthor.Assistant,
            Text = reply.ToString(),
            ToolCalls = toolCalls.Distinct(StringComparer.Ordinal).ToList(),
            ModelId = kernel.ModelId,
        });

        Sessions.Save(session);
    }

    /// <summary>Records a failure in the transcript and returns the text to display.</summary>
    private string Fail(ChatSession session, string message)
    {
        session.Messages.Add(new ChatMessageRecord
        {
            Author = ChatAuthor.Assistant,
            Text = message,
            IsError = true,
        });

        Sessions.Save(session);
        return message;
    }

    /// <summary>
    /// Turns a stored session into the history sent to the model: the persona, then the last
    /// <see cref="AssistantOptions.HistoryTurnLimit"/> turns.
    /// </summary>
    private ChatHistory BuildHistory(ChatSession session)
    {
        var history = new ChatHistory();

        var persona = string.IsNullOrWhiteSpace(session.SystemPrompt)
            ? Options.SystemPrompt
            : session.SystemPrompt;

        history.AddSystemMessage(persona);

        var recent = session.Messages.Count > Options.HistoryTurnLimit
            ? session.Messages.Skip(session.Messages.Count - Options.HistoryTurnLimit)
            : session.Messages;

        foreach (var message in recent)
        {
            switch (message.Author)
            {
                case ChatAuthor.User:
                    history.Add(BuildUserMessage(message));
                    break;

                case ChatAuthor.Assistant when !message.IsError:
                    history.AddAssistantMessage(message.Text);
                    break;

                case ChatAuthor.System:
                    history.AddSystemMessage(message.Text);
                    break;
            }
        }

        return history;
    }

    /// <summary>
    /// Builds a user turn, adding image attachments as inline image content and document
    /// attachments as links appended to the text.
    /// </summary>
    private ChatMessageContent BuildUserMessage(ChatMessageRecord message)
    {
        var items = new ChatMessageContentItemCollection();
        var text = new StringBuilder(message.Text);

        var documents = message.Attachments.Where(a => a.Kind == AttachmentKind.Document).ToList();
        if (documents.Count > 0)
        {
            text.AppendLine().AppendLine();
            text.AppendLine("Attached files:");

            foreach (var document in documents)
            {
                // A URL rather than the contents: the model can decide whether it needs the file
                // and fetch it with the web plugin, instead of every attachment burning tokens.
                text.AppendLine($"- {document.FileName} ({document.MimeType}): {document.Url}");
            }
        }

        items.Add(new TextContent(text.ToString()));

        foreach (var image in message.Attachments.Where(a => a.Kind == AttachmentKind.Image))
        {
            try
            {
                // Sent as bytes, not as the loopback URL, which a hosted model cannot reach.
                items.Add(new ImageContent(Attachments.ReadBytes(image), image.MimeType));
            }
            catch (IOException)
            {
                // The file was moved or deleted since it was attached; the text still mentions it.
            }
        }

        return new ChatMessageContent(AuthorRole.User, items);
    }

    /// <summary>Builds, or returns the cached, kernel for a provider.</summary>
    public AssistantKernel GetKernel(AssistantProvider provider)
    {
        if (_kernels.TryGetValue(provider, out var existing)) return existing;

        var kernel = AssistantKernelFactory.Create(Options, provider);

        // The design plugin is registered as a shared instance so the designer can pick up the
        // layout the model produced; the factory's own copy is replaced here.
        kernel.Kernel.Plugins.Remove(kernel.Kernel.Plugins["lvgl_design"]);
        kernel.Kernel.Plugins.AddFromObject(_designPlugin, "lvgl_design");

        _kernels[provider] = kernel;
        return kernel;
    }

    /// <summary>Drops cached kernels so changed settings take effect.</summary>
    public void InvalidateKernels() => _kernels.Clear();

    private static string Describe(Exception ex) => ex switch
    {
        HttpRequestException http =>
            $"The request failed: {http.Message}\n\nCheck the API key, the model id, and that you are online.",
        AssistantConfigurationException configuration => configuration.Message,
        _ => $"Something went wrong: {ex.Message}",
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Attachments.Dispose();
    }
}
