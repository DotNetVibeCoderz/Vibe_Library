using System.Collections.ObjectModel;
using System.Configuration;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Lvgl.Assistant;
using Lvgl.Assistant.Chat;
using Lvgl.Designer.Views;
using Lvgl.Ui;

namespace Lvgl.Designer.Assistant;

/// <summary>
/// Jack, the design assistant, docked beside the canvas.
/// </summary>
/// <remarks>
/// <para>
/// A panel rather than a separate window because the point of the assistant is to act on the
/// document you are looking at: a layout it produces goes onto the canvas immediately (and is one
/// undo away), and having both on screen at once is what makes that loop feel direct.
/// </para>
/// <para>
/// Presentation only. Sessions, providers, tools and the model call live in
/// <see cref="AssistantService"/>, which has no UI dependency.
/// </para>
/// </remarks>
public sealed class ChatPanel : UserControl
{
    private static AssistantService? _shared;

    private readonly ObservableCollection<ChatSession> _sessions = [];
    private readonly List<ChatAttachment> _pending = [];

    private readonly ComboBox _sessionBox = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _providerBox = new() { Width = 116 };
    private readonly TextBlock _model = new() { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _transcript = new() { Spacing = 14, Margin = new Thickness(14, 14, 14, 18) };
    private readonly ScrollViewer _scroller;
    private readonly TextBox _composer;
    private readonly WrapPanel _attachmentStrip = new() { Orientation = Orientation.Horizontal };
    private readonly Button _send;
    private readonly Button _stop;

    private AssistantService? _assistant;
    private ChatSession? _session;
    private CancellationTokenSource? _inFlight;
    private Control? _pendingBubble;
    private bool _suppressSelection;
    private bool _started;

    public ChatPanel()
    {
        _sessionBox.ItemsSource = _sessions;
        _sessionBox.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<ChatSession>((session, _) =>
            new TextBlock { Text = session?.Title ?? string.Empty, TextTrimming = TextTrimming.CharacterEllipsis });
        _sessionBox.SelectionChanged += (_, _) =>
        {
            if (!_suppressSelection && _sessionBox.SelectedItem is ChatSession session) SelectSession(session);
        };

        _providerBox.ItemsSource = Enum.GetValues<AssistantProvider>();
        _providerBox.SelectionChanged += (_, _) => OnProviderChanged();

        _model.Classes.Add("designator");
        _status.Classes.Add("dim");
        _status.Classes.Add("small");

        _scroller = new ScrollViewer { Content = _transcript, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

        _composer = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 64,
            MaxHeight = 180,
            PlaceholderText = "Describe a screen, or ask about LVGL.Net…",
        };
        ToolTip.SetTip(_composer, "Enter sends, Shift+Enter starts a new line");
        _composer.AddHandler(KeyDownEvent, OnComposerKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        _send = new Button { Content = "Send", MinWidth = 72 };
        _send.Classes.Add("primary");
        _send.Click += (_, _) => _ = SendAsync();

        _stop = new Button { Content = "Stop", MinWidth = 64, IsVisible = false };
        _stop.Click += (_, _) => _inFlight?.Cancel();

        Content = BuildLayout();
    }

    /// <summary>Raised when the assistant produces a layout the designer should show.</summary>
    public event EventHandler<UiDocument>? LayoutProduced;

    /// <summary>Raised when the user closes the panel from its header.</summary>
    public event EventHandler? HideRequested;

    /// <summary>
    /// Where the assistant's settings come from: App.config by default. The UI tests replace it so
    /// their chats land in a temporary folder, not in the user's real history.
    /// </summary>
    internal static Func<AssistantOptions> OptionsFactory { get; set; } = () => AssistantOptions.Load(ConfigurationManager.AppSettings);

    /// <summary>One service per process: it owns the attachment HTTP host and the cached kernels.</summary>
    private static AssistantService Shared() =>
        _shared ??= new AssistantService(OptionsFactory());

    /// <summary>Releases the shared service when the designer shuts down.</summary>
    public static void DisposeShared()
    {
        _shared?.Dispose();
        _shared = null;
    }

    #region Layout

    private Control BuildLayout()
    {
        var title = new TextBlock { Text = "JACK THE CODE BENDER", VerticalAlignment = VerticalAlignment.Center };
        title.Classes.Add("head");

        var hide = GhostButton("✕", "Hide the assistant (Ctrl+J)", (_, _) => HideRequested?.Invoke(this, EventArgs.Empty));

        var header = new DockPanel();
        DockPanel.SetDock(hide, Dock.Right);
        header.Children.Add(hide);
        header.Children.Add(title);

        var headerBorder = new Border { Child = header };
        headerBorder.Classes.Add("panel-head");

        var newChat = GhostButton("New", "Start a new chat", (_, _) => CreateSession());
        var reset = GhostButton("Clear", "Clear the messages, keep the chat", async (_, _) => await ResetSessionAsync());
        var delete = GhostButton("Delete", "Delete this chat", async (_, _) => await DeleteSessionAsync());
        delete.Classes.Add("danger");

        var sessionRow = new DockPanel();
        var sessionButtons = new StackPanel { Orientation = Orientation.Horizontal, Children = { newChat, reset, delete }, Margin = new Thickness(6, 0, 0, 0) };
        DockPanel.SetDock(sessionButtons, Dock.Right);
        sessionRow.Children.Add(sessionButtons);
        sessionRow.Children.Add(_sessionBox);

        var prompts = new Button { Content = "Prompts", Flyout = BuildPromptFlyout() };
        prompts.Classes.Add("ghost");
        ToolTip.SetTip(prompts, "Ready-made design requests");

        var providerRow = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
        DockPanel.SetDock(prompts, Dock.Right);
        DockPanel.SetDock(_providerBox, Dock.Left);
        providerRow.Children.Add(prompts);
        providerRow.Children.Add(_providerBox);
        _model.Margin = new Thickness(10, 0, 6, 0);
        providerRow.Children.Add(_model);

        var settings = new Border
        {
            Padding = new Thickness(12, 8, 12, 8),
            BorderBrush = Brush("Trace"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = new StackPanel { Children = { sessionRow, providerRow, _status }, Spacing = 2 },
        };
        _status.Margin = new Thickness(0, 4, 0, 0);

        var attachImage = GhostButton("Image", "Attach an image Jack can see", async (_, _) => await AttachAsync(true));
        var attachDocument = GhostButton("File", "Attach a document", async (_, _) => await AttachAsync(false));

        var composerButtons = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
        var sendButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { _stop, _send } };
        DockPanel.SetDock(sendButtons, Dock.Right);
        composerButtons.Children.Add(sendButtons);
        composerButtons.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { attachImage, attachDocument } });

        var composer = new Border
        {
            Padding = new Thickness(12, 10),
            BorderBrush = Brush("Trace"),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = new StackPanel { Children = { _attachmentStrip, _composer, composerButtons }, Spacing = 4 },
        };

        var root = new DockPanel();
        DockPanel.SetDock(headerBorder, Dock.Top);
        DockPanel.SetDock(settings, Dock.Top);
        DockPanel.SetDock(composer, Dock.Bottom);
        root.Children.Add(headerBorder);
        root.Children.Add(settings);
        root.Children.Add(composer);
        root.Children.Add(new Border { Background = Brush("Mask"), Child = _scroller });

        var panel = new Border { Child = root };
        panel.Classes.Add("panel");
        return panel;
    }

