using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Search;
using AvaloniaEdit.TextMate;
using Lvgl.Assistant.Plugins;
using Lvgl.Ui;
using TextMateSharp.Grammars;

namespace Lvgl.Designer.Views;

/// <summary>What the code view is showing.</summary>
public enum CodeView
{
    /// <summary>The layout document itself. Editable, and applies back to the canvas.</summary>
    LayoutJson,

    /// <summary>The generated builder class. Read-only, because editing it would be overwritten.</summary>
    GeneratedCSharp,

    /// <summary>The hand-written half, with a handler stub per interactive widget.</summary>
    EventHandlers,
}

/// <summary>
/// The designer's code mode: a real editor over the document, next to the visual canvas.
/// </summary>
/// <remarks>
/// Two of the three views are generated output and are read-only - editing them would be silently
/// discarded on the next regeneration, which is worse than not offering it. The layout JSON is the
/// one editable view, and <b>Apply to design</b> parses it back onto the canvas, so the round trip
/// between hand-editing and the visual designer is closed.
/// </remarks>
public sealed class CodePanel : UserControl
{
    private readonly TextEditor _editor;
    private readonly TextMate.Installation _textMate;
    private readonly RegistryOptions _registry;
    private readonly TextBlock _status = new() { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _caret = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _apply;
    private readonly RadioButton[] _views;

    private UiDocument? _document;
    private bool _loading;

    public CodePanel()
    {
        _editor = new TextEditor
        {
            ShowLineNumbers = true,
            FontFamily = Resource<FontFamily>("MonoFont") ?? FontFamily.Default,
            FontSize = 12.5,
            Background = Resource<IBrush>("Mask"),
            Foreground = Resource<IBrush>("Silk"),
            LineNumbersForeground = Resource<IBrush>("SilkFaint"),
            Padding = new Thickness(8, 6),
            WordWrap = false,
        };

        _registry = new RegistryOptions(ThemeName.DarkPlus);
        _textMate = _editor.InstallTextMate(_registry);
        SearchPanel.Install(_editor);

        _editor.TextChanged += (_, _) =>
        {
            if (_loading || View != CodeView.LayoutJson) return;
            HasPendingEdits = true;
            _status.Text = "Edited - Apply to design (Ctrl+Enter) puts the changes on the canvas.";
        };
        _editor.TextArea.Caret.PositionChanged += (_, _) => UpdateCaret();

        _views =
        [
            Segment("Layout JSON", CodeView.LayoutJson, isChecked: true),
            Segment("Generated C#", CodeView.GeneratedCSharp, isChecked: false),
            Segment("Event handlers", CodeView.EventHandlers, isChecked: false),
        ];

        _apply = new Button { Content = "Apply to design" };
        _apply.Classes.Add("primary");
        _apply.Click += (_, _) => Apply();
        ToolTip.SetTip(_apply, "Ctrl+Enter");

        var copy = new Button { Content = "Copy all" };
        copy.Classes.Add("ghost");
        copy.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(_editor.Text);
                _status.Text = "Copied the whole view to the clipboard.";
            }
        };

        var find = new Button { Content = "Find" };
        find.Classes.Add("ghost");
        find.Click += (_, _) =>
        {
            _editor.Focus();
            SearchPanel.Install(_editor).Open();
        };
        ToolTip.SetTip(find, "Ctrl+F");

        var switcher = new Border
        {
            Background = Resource<IBrush>("Mask"),
            BorderBrush = Resource<IBrush>("Trace"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(2),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { _views[0], _views[1], _views[2] } },
        };

