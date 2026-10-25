using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Lvgl.Designer.Assistant;
using Lvgl.Designer.Core;
using Lvgl.Designer.Views;
using Lvgl.Ui;

namespace Lvgl.Designer;

public sealed partial class MainWindow : Window
{
    private readonly DesignerSession _session = new();
    private readonly LvglPreview _preview = new();
    private readonly DispatcherTimer _rebuild;
    private bool _suppressPreset;
    private bool _closingConfirmed;
    private GridLength _assistantWidth = new(420);

    /// <summary>Parts grouped the way people look for them: what holds, what you touch, what shows.</summary>
    private static readonly (string Group, UiWidgetType[] Types)[] PartGroups =
    [
        ("Layout", [UiWidgetType.Panel, UiWidgetType.TabView, UiWidgetType.Tab, UiWidgetType.TileView, UiWidgetType.Tile, UiWidgetType.List]),
        ("Input", [UiWidgetType.Button, UiWidgetType.Slider, UiWidgetType.Switch, UiWidgetType.Checkbox, UiWidgetType.Dropdown,
            UiWidgetType.Roller, UiWidgetType.TextArea, UiWidgetType.Spinbox, UiWidgetType.Keyboard]),
        ("Display", [UiWidgetType.Label, UiWidgetType.Image, UiWidgetType.Bar, UiWidgetType.Arc, UiWidgetType.Led,
            UiWidgetType.Spinner, UiWidgetType.Scale, UiWidgetType.Chart, UiWidgetType.Table]),
    ];

    public MainWindow()
    {
        InitializeComponent();

        // Every edit lands here; rebuilding LVGL's tree once per burst keeps dragging and typing
        // smooth while still re-rendering within a frame or two.
        _rebuild = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(16) };
        _rebuild.Tick += (_, _) =>
        {
            _rebuild.Stop();
            RebuildPreview();
        };

        Canvas.Attach(_session, _preview);
        Canvas.NodePicked += (_, node) => _session.Selection = node;
        Canvas.ZoomChanged += (_, _) => ZoomText.Text = $"{Canvas.Zoom * 100:0}%";

        Outline.Attach(_session);
        Inspector.Attach(_session);
        Inspector.PickAsset = PickAssetAsync;

        CodeSurface.ApplyRequested += (_, document) =>
        {
            _session.Replace(document);
            SetStatus($"Applied '{document.Name}' from the code view. Undo puts the previous layout back.");
        };

        Assistant.LayoutProduced += OnAssistantLayout;
        Assistant.HideRequested += (_, _) => SetAssistantVisible(false);

        _session.Changed += OnSessionChanged;
        _session.DocumentStateChanged += (_, _) => UpdateDocumentState();
        _session.SelectionChanged += (_, _) => UpdateSelectionStatus();

        _preview.SurfaceChanged += (_, _) => UpdatePreviewState();

