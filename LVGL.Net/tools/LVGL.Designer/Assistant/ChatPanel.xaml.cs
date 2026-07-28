using System.Collections.ObjectModel;
using System.Configuration;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Lvgl.Assistant;
using Lvgl.Assistant.Chat;
using Lvgl.Assistant.Rendering;
using Lvgl.Ui;
using Microsoft.Win32;

namespace Lvgl.Designer.Assistant;

/// <summary>
/// The assistant docked into the designer, in the manner of an IDE's AI side panel.
/// </summary>
/// <remarks>
/// <para>
/// A panel rather than a separate window because the point of the assistant is to act on the
/// document you are looking at: a layout it produces is applied to the canvas immediately, and
/// having both on screen at once is what makes that loop feel direct.
/// </para>
/// <para>
/// Presentation only. Sessions, providers, tools and the model call live in
/// <see cref="AssistantService"/>, which has no WPF dependency.
/// </para>
/// </remarks>
public partial class ChatPanel : UserControl
{
    private static AssistantService? _shared;

    private readonly ChatMarkdownRenderer _renderer = new();
    private readonly ObservableCollection<ChatSession> _sessions = [];
    private readonly ObservableCollection<ChatAttachment> _pending = [];

    private AssistantService? _assistant;
    private ChatSession? _session;
    private CancellationTokenSource? _inFlight;
    private bool _webViewReady;
    private bool _suppressSelection;
    private bool _started;

    public ChatPanel()
    {
        InitializeComponent();

        SessionBox.ItemsSource = _sessions;
        AttachmentStrip.ItemsSource = _pending;
        ProviderBox.ItemsSource = Enum.GetValues<AssistantProvider>();
    }

    /// <summary>Raised when the assistant produces a layout the designer should show.</summary>
    public event EventHandler<UiDocument>? LayoutProduced;

    /// <summary>Raised when the user closes the panel from its header.</summary>
    public event EventHandler? HideRequested;

    /// <summary>
    /// One service per process: it owns the attachment HTTP host and the cached kernels.
    /// </summary>
    private static AssistantService Shared() =>
        _shared ??= new AssistantService(AssistantOptions.Load(ConfigurationManager.AppSettings));

    /// <summary>Releases the shared service when the designer shuts down.</summary>
    public static void DisposeShared()
    {
        _shared?.Dispose();
        _shared = null;
    }

    /// <summary>
    /// Starts the assistant the first time the panel is shown, so a designer session that never
    /// opens it pays nothing - no kernel, no HTTP host, no config parsing.
    /// </summary>
    public async Task EnsureStartedAsync()
    {
        if (_started) return;
        _started = true;

        _assistant = Shared();
        _assistant.LayoutProduced += OnAssistantLayout;

        _suppressSelection = true;
        ProviderBox.SelectedItem = _assistant.Options.Provider;
        _suppressSelection = false;

        await InitialiseWebViewAsync();
        LoadSessions();
        UpdateStatus();
    }

    /// <summary>Puts the caret in the composer, for the show-panel shortcut.</summary>
    public void FocusComposer() => Composer.Focus();

    /// <summary>Cancels any in-flight request. Called when the designer closes.</summary>
    public void Shutdown()
    {
        _inFlight?.Cancel();
        if (_assistant is not null) _assistant.LayoutProduced -= OnAssistantLayout;
    }

    private async Task InitialiseWebViewAsync()
    {
        try
        {
            await Transcript.EnsureCoreWebView2Async();
            _webViewReady = true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _webViewReady = false;
            TranscriptFallback.Visibility = Visibility.Visible;
            TranscriptFallbackText.Text = ex.Message;
        }
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
        SessionBox.SelectedItem = session;
        ProviderBox.SelectedItem = session.Provider;
        _suppressSelection = false;

        _pending.Clear();
        Render();
        UpdateStatus();
    }