    private static Button GhostButton(string text, string tip, EventHandler<Avalonia.Interactivity.RoutedEventArgs> click)
    {
        var button = new Button { Content = text, Padding = new Thickness(7, 3) };
        button.Classes.Add("ghost");
        ToolTip.SetTip(button, tip);
        button.Click += click;
        return button;
    }

    private Flyout BuildPromptFlyout()
    {
        var list = new StackPanel { Spacing = 2, Width = 360 };

        foreach (var category in PromptTemplates.Categories)
        {
            var head = new TextBlock { Text = category.ToUpperInvariant(), Margin = new Thickness(6, 10, 0, 4) };
            head.Classes.Add("head");
            list.Children.Add(head);

            foreach (var template in PromptTemplates.InCategory(category))
            {
                var title = new TextBlock { Text = template.Title, FontWeight = FontWeight.Medium };
                var description = new TextBlock { Text = template.Description, TextWrapping = TextWrapping.Wrap };
                description.Classes.Add("dim");
                description.Classes.Add("small");

                var button = new Button
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Content = new StackPanel { Children = { title, description }, Spacing = 1 },
                };
                button.Classes.Add("ghost");
                button.Click += (_, _) =>
                {
                    _composer.Text = template.Prompt;
                    _composer.CaretIndex = _composer.Text.Length;
                    _composer.Focus();
                };

                list.Children.Add(button);
            }
        }

