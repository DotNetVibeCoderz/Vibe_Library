using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Search;
using Lvgl.Ui;

namespace Lvgl.Designer.Editor;

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
/// <para>
/// Two of the three views are generated output and are therefore read-only - editing them would
/// be silently discarded on the next regeneration, which is worse than not offering it. The layout
/// JSON is the one editable view, and <b>Apply to design</b> parses it back onto the canvas, so
/// the round trip between hand-editing and the visual designer is closed.
/// </para>
/// <para>
/// AvalonEdit supplies line numbers, the search panel, and undo/redo; the toolbar simply drives
/// them so the commands are discoverable rather than keyboard-only.
/// </para>
/// </remarks>
public partial class CodeEditorPanel : UserControl
{
    private UiDocument? _document;
    private bool _suppressViewChange;

    public CodeEditorPanel()
    {
        InitializeComponent();

        ViewBox.ItemsSource = new[]
        {
            new ViewOption(CodeView.LayoutJson, "Layout (JSON)"),
            new ViewOption(CodeView.GeneratedCSharp, "Generated C#"),
            new ViewOption(CodeView.EventHandlers, "Event handlers"),
        };

        ViewBox.DisplayMemberPath = nameof(ViewOption.Label);
        ViewBox.SelectedIndex = 0;

        // The search panel is what gives Ctrl+F, F3 and replace; it has to be installed on the
        // editor rather than being on by default.
        SearchPanel.Install(Editor);

        Editor.TextArea.Caret.PositionChanged += (_, _) => UpdateCaret();
        Editor.TextChanged += (_, _) => OnTextChanged();

        InstallCommands();
        UpdateCaret();
    }

    /// <summary>Raised when the user applies edited JSON back to the designer.</summary>
    public event EventHandler<UiDocument>? ApplyRequested;

    /// <summary>The namespace used when generating C#.</summary>
    public string? GenerationNamespace { get; set; }

    /// <summary>Which view is showing.</summary>
    public CodeView View => ViewBox.SelectedItem is ViewOption option ? option.Value : CodeView.LayoutJson;

    /// <summary>True when the editable view has unsaved edits that have not been applied.</summary>
    public bool HasPendingEdits { get; private set; }

    /// <summary>
    /// Loads a document into the editor and renders the current view.
    /// </summary>
    public void Show(UiDocument document)
    {
        _document = document;
        Refresh();
    }

    /// <summary>Re-renders the current view from the document, discarding unapplied edits.</summary>
    public void Refresh()
    {
        if (_document is null) return;

        Editor.Text = Render(_document, View);

        // A fresh render is the baseline, so the undo stack should not let the user "undo" back
        // into the previous document's text.
        Editor.Document.UndoStack.ClearAll();

        Editor.SyntaxHighlighting = HighlightingFor(View);
        Editor.IsReadOnly = View != CodeView.LayoutJson;
        ApplyButton.IsEnabled = View == CodeView.LayoutJson;

        HasPendingEdits = false;
        EditorStatus.Text = View == CodeView.LayoutJson
            ? "Editable - Apply to design (Ctrl+Enter) to put changes on the canvas"
            : "Read-only - generated from the layout";
    }

    private static string Render(UiDocument document, CodeView view)
    {
        try
        {
            return view switch
            {
                CodeView.LayoutJson => UiJson.ToJson(document),
                CodeView.GeneratedCSharp => new CSharpUiGenerator().Generate(document),
                CodeView.EventHandlers => new Lvgl.Assistant.Plugins.LvglDesignPlugin()
                    .GenerateEventHandlers(UiJson.ToJson(document), string.Empty, document.Name),
                _ => string.Empty,
            };
        }
        catch (InvalidOperationException ex)
        {
            // The generator refuses documents with duplicate or invalid names; showing the reason
            // is more useful than an empty pane.
            return "// The layout cannot be generated yet:" + Environment.NewLine +
                   "// " + ex.Message.ReplaceLineEndings(Environment.NewLine + "// ");
        }
    }

    private static IHighlightingDefinition? HighlightingFor(CodeView view) => view switch
    {
        CodeView.LayoutJson => HighlightingManager.Instance.GetDefinition("JavaScript"),
        _ => HighlightingManager.Instance.GetDefinition("C#"),
    };