        var toolbar = new DockPanel { Margin = new Thickness(12, 8) };
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { find, copy, _apply } };
        DockPanel.SetDock(right, Dock.Right);
        toolbar.Children.Add(right);
        toolbar.Children.Add(switcher);

        _caret.Classes.Add("mono");
        _caret.Classes.Add("faint");
        _status.Classes.Add("dim");
        _status.Classes.Add("small");

        var footer = new DockPanel { Margin = new Thickness(12, 5) };
        DockPanel.SetDock(_caret, Dock.Right);
        footer.Children.Add(_caret);
        footer.Children.Add(_status);

        var top = new Border { Child = toolbar, BorderBrush = Resource<IBrush>("Trace"), BorderThickness = new Thickness(0, 0, 0, 1), Background = Resource<IBrush>("Board") };
        var bottom = new Border { Child = footer, BorderBrush = Resource<IBrush>("Trace"), BorderThickness = new Thickness(0, 1, 0, 0), Background = Resource<IBrush>("Board") };

        var root = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(top);
        root.Children.Add(bottom);
        root.Children.Add(_editor);
        Content = root;

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                Apply();
                e.Handled = true;
            }
        };
    }

    private RadioButton Segment(string text, CodeView view, bool isChecked)
    {
        var button = new RadioButton { Content = text, GroupName = "code-view", IsChecked = isChecked, Tag = view };
        button.Classes.Add("segment");
        button.IsCheckedChanged += (_, _) =>
        {
            if (button.IsChecked == true && !_loading) _ = SwitchViewAsync();
        };
        return button;
    }

    private static T? Resource<T>(string key) where T : class =>
        Application.Current!.TryFindResource(key, out var value) ? value as T : null;

    /// <summary>Raised when the user applies edited JSON back to the designer.</summary>
    public event EventHandler<UiDocument>? ApplyRequested;

    /// <summary>Which view is showing.</summary>
    public CodeView View => _views.FirstOrDefault(v => v.IsChecked == true)?.Tag is CodeView view ? view : CodeView.LayoutJson;

    /// <summary>True when the JSON view has edits that have not been applied.</summary>
    public bool HasPendingEdits { get; private set; }

    /// <summary>The editor, for tests and focus handling.</summary>
    internal TextEditor Editor => _editor;

    /// <summary>Loads a document and renders the current view.</summary>
    public void Show(UiDocument document)
    {
        _document = document;
        Refresh();
    }

    /// <summary>Re-renders the current view from the document, discarding unapplied edits.</summary>
    public void Refresh()
    {
        if (_document is null) return;

        _loading = true;
        _editor.Text = Render(_document, View);
        _editor.Document.UndoStack.ClearAll();
        _editor.IsReadOnly = View != CodeView.LayoutJson;
        _loading = false;

        var language = View == CodeView.LayoutJson ? ".json" : ".cs";
        _textMate.SetGrammar(_registry.GetScopeByLanguageId(_registry.GetLanguageByExtension(language).Id));

        _apply.IsVisible = View == CodeView.LayoutJson;
        HasPendingEdits = false;
        _status.Text = View == CodeView.LayoutJson
            ? "Editable. Apply to design (Ctrl+Enter) puts your changes on the canvas."
            : "Read-only: generated from the layout on every change.";
        UpdateCaret();
    }

    private async Task SwitchViewAsync()
    {
        if (HasPendingEdits && View != CodeView.LayoutJson && TopLevel.GetTopLevel(this) is Window window)
        {
            var discard = await Dialogs.ConfirmAsync(window, "Unapplied edits",
                "The layout JSON has edits that are not on the canvas yet. Switching view discards them.", "Discard edits");

            if (!discard)
            {
                _loading = true;
                _views[0].IsChecked = true;
                _loading = false;
                return;
            }
        }

        Refresh();
    }

    internal static string Render(UiDocument document, CodeView view)
    {
        try
        {
            return view switch
            {
                CodeView.LayoutJson => UiJson.ToJson(document),
                CodeView.GeneratedCSharp => new CSharpUiGenerator().Generate(document),
                CodeView.EventHandlers => new LvglDesignPlugin().GenerateEventHandlers(UiJson.ToJson(document), string.Empty, document.Name),
                _ => string.Empty,
            };
        }
        catch (InvalidOperationException ex)
        {
            // The generator refuses documents with problems; the reason is more useful than an empty pane.
            return "// The layout cannot be generated yet:" + Environment.NewLine +
                   "// " + ex.Message.ReplaceLineEndings(Environment.NewLine + "// ");
        }
    }

    private void UpdateCaret()
    {
        var caret = _editor.TextArea.Caret;
        var selection = _editor.SelectionLength;
        _caret.Text = selection > 0 ? $"Ln {caret.Line}, Col {caret.Column}  ({selection} selected)" : $"Ln {caret.Line}, Col {caret.Column}";
    }

    /// <summary>Parses the edited JSON and hands it back to the designer.</summary>
    internal void Apply()
    {
        if (View != CodeView.LayoutJson) return;

        UiDocument parsed;
        try
        {
            parsed = UiJson.Parse(_editor.Text);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or ArgumentException)
        {
            _status.Text = "Not applied: " + ex.Message;
            return;
        }

        var problems = parsed.Validate();
        if (problems.Count > 0)
        {
            _status.Text = "Not applied: " + string.Join("; ", problems);
            return;
        }

        _document = parsed;
        HasPendingEdits = false;

        var warnings = parsed.Warnings();
        _status.Text = warnings.Count == 0 ? "Applied to the design." : "Applied, with warnings: " + string.Join("; ", warnings);

        ApplyRequested?.Invoke(this, parsed);
    }
}