    private void OnSessionSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelection) return;
        if (SessionBox.SelectedItem is ChatSession session) SelectSession(session);
    }

    private void OnNewSession(object sender, RoutedEventArgs e) => CreateSession();

    private void OnResetSession(object sender, RoutedEventArgs e)
    {
        if (_assistant is null || _session is null || _session.IsEmpty) return;

        var confirm = MessageBox.Show(
            Window.GetWindow(this),
            "Clear every message in this chat? The chat itself is kept.",
            "Reset chat",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes) return;

        _session.Reset();
        _assistant.Sessions.Save(_session);
        Render();
    }

    private void OnDeleteSession(object sender, RoutedEventArgs e)
    {
        if (_assistant is null || _session is null) return;

        var confirm = MessageBox.Show(
            Window.GetWindow(this),
            $"Delete \"{_session.Title}\"? This cannot be undone.",
            "Delete chat",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        _assistant.Sessions.Delete(_session.Id);
        _sessions.Remove(_session);

        if (_sessions.Count == 0) CreateSession();
        else SelectSession(_sessions[0]);
    }

    /// <summary>Rebuilds the session list entry so its title and count refresh.</summary>
    private void RefreshSessionList()
    {
        if (_session is null) return;

        var index = _sessions.IndexOf(_session);
        if (index < 0) return;

        _suppressSelection = true;
        _sessions.RemoveAt(index);
        _sessions.Insert(index, _session);
        SessionBox.SelectedItem = _session;
        _suppressSelection = false;
    }

    #endregion

    #region Provider and status

    private AssistantProvider SelectedProvider() =>
        ProviderBox.SelectedItem is AssistantProvider provider
            ? provider
            : _assistant?.Options.Provider ?? AssistantProvider.OpenAI;

    private void OnProviderChanged(object sender, SelectionChangedEventArgs e)
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

        ModelText.Text = settings.Model;

        if (!settings.IsConfigured)
        {
            StatusText.Text = $"No API key. Set {settings.ApiKeyVariable}, or fill it in app.config.";
            return;
        }

        var notes = new List<string>();

        if (provider == AssistantProvider.Anthropic &&
            Lvgl.Assistant.Providers.AnthropicModelTraits.ExplainIgnoredTemperature(settings.Model) is not null)
        {
            notes.Add("temperature ignored on this model");
        }

        if (string.IsNullOrWhiteSpace(_assistant.Options.TavilyApiKey)) notes.Add("web search off");

        StatusText.Text = notes.Count == 0 ? "Ready" : string.Join(" | ", notes);
    }

    #endregion

    #region Attachments

    private void OnAttachImage(object sender, RoutedEventArgs e) => Attach(
        "Attach an image",
        "Images|*.png;*.jpg;*.jpeg;*.gif;*.webp;*.bmp|All files|*.*");

    private void OnAttachDocument(object sender, RoutedEventArgs e) => Attach(
        "Attach a document",
        "Documents|*.pdf;*.txt;*.md;*.json;*.csv;*.xml;*.log;*.cs;*.c;*.h;*.yml;*.yaml|All files|*.*");

    private void Attach(string title, string filter)
    {
        if (_assistant is null) return;

        var dialog = new OpenFileDialog { Title = title, Filter = filter, Multiselect = true };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;

        foreach (var path in dialog.FileNames)
        {
            try
            {
                _pending.Add(_assistant.Attachments.Add(path));
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
            {
                MessageBox.Show(Window.GetWindow(this), ex.Message, "Could not attach",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private void OnRemoveAttachment(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ChatAttachment attachment } || _assistant is null) return;

        _pending.Remove(attachment);
        _assistant.Attachments.Remove(attachment);
    }

    #endregion

    #region Sending

    private void OnComposerKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || (Keyboard.Modifiers & ModifierKeys.Shift) != 0) return;

        e.Handled = true;
        _ = SendAsync();
    }

    private void OnSend(object sender, RoutedEventArgs e) => _ = SendAsync();

    private void OnStop(object sender, RoutedEventArgs e) => _inFlight?.Cancel();

    private async Task SendAsync()
    {
        if (_inFlight is not null || _assistant is null || _session is null) return;

        var text = Composer.Text.Trim();
        if (text.Length == 0 && _pending.Count == 0) return;

        var attachments = _pending.ToList();

        Composer.Clear();
        _pending.Clear();
        SetBusy(true);

        _inFlight = new CancellationTokenSource();
        var pending = new System.Text.StringBuilder();

        try
        {
            await foreach (var chunk in _assistant.SendAsync(_session, text, attachments, _inFlight.Token))
            {
                pending.Append(chunk);
                Render(pending.ToString());
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
            RefreshSessionList();
            Render();
        }
    }

    private void SetBusy(bool busy)
    {
        SendButton.IsEnabled = !busy;
        Composer.IsEnabled = !busy;
        StopButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;

        if (busy) StatusText.Text = "Thinking...";
        else UpdateStatus();
    }

    #endregion

    private void Render(string? pendingText = null)
    {
        if (!_webViewReady || _assistant is null || _session is null) return;

        Transcript.NavigateToString(
            _renderer.RenderDocument(_session, _assistant.Options.Name, pendingText));
    }

    private void OnAssistantLayout(object? sender, UiDocument document) =>
        Dispatcher.Invoke(() => LayoutProduced?.Invoke(this, document));

    private void OnHide(object sender, RoutedEventArgs e) => HideRequested?.Invoke(this, EventArgs.Empty);

    private void OnShowPrompts(object sender, RoutedEventArgs e)
    {
        var picked = PromptGalleryWindow.Pick(Window.GetWindow(this)!);
        if (picked is null) return;

        Composer.Text = picked.Prompt;
        Composer.Focus();
        Composer.CaretIndex = Composer.Text.Length;
    }
}