        BuildParts();
        PresetBox.ItemsSource = ScreenPresets.All.Cast<object>().Append("Custom size").ToList();

        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
        Opened += (_, _) => OnOpened();
        Closing += OnClosing;
    }

    private void OnOpened()
    {
        _session.Load(DesignerSession.StarterDocument(), path: null);
        _preview.Start();
        Canvas.FitToView();
        UpdateDocumentState();
        SetStatus("Ready. Click a part to add it; drag on the board to move, the copper pads to resize.");
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closingConfirmed) return;

        if (_session.IsDirty)
        {
            e.Cancel = true;
            if (!await ConfirmDiscardAsync("close the designer")) return;
            _closingConfirmed = true;
            Close();
            return;
        }

        Shutdown();
    }

    private void Shutdown()
    {
        _preview.Dispose();
        Assistant.Shutdown();

        // The panel's service owns the attachment HTTP host, which must be shut down explicitly or
        // the process lingers with a bound port.
        ChatPanel.DisposeShared();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (_closingConfirmed) Shutdown();
    }

    #region Parts

    private void BuildParts()
    {
        foreach (var (group, types) in PartGroups)
        {
            var head = new TextBlock { Text = group.ToUpperInvariant(), Margin = new Thickness(6, 10, 0, 4) };
            head.Classes.Add("head");
            head.Classes.Add("small");
            PartsList.Children.Add(head);

            foreach (var type in types) PartsList.Children.Add(PartButton(type));
        }
    }

    private Button PartButton(UiWidgetType type)
    {
        var designator = new TextBlock { Text = Designators.Prefix(type), Width = 40, VerticalAlignment = VerticalAlignment.Center };
        designator.Classes.Add("designator");

        var name = new TextBlock { Text = type.ToString(), FontWeight = FontWeight.Medium };
        var description = new TextBlock { Text = Designators.Describe(type), TextTrimming = TextTrimming.CharacterEllipsis };
        description.Classes.Add("faint");
        description.Classes.Add("small");

        var row = new DockPanel();
        DockPanel.SetDock(designator, Dock.Left);
        row.Children.Add(designator);
        row.Children.Add(new StackPanel { Children = { name, description } });

        var button = new Button
        {
            Content = row,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(6, 4),
        };
        button.Classes.Add("ghost");
        button.Click += (_, _) => AddPart(type);
        return button;
    }

    /// <summary>Adds a widget of <paramref name="type"/> where the selection says it should go.</summary>
    internal void AddPart(UiWidgetType type)
    {
        try
        {
            var node = _session.Add(type);
            var parent = _session.Document.ParentOf(node);
            SetStatus(parent is null
                ? $"Added {Designators.For(_session.Document, node)} to the screen."
                : $"Added {Designators.For(_session.Document, node)} inside {parent.Name ?? parent.Type.ToString()}.");
        }
        catch (InvalidOperationException ex)
        {
            SetStatus(ex.Message);
        }
    }

    #endregion

    #region Session events

    private void OnSessionChanged(object? sender, DesignChange change)
    {
        if (change.HasFlag(DesignChange.Document) || change.HasFlag(DesignChange.Screen))
        {
            SyncScreenSize();
        }

        if (IsCodeMode) CodeSurface.Show(_session.Document);

        UndoButton.IsEnabled = _session.CanUndo;
        RedoButton.IsEnabled = _session.CanRedo;

        _rebuild.Stop();
        _rebuild.Start();
    }

    private void RebuildPreview()
    {
        _preview.AssetDirectory = _session.DocumentFolder ?? Environment.CurrentDirectory;
        _preview.Show(_session.Document);

        UpdatePreviewState();
        UpdateProblems();
        Canvas.InvalidateVisual();
    }

    private void UpdatePreviewState()
    {
        var unavailable = _preview.UnavailableReason;
        PreviewUnavailablePanel.IsVisible = unavailable is not null && !IsCodeMode;
        PreviewUnavailableText.Text = unavailable ?? string.Empty;

        LiveDot.Fill = Brush(unavailable is null ? "Live" : "Fault");
        LiveText.Text = _preview.LvglVersion is { } version ? $"LVGL {version.ToString(3)}" : "LVGL offline";
        ToolTip.SetTip(LiveText, unavailable ?? "The preview is LVGL itself, rendering off-screen.");
    }

    private void UpdateProblems()
    {
        var problems = _session.Document.Validate().Count;
        var warnings = _session.Document.Warnings().Count;
        var diagnostics = _preview.Diagnostics.Count;
        var total = problems + warnings + diagnostics;

        ProblemsButton.IsVisible = total > 0;
        ProblemsText.Text = total == 1 ? "1 thing to check" : $"{total} things to check";
    }

    private void UpdateDocumentState()
    {
        DocumentTitle.Text = _session.Path is null ? "(unsaved layout)" : System.IO.Path.GetFileName(_session.Path);
        DirtyDot.IsVisible = _session.IsDirty;
        Title = $"{(_session.IsDirty ? "• " : string.Empty)}{DocumentTitle.Text} - LVGL.Net Designer";
        UndoButton.IsEnabled = _session.CanUndo;
        RedoButton.IsEnabled = _session.CanRedo;
    }

    private void UpdateSelectionStatus()
    {
        if (_session.Selection is not { } node) return;
        SetStatus($"{Designators.For(_session.Document, node)}  {node.Name ?? "(unnamed)"}  ·  arrows nudge, Shift for 10 px, Del deletes, Ctrl+D duplicates");
    }

    private void SetStatus(string message) => StatusText.Text = message;

    private static IBrush Brush(string key) =>
        Application.Current!.TryFindResource(key, out var value) && value is IBrush brush ? brush : Brushes.Gray;

    #endregion

    #region Screen size

    private void SyncScreenSize()
    {
        var document = _session.Document;
        ScreenWidthBox.Text = document.Width.ToString(CultureInfo.InvariantCulture);
        ScreenHeightBox.Text = document.Height.ToString(CultureInfo.InvariantCulture);

        _suppressPreset = true;
        PresetBox.SelectedItem = (object?)ScreenPresets.Match(document.Width, document.Height) ?? "Custom size";
        _suppressPreset = false;
    }

    private void OnPresetChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressPreset || PresetBox.SelectedItem is not ScreenPreset preset) return;

        _session.SetScreenSize(preset.Width, preset.Height);
        Canvas.FitToView();
        SetStatus($"Screen set to {preset}.");
    }

    private void OnScreenSizeCommitted(object? sender, RoutedEventArgs e) => CommitScreenSize();

    private void OnScreenSizeKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) CommitScreenSize();
    }

    private void CommitScreenSize()
    {
        if (!int.TryParse(ScreenWidthBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var width) ||
            !int.TryParse(ScreenHeightBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var height))
        {
            SyncScreenSize();
            return;
        }

        var before = (_session.Document.Width, _session.Document.Height);
        _session.SetScreenSize(width, height);
        if (before != (_session.Document.Width, _session.Document.Height)) Canvas.FitToView();
        SyncScreenSize();
    }

    #endregion

    #region Files

    private async void OnNew(object? sender, RoutedEventArgs e)
    {
        if (!await ConfirmDiscardAsync("start a new layout")) return;
        _session.Load(DesignerSession.StarterDocument(), path: null);
        Canvas.FitToView();
        SetStatus("New layout.");
    }

    private async void OnOpen(object? sender, RoutedEventArgs e) => await OpenAsync();

    private async Task OpenAsync()
    {
        if (!await ConfirmDiscardAsync("open another layout")) return;

        var path = await Dialogs.OpenFileAsync(this, "Open layout", Dialogs.Layouts);
        if (path is null) return;

        try
        {
            _session.Load(UiJson.Load(path), path);
            Canvas.FitToView();
            SetStatus($"Opened {path}.");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException or ArgumentException)
        {
            await Dialogs.InformAsync(this, "Could not open the layout", ex.Message);
        }
    }

    private async void OnSave(object? sender, RoutedEventArgs e) => await SaveAsync(saveAs: false);

    internal async Task<bool> SaveAsync(bool saveAs)
    {
        var path = _session.Path;

        if (path is null || saveAs)
        {
            path = await Dialogs.SaveFileAsync(this, "Save layout", _session.Document.Name + UiJson.FileExtension, ".lvgl.json", Dialogs.Layouts);
            if (path is null) return false;
        }

        try
        {
            _session.Save(path);
            SetStatus($"Saved {path}.");
            RebuildPreview(); // assets now resolve against the saved folder
            return true;
        }
        catch (IOException ex)
        {
            await Dialogs.InformAsync(this, "Could not save", ex.Message);
            return false;
        }
    }

    private async void OnExport(object? sender, RoutedEventArgs e) => await ExportAsync();

    private async Task ExportAsync()
    {
        var problems = _session.Document.Validate();
        if (problems.Count > 0)
        {
            await Dialogs.InformAsync(this, "Fix these before exporting", string.Join(Environment.NewLine, problems.Select(p => "• " + p)));
            return;
        }

        var path = await Dialogs.SaveFileAsync(this, "Export generated C#", _session.Document.Name + ".Designer.cs", ".cs", Dialogs.CSharp);
        if (path is null) return;

        var @namespace = await Dialogs.PromptAsync(this, "Export C#", "Namespace for the generated class. Leave empty for none.", "MyApp.Ui", "Export");
        if (@namespace is null) return;

        try
        {
            var copied = _session.ExportCSharp(path, @namespace);
            SetStatus(copied.Count == 0 ? $"Exported {path}." : $"Exported {path} and {copied.Count} asset file(s) next to it.");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            await Dialogs.InformAsync(this, "Could not export", ex.Message);
        }
    }

    private async Task<bool> ConfirmDiscardAsync(string action)
    {
        if (!_session.IsDirty) return true;
        return await Dialogs.ConfirmAsync(this, "Unsaved changes", $"The layout has unsaved changes. Discard them and {action}?", "Discard changes", destructive: true);
    }

    /// <summary>Imports a file for an inspector field: requires a saved layout, since assets live beside it.</summary>
    private async Task<string?> PickAssetAsync(string label, Avalonia.Platform.Storage.FilePickerFileType type)
    {
        if (_session.Path is null)
        {
            var save = await Dialogs.ConfirmAsync(this, "Save the layout first",
                "Images and fonts are copied into an assets folder next to the layout file, so it needs a place on disk.", "Save layout");
            if (!save || !await SaveAsync(saveAs: false)) return null;
        }

        var source = await Dialogs.OpenFileAsync(this, $"Choose a {label.ToLowerInvariant()}", type);
        if (source is null) return null;

        try
        {
            var relative = _session.ImportAsset(source);
            SetStatus($"Imported {relative}.");
            return relative;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            await Dialogs.InformAsync(this, "Could not import", ex.Message);
            return null;
        }
    }

    #endregion

    #region Editing commands

    private void OnUndo(object? sender, RoutedEventArgs e) => Undo();

    private void OnRedo(object? sender, RoutedEventArgs e) => Redo();

    private void Undo()
    {
        if (!_session.CanUndo) return;
        _session.Undo();
        SetStatus("Undone.");
    }

    private void Redo()
    {
        if (!_session.CanRedo) return;
        _session.Redo();
        SetStatus("Redone.");
    }

    private void OnDelete(object? sender, RoutedEventArgs e) => DeleteSelection();

    private void DeleteSelection()
    {
        if (_session.Selection is not { } node) return;
        var designator = Designators.For(_session.Document, node);
        _session.Delete(node);
        SetStatus($"Deleted {designator}. Undo brings it back.");
    }

    private void OnDuplicate(object? sender, RoutedEventArgs e) => DuplicateSelection();

    private void DuplicateSelection()
    {
        if (_session.Selection is not { } node) return;
        var copy = _session.Duplicate(node);
        SetStatus($"Duplicated as {Designators.For(_session.Document, copy)}.");
    }

    private void OnMoveUp(object? sender, RoutedEventArgs e)
    {
        if (_session.Selection is { } node) _session.MoveInOrder(node, -1);
    }

    private void OnMoveDown(object? sender, RoutedEventArgs e)
    {
        if (_session.Selection is { } node) _session.MoveInOrder(node, +1);
    }

    private async void OnShowProblems(object? sender, RoutedEventArgs e)
    {
        var lines = _session.Document.Validate().Select(p => "✕ " + p)
            .Concat(_session.Document.Warnings().Select(w => "! " + w))
            .Concat(_preview.Diagnostics.Select(d => "· " + d))
            .ToList();

        await Dialogs.InformAsync(this, "Things to check", lines.Count == 0 ? "Nothing - the layout is clean." : string.Join(Environment.NewLine + Environment.NewLine, lines));
    }

    private void OnSnapToggled(object? sender, RoutedEventArgs e) => Canvas.SnapToGrid = SnapToggle.IsChecked == true;

    private void OnFit(object? sender, RoutedEventArgs e) => Canvas.FitToView();

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var typing = FocusManager?.GetFocusedElement() is TextBox or AvaloniaEdit.Editing.TextArea;

        if (ctrl)
        {
            switch (e.Key)
            {
                case Key.N: OnNew(null, new RoutedEventArgs()); break;
                case Key.O: _ = OpenAsync(); break;
                case Key.S: _ = SaveAsync(saveAs: shift); break;
                case Key.E: _ = ExportAsync(); break;
                case Key.J: SetAssistantVisible(Assistant.IsVisible == false); break;
                case Key.D0: Canvas.FitToView(); break;
                case Key.Z when !typing: if (shift) Redo(); else Undo(); break;
                case Key.Y when !typing: Redo(); break;
                case Key.D when !typing: DuplicateSelection(); break;
                default: return;
            }

            e.Handled = true;
            return;
        }

        if (typing || IsCodeMode) return;

        var step = shift ? 10 : 1;
        switch (e.Key)
        {
            case Key.Delete or Key.Back:
                DeleteSelection();
                break;
            case Key.Escape:
                _session.Selection = null;
                break;
            case Key.Left when _session.Selection is { } left:
                _session.MoveBy(left, -step, 0, "nudge");
                break;
            case Key.Right when _session.Selection is { } right:
                _session.MoveBy(right, step, 0, "nudge");
                break;
            case Key.Up when _session.Selection is { } up:
                _session.MoveBy(up, 0, -step, "nudge");
                break;
            case Key.Down when _session.Selection is { } down:
                _session.MoveBy(down, 0, step, "nudge");
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    #endregion

    #region Modes and panels

    // Read from the Design button, whose change event drives the switch: when it fires, the Code
    // button of the same group may not have been updated yet.
    private bool IsCodeMode => DesignModeButton.IsChecked != true;

    // Named ColumnDefinitions get no generated fields in Avalonia, so the assistant's two columns
    // are addressed by position: splitter, then panel, at the right edge of the workspace.
    private ColumnDefinition AssistantSplitterColumn => Workspace.ColumnDefinitions[5];

    private ColumnDefinition AssistantColumn => Workspace.ColumnDefinitions[6];

    private void OnModeChanged(object? sender, RoutedEventArgs e)
    {
        // Fires during InitializeComponent before the named parts exist.
        if (CodeSurface is null || Canvas is null) return;

        if (IsCodeMode)
        {
            CodeSurface.Show(_session.Document);
            CodeSurface.IsVisible = true;
            Canvas.IsVisible = false;
            PreviewUnavailablePanel.IsVisible = false;
            _preview.Stop();
            SetStatus("Code mode: edit the layout JSON and apply it, or read the generated C#.");
        }
        else
        {
            CodeSurface.IsVisible = false;
            Canvas.IsVisible = true;
            _preview.Start();
            RebuildPreview();
            SetStatus("Design mode.");
        }
    }

    private void OnAssistantToggled(object? sender, RoutedEventArgs e) => SetAssistantVisible(AssistantToggle.IsChecked == true);

    private void SetAssistantVisible(bool visible)
    {
        AssistantToggle.IsChecked = visible;

        if (visible)
        {
            AssistantColumn.Width = _assistantWidth;
            AssistantColumn.MinWidth = 320;
            AssistantSplitterColumn.Width = new GridLength(1);
            Assistant.IsVisible = true;
            AssistantSplitter.IsVisible = true;

            // Started lazily so a session that never opens the assistant pays nothing.
            Assistant.EnsureStarted();
            Assistant.FocusComposer();
        }
        else
        {
            if (AssistantColumn.Width.Value > 0) _assistantWidth = AssistantColumn.Width;
            AssistantColumn.MinWidth = 0;
            AssistantColumn.Width = new GridLength(0);
            AssistantSplitterColumn.Width = new GridLength(0);
            Assistant.IsVisible = false;
            AssistantSplitter.IsVisible = false;
        }
    }

    /// <summary>
    /// A layout from the assistant goes straight onto the canvas as an undoable step: watching the
    /// design change as you ask for it is the point of docking the assistant, and Undo makes that
    /// safe rather than destructive.
    /// </summary>
    private void OnAssistantLayout(object? sender, UiDocument document)
    {
        _session.Replace(document);

        var warnings = document.Warnings();
        SetStatus(warnings.Count == 0
            ? $"Jack applied '{document.Name}'. Undo puts your previous layout back."
            : $"Jack applied '{document.Name}', with {warnings.Count} warning(s). Undo puts your previous layout back.");
    }

    #endregion

    /// <summary>The editing model, for the headless UI tests.</summary>
    internal DesignerSession Session => _session;

    /// <summary>The renderer, for the headless UI tests.</summary>
    internal LvglPreview Preview => _preview;
}