    private void OnViewChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressViewChange) return;

        if (HasPendingEdits && View != CodeView.LayoutJson)
        {
            var confirm = MessageBox.Show(
                Window.GetWindow(this),
                "The layout JSON has edits you have not applied. Switching view will discard them.",
                "Discard edits?",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes)
            {
                _suppressViewChange = true;
                ViewBox.SelectedIndex = 0;
                _suppressViewChange = false;
                return;
            }
        }

        Refresh();
    }

    private void OnTextChanged()
    {
        if (View != CodeView.LayoutJson) return;

        HasPendingEdits = true;
        EditorStatus.Text = "Edited - Apply to design (Ctrl+Enter) to put changes on the canvas";
    }

    private void UpdateCaret()
    {
        var caret = Editor.TextArea.Caret;
        var selection = Editor.SelectionLength;

        CaretText.Text = selection > 0
            ? $"Ln {caret.Line}, Col {caret.Column}   ({selection} selected)"
            : $"Ln {caret.Line}, Col {caret.Column}";
    }

    #region Commands

    /// <summary>
    /// Binds the shortcuts the toolbar buttons mirror. AvalonEdit already handles the standard
    /// editing keys; these are the ones it does not provide.
    /// </summary>
    private void InstallCommands()
    {
        InputBindings.Add(new KeyBinding(
            new RelayCommand(_ => GoToLine()), Key.G, ModifierKeys.Control));

        InputBindings.Add(new KeyBinding(
            new RelayCommand(_ => Apply()), Key.Enter, ModifierKeys.Control));

        Editor.TextArea.InputBindings.Add(new KeyBinding(
            new RelayCommand(_ => GoToLine()), Key.G, ModifierKeys.Control));

        Editor.TextArea.InputBindings.Add(new KeyBinding(
            new RelayCommand(_ => Apply()), Key.Enter, ModifierKeys.Control));

        Editor.ContextMenu = BuildContextMenu();
    }

    private ContextMenu BuildContextMenu()
    {
        var menu = new ContextMenu();

        void Add(string header, string gesture, Action action)
        {
            var item = new MenuItem { Header = header, InputGestureText = gesture };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }

        Add("Undo", "Ctrl+Z", () => Editor.Undo());
        Add("Redo", "Ctrl+Y", () => Editor.Redo());
        menu.Items.Add(new Separator());
        Add("Cut", "Ctrl+X", () => Editor.Cut());
        Add("Copy", "Ctrl+C", () => Editor.Copy());
        Add("Paste", "Ctrl+V", () => Editor.Paste());
        menu.Items.Add(new Separator());
        Add("Select all", "Ctrl+A", () => Editor.SelectAll());
        Add("Find and replace", "Ctrl+F", OpenSearch);
        Add("Go to line", "Ctrl+G", GoToLine);

        return menu;
    }

    private void OnUndo(object sender, RoutedEventArgs e) => Editor.Undo();

    private void OnRedo(object sender, RoutedEventArgs e) => Editor.Redo();

    private void OnCut(object sender, RoutedEventArgs e) => Editor.Cut();

    private void OnCopy(object sender, RoutedEventArgs e) => Editor.Copy();

    private void OnPaste(object sender, RoutedEventArgs e) => Editor.Paste();

    private void OnCopyAll(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(Editor.Text);
            EditorStatus.Text = "Copied the whole document to the clipboard.";
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // Another process can hold the clipboard open; not worth failing over.
            EditorStatus.Text = "The clipboard was busy - try again.";
        }
    }

    private void OnFind(object sender, RoutedEventArgs e) => OpenSearch();

    private void OpenSearch()
    {
        Editor.Focus();
        SearchPanel.Install(Editor).Open();
    }

    private void OnGoToLine(object sender, RoutedEventArgs e) => GoToLine();

    private void GoToLine()
    {
        var total = Editor.Document.LineCount;
        var line = GoToLineDialog.Ask(Window.GetWindow(this)!, Editor.TextArea.Caret.Line, total);
        if (line is null) return;

        var target = Math.Clamp(line.Value, 1, total);

        Editor.ScrollToLine(target);
        Editor.TextArea.Caret.Line = target;
        Editor.TextArea.Caret.Column = 1;
        Editor.Focus();
    }

    // The toolbar is declared before the editor in XAML, so an IsChecked="True" default raises
    // these during InitializeComponent, while Editor is still null.
    private void OnLineNumbersChanged(object sender, RoutedEventArgs e)
    {
        if (Editor is null) return;
        Editor.ShowLineNumbers = LineNumbersToggle.IsChecked == true;
    }

    private void OnWordWrapChanged(object sender, RoutedEventArgs e)
    {
        if (Editor is null) return;
        Editor.WordWrap = WordWrapToggle.IsChecked == true;
    }

    private void OnApply(object sender, RoutedEventArgs e) => Apply();

    /// <summary>Parses the edited JSON and hands it back to the designer.</summary>
    private void Apply()
    {
        if (View != CodeView.LayoutJson) return;

        UiDocument parsed;

        try
        {
            parsed = UiJson.Parse(Editor.Text);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or ArgumentException)
        {
            EditorStatus.Text = "Not applied - " + ex.Message;
            return;
        }

        var problems = parsed.Validate();
        if (problems.Count > 0)
        {
            EditorStatus.Text = "Not applied - " + string.Join("; ", problems);
            return;
        }

        _document = parsed;
        HasPendingEdits = false;

        var warnings = parsed.Warnings();
        EditorStatus.Text = warnings.Count == 0
            ? "Applied to the design."
            : "Applied, with warnings: " + string.Join("; ", warnings);

        ApplyRequested?.Invoke(this, parsed);
    }

    #endregion

    private sealed record ViewOption(CodeView Value, string Label);

    /// <summary>Minimal ICommand so key bindings can call a method directly.</summary>
    private sealed class RelayCommand(Action<object?> execute) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => execute(parameter);
    }
}