        return new Flyout
        {
            Placement = PlacementMode.BottomEdgeAlignedRight,
            Content = new ScrollViewer { Content = list, MaxHeight = 520 },
        };
    }

    #endregion

    /// <summary>
    /// Starts the assistant the first time the panel is shown, so a designer session that never
    /// opens it pays nothing - no kernel, no HTTP host, no config parsing.
    /// </summary>
    public void EnsureStarted()
    {
        if (_started) return;
        _started = true;

        _assistant = Shared();
        _assistant.LayoutProduced += OnAssistantLayout;

        _suppressSelection = true;
        _providerBox.SelectedItem = _assistant.Options.Provider;
        _suppressSelection = false;

        LoadSessions();
        UpdateStatus();
    }

    /// <summary>Puts the caret in the composer, for the show-panel shortcut.</summary>
    public void FocusComposer() => _composer.Focus();

    /// <summary>Cancels any in-flight request. Called when the designer closes.</summary>
    public void Shutdown()
    {
        _inFlight?.Cancel();
        if (_assistant is not null) _assistant.LayoutProduced -= OnAssistantLayout;
    }

    #region Sessions

    private void LoadSessions()
    {
        if (_assistant is null) return;

        _sessions.Clear();
        foreach (var session in _assistant.Sessions.LoadAll()) _sessions.Add(session);

        if (_sessions.Count == 0) CreateSession();
        else SelectSession(_sessions[0]);
    }

    private void CreateSession()
    {
        if (_assistant is null) return;

        var provider = SelectedProvider();
        var session = _assistant.Sessions.Create(provider, _assistant.Options.For(provider).Model);

        _sessions.Insert(0, session);
        SelectSession(session);
    }

    private void SelectSession(ChatSession session)
    {
        _session = session;

        _suppressSelection = true;
        _sessionBox.SelectedItem = session;
        _providerBox.SelectedItem = session.Provider;
        _suppressSelection = false;

        _pending.Clear();
        RenderAttachments();
        RenderTranscript();
        UpdateStatus();
    }

    private async Task ResetSessionAsync()
    {
        if (_assistant is null || _session is null || _session.IsEmpty) return;
        if (TopLevel.GetTopLevel(this) is not Window window) return;

        if (!await Dialogs.ConfirmAsync(window, "Clear chat", "Clear every message in this chat? The chat itself is kept.", "Clear messages")) return;

        _session.Reset();
        _assistant.Sessions.Save(_session);
        RenderTranscript();
    }

    private async Task DeleteSessionAsync()
    {
        if (_assistant is null || _session is null) return;
        if (TopLevel.GetTopLevel(this) is not Window window) return;

        if (!await Dialogs.ConfirmAsync(window, "Delete chat", $"Delete \"{_session.Title}\"? This cannot be undone.", "Delete chat", destructive: true)) return;

        _assistant.Sessions.Delete(_session.Id);
        _sessions.Remove(_session);

        if (_sessions.Count == 0) CreateSession();
        else SelectSession(_sessions[0]);
    }

    private void RefreshSessionTitle()
    {
        if (_session is null) return;
        var index = _sessions.IndexOf(_session);
        if (index < 0) return;

        _suppressSelection = true;
        _sessions[index] = _session;
        _sessionBox.SelectedItem = _session;
        _suppressSelection = false;
    }

    #endregion

    #region Provider and status

    private AssistantProvider SelectedProvider() =>
        _providerBox.SelectedItem is AssistantProvider provider ? provider : _assistant?.Options.Provider ?? AssistantProvider.OpenAI;

    private void OnProviderChanged()
    {
        if (_suppressSelection || _assistant is null || _session is null) return;

        _session.Provider = SelectedProvider();
        _session.ModelId = _assistant.Options.For(_session.Provider).Model;
        _assistant.Sessions.Save(_session);
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        if (_assistant is null) return;

        var provider = SelectedProvider();
        var settings = _assistant.Options.For(provider);
        _model.Text = settings.Model;

        if (!settings.IsConfigured)
        {
            _status.Text = $"No API key. Set {settings.ApiKeyVariable}, or fill it in App.config.";
            _status.Foreground = Brush("Warn");
            return;
        }

        _status.ClearValue(TextBlock.ForegroundProperty);

        var notes = new List<string>();
        if (provider == AssistantProvider.Anthropic &&
            Lvgl.Assistant.Providers.AnthropicModelTraits.ExplainIgnoredTemperature(settings.Model) is not null)
        {
            notes.Add("temperature not sent to this model");
        }

        if (string.IsNullOrWhiteSpace(_assistant.Options.TavilyApiKey)) notes.Add("web search off");

        _status.Text = notes.Count == 0 ? "Ready." : "Ready · " + string.Join(" · ", notes);
    }

    #endregion

    #region Attachments

    private async Task AttachAsync(bool image)
    {
        if (_assistant is null || TopLevel.GetTopLevel(this) is not Window window) return;

        var paths = image
            ? await Dialogs.OpenFilesAsync(window, "Attach an image", Dialogs.AnyImage)
            : await Dialogs.OpenFilesAsync(window, "Attach a document", Dialogs.Documents);

        foreach (var path in paths)
        {
            try
            {
                _pending.Add(_assistant.Attachments.Add(path));
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
            {
                await Dialogs.InformAsync(window, "Could not attach", ex.Message);
            }
        }

        RenderAttachments();
    }

    private void RenderAttachments()
    {
        _attachmentStrip.Children.Clear();

        foreach (var attachment in _pending)
        {
            var remove = new Button { Content = "✕", Padding = new Thickness(4, 0), FontSize = 10 };
            remove.Classes.Add("ghost");
            remove.Click += (_, _) =>
            {
                _pending.Remove(attachment);
                _assistant?.Attachments.Remove(attachment);
                RenderAttachments();
            };

            var name = new TextBlock { Text = attachment.FileName, VerticalAlignment = VerticalAlignment.Center };
            name.Classes.Add("small");

            var chip = new Border { Margin = new Thickness(0, 0, 4, 4), Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { name, remove } } };
            chip.Classes.Add("chip");
            _attachmentStrip.Children.Add(chip);
        }
    }

    #endregion

    #region Sending

    private void OnComposerKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.KeyModifiers.HasFlag(KeyModifiers.Shift)) return;

        e.Handled = true;
        _ = SendAsync();
    }

    /// <summary>Sends the composer's text. Public for the headless UI tests.</summary>
    internal async Task SendAsync()
    {
        if (_inFlight is not null || _assistant is null || _session is null) return;

        var text = _composer.Text?.Trim() ?? string.Empty;
        if (text.Length == 0 && _pending.Count == 0) return;

        var attachments = _pending.ToList();
        _composer.Text = string.Empty;
        _pending.Clear();
        RenderAttachments();
        SetBusy(true);

        _inFlight = new CancellationTokenSource();
        var reply = new StringBuilder();

        // Show the user's message straight away; the reply streams in below it.
        var userMessage = new ChatMessageRecord { Author = ChatAuthor.User, Text = text, Attachments = attachments };
        _transcript.Children.Add(Bubble(userMessage));
        _pendingBubble = null;
        ScrollToEnd();

        var lastRender = DateTime.MinValue;

        try
        {
            await foreach (var chunk in _assistant.SendAsync(_session, text, attachments, _inFlight.Token))
            {
                reply.Append(chunk);

                // Markdown re-layout is not free; a few renders a second keeps streaming smooth.
                if (DateTime.UtcNow - lastRender > TimeSpan.FromMilliseconds(80))
                {
                    ShowPending(reply.ToString());
                    lastRender = DateTime.UtcNow;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Cancelling is a normal outcome; the transcript already holds what arrived.
        }
        finally
        {
            _inFlight?.Dispose();
            _inFlight = null;

            SetBusy(false);
            RefreshSessionTitle();
            RenderTranscript();
        }
    }

    private void ShowPending(string markdown)
    {
        var message = new ChatMessageRecord { Author = ChatAuthor.Assistant, Text = markdown };
        var bubble = Bubble(message);

        if (_pendingBubble is not null) _transcript.Children.Remove(_pendingBubble);
        _pendingBubble = bubble;
        _transcript.Children.Add(bubble);
        ScrollToEnd();
    }

    private void SetBusy(bool busy)
    {
        _send.IsEnabled = !busy;
        _composer.IsEnabled = !busy;
        _stop.IsVisible = busy;

        if (busy) _status.Text = "Thinking…";
        else UpdateStatus();
    }

    #endregion

    #region Transcript

    private void RenderTranscript()
    {
        _transcript.Children.Clear();
        _pendingBubble = null;

        if (_session is null || _session.IsEmpty)
        {
            _transcript.Children.Add(EmptyState());
            return;
        }

        foreach (var message in _session.Messages) _transcript.Children.Add(Bubble(message));
        ScrollToEnd();
    }

    private Control EmptyState()
    {
        var title = new TextBlock { Text = "Ask for a screen.", FontSize = 16, FontWeight = FontWeight.SemiBold };
        var body = new TextBlock
        {
            Text = "Jack designs layouts against the real document model and puts them on the canvas, one undo away. " +
                   "Try \"a thermostat for the 800×480 Pi display\", or pick one from Prompts.",
            TextWrapping = TextWrapping.Wrap,
        };
        body.Classes.Add("dim");

        return new StackPanel { Spacing = 6, Margin = new Thickness(4, 24, 4, 0), Children = { title, body } };
    }

    private Control Bubble(ChatMessageRecord message)
    {
        var content = new StackPanel { Spacing = 8 };
        var isUser = message.Author == ChatAuthor.User;

        if (!isUser)
        {
            var who = new TextBlock { Text = message.IsError ? "JACK · ERROR" : "JACK" };
            who.Classes.Add("designator");
            if (message.IsError) who.Foreground = Brush("Fault");
            content.Children.Add(who);
        }

        if (!string.IsNullOrEmpty(message.Text))
        {
            content.Children.Add(isUser
                ? new SelectableTextBlock { Text = message.Text, TextWrapping = TextWrapping.Wrap }
                : MarkdownView.Render(message.Text));
        }

        foreach (var attachment in message.Attachments) content.Children.Add(AttachmentView(attachment));

        if (message.ToolCalls.Count > 0)
        {
            var tools = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (var call in message.ToolCalls)
            {
                var name = new TextBlock { Text = call };
                name.Classes.Add("mono");
                name.Classes.Add("faint");
                var chip = new Border { Child = name, Margin = new Thickness(0, 0, 4, 4) };
                chip.Classes.Add("chip");
                tools.Children.Add(chip);
            }

            content.Children.Add(tools);
        }

        return new Border
        {
            Background = isUser ? Brush("Raised") : Brushes.Transparent,
            BorderBrush = isUser ? Brush("Trace") : Brushes.Transparent,
            BorderThickness = new Thickness(isUser ? 1 : 0),
            CornerRadius = new CornerRadius(6),
            Padding = isUser ? new Thickness(12, 8) : new Thickness(0),
            Margin = isUser ? new Thickness(36, 0, 0, 0) : new Thickness(0),
            HorizontalAlignment = isUser ? HorizontalAlignment.Right : HorizontalAlignment.Stretch,
            Child = content,
        };
    }

    private static Control AttachmentView(ChatAttachment attachment)
    {
        if (attachment.Kind == AttachmentKind.Image && attachment.LocalPath is { } path && File.Exists(path))
        {
            try
            {
                return new Image
                {
                    Source = new Bitmap(path),
                    MaxHeight = 160,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Stretch = Stretch.Uniform,
                };
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException)
            {
                // Fall through to the chip: an image the decoder rejects is still an attachment.
            }
        }

        var name = new TextBlock { Text = attachment.FileName };
        name.Classes.Add("small");
        var chip = new Border { Child = name, HorizontalAlignment = HorizontalAlignment.Left };
        chip.Classes.Add("chip");
        return chip;
    }

    private void ScrollToEnd() => Dispatcher.UIThread.Post(() => _scroller.ScrollToEnd(), DispatcherPriority.Background);

    #endregion

    private void OnAssistantLayout(object? sender, UiDocument document) =>
        Dispatcher.UIThread.Post(() => LayoutProduced?.Invoke(this, document));

    private static IBrush Brush(string key) =>
        Application.Current!.TryFindResource(key, out var value) && value is IBrush brush ? brush : Brushes.Gray;
}
