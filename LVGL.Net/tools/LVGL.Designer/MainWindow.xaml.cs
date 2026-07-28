using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Lvgl.Ui;
using Microsoft.Win32;

// System.Windows.Shapes is not imported wholesale: its Path type collides with System.IO.Path,
// which this file uses far more often.
using Rectangle = System.Windows.Shapes.Rectangle;

namespace Lvgl.Designer;

public partial class MainWindow : Window
{
    private readonly LvglPreview _preview = new();
    private readonly DispatcherTimer _rebuildDebounce;
    private UiDocument _document = CreateStarterDocument();
    private NodeViewModel? _selection;
    private string? _path;
    private bool _dirty;
    private bool _suppressScreenSizeEvents;

    public MainWindow()
    {
        InitializeComponent();

        // Property edits arrive per keystroke; rebuilding the LVGL tree on each one would make
        // typing feel heavy, so edits are coalesced into one rebuild.
        _rebuildDebounce = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(150),
        };
        _rebuildDebounce.Tick += (_, _) =>
        {
            _rebuildDebounce.Stop();
            RebuildPreview();
        };

        ToolboxList.ItemsSource = Enum.GetValues<UiWidgetType>();

        _preview.SurfaceChanged += (_, _) => OnPreviewSurfaceChanged();
        _preview.FrameReady += (_, _) => UpdateSelectionOverlay();

        Assistant.LayoutProduced += OnAssistantLayout;
        Assistant.HideRequested += (_, _) => AssistantToggle.IsChecked = false;
        CodeSurface.ApplyRequested += OnCodeApplied;

        // Panel shortcuts, following the convention of the editors this borrows from.
        InputBindings.Add(new KeyBinding(
            new ToggleCommand(() => AssistantToggle), Key.J, ModifierKeys.Control));

        InputBindings.Add(new KeyBinding(
            new ToggleCommand(() => ToolboxToggle), Key.B, ModifierKeys.Control));

        InputBindings.Add(new KeyBinding(
            new ToggleCommand(() => InspectorToggle), Key.B, ModifierKeys.Control | ModifierKeys.Shift));

        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    /// <summary>Flips a toggle button, for the panel keyboard shortcuts.</summary>
    private sealed class ToggleCommand(Func<ToggleButton> target) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter)
        {
            var button = target();
            if (button.IsEnabled) button.IsChecked = button.IsChecked != true;
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ClampToWorkingArea();
        ApplyPanelVisibility();

        LoadDocument(_document, path: null);
        _preview.Start();
    }

    /// <summary>
    /// Shrinks the window to fit the screen it opened on.
    /// </summary>
    /// <remarks>
    /// The default size suits a large display; on a smaller one the toolbar's right-hand controls
    /// would sit off-screen with no way to reach them, because the window would be wider than the
    /// desktop.
    /// </remarks>
    private void ClampToWorkingArea()
    {
        var available = SystemParameters.WorkArea;

        if (Width > available.Width) Width = available.Width;
        if (Height > available.Height) Height = available.Height;

        if (Left < available.Left) Left = available.Left;
        if (Top < available.Top) Top = available.Top;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!ConfirmDiscardChanges())
        {
            e.Cancel = true;
            return;
        }

        _preview.Dispose();
        Assistant.Shutdown();

        // Fully qualified: the `Assistant` field shadows the namespace of the same name.
        // The panel owns the attachment HTTP host, which must be shut down explicitly or the
        // process lingers with a bound port.
        Lvgl.Designer.Assistant.ChatPanel.DisposeShared();
    }

    #region Design / Code mode

    /// <summary>Which pane the workspace is showing.</summary>
    private bool IsCodeMode => CodeModeButton.IsChecked == true;

    private void OnModeChanged(object sender, RoutedEventArgs e)
    {
        // Fires during InitializeComponent before the panels exist.
        if (CodeSurface is null || DesignSurface is null) return;

        if (IsCodeMode)
        {
            CodeSurface.Show(_document);
            CodeSurface.Visibility = Visibility.Visible;
            DesignSurface.Visibility = Visibility.Collapsed;

            _preview.Stop();
            SetStatus("Code mode. Edit the layout JSON and apply it, or read the generated C#.");
        }
        else
        {
            CodeSurface.Visibility = Visibility.Collapsed;
            DesignSurface.Visibility = Visibility.Visible;

            _preview.Start();
            RebuildPreview();
            SetStatus("Design mode.");
        }

        ApplyPanelVisibility();
    }

    #endregion

    #region Side panels

    // Remembered separately from actual visibility: code mode hides both panels because they act
    // on the canvas, but the user's choice must survive switching back to design.
    private GridLength _toolboxWidth = new(170);
    private GridLength _inspectorWidth = new(300);

    private void OnPanelToggled(object sender, RoutedEventArgs e) => ApplyPanelVisibility();

    private void OnHideToolbox(object sender, RoutedEventArgs e) => ToolboxToggle.IsChecked = false;

    private void OnHideInspector(object sender, RoutedEventArgs e) => InspectorToggle.IsChecked = false;

    /// <summary>
    /// Reconciles the toggles, the current mode and the grid columns.
    /// </summary>
    /// <remarks>
    /// Collapsing is done by zeroing the column width rather than by hiding the border, because a
    /// hidden element in a sized column would leave the gap behind.
    /// </remarks>
    private void ApplyPanelVisibility()
    {
        if (ToolboxColumn is null) return;

        // In code mode the editor gets the whole width; the toggles stay in their user-chosen
        // state but are disabled so it is clear why they do nothing.
        var showToolbox = ToolboxToggle.IsChecked == true && !IsCodeMode;
        var showInspector = InspectorToggle.IsChecked == true && !IsCodeMode;

        ToolboxToggle.IsEnabled = !IsCodeMode;
        InspectorToggle.IsEnabled = !IsCodeMode;

        SetPanel(showToolbox, ToolboxColumn, ToolboxSplitterColumn, ToolboxPanel, ToolboxSplitter, ref _toolboxWidth, 120);
        SetPanel(showInspector, InspectorColumn, InspectorSplitterColumn, InspectorPanel, InspectorSplitter, ref _inspectorWidth, 220);
    }

    private static void SetPanel(
        bool show,
        ColumnDefinition column,
        ColumnDefinition splitterColumn,
        UIElement panel,
        UIElement splitter,
        ref GridLength remembered,
        double minimumWidth)
    {
        if (show)
        {
            column.Width = remembered;
            column.MinWidth = minimumWidth;
            splitterColumn.Width = new GridLength(4);

            panel.Visibility = Visibility.Visible;
            splitter.Visibility = Visibility.Visible;
        }
        else
        {
            // Remember whatever width the user dragged it to, so reopening restores it.
            if (column.Width.Value > 0) remembered = column.Width;

            column.MinWidth = 0;
            column.Width = new GridLength(0);
            splitterColumn.Width = new GridLength(0);

            panel.Visibility = Visibility.Collapsed;
            splitter.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>Applies JSON edited in the code view back onto the canvas.</summary>
    private void OnCodeApplied(object? sender, UiDocument document)
    {
        RememberForRevert();

        LoadDocument(document, _path);
        MarkDirty();

        SetStatus($"Applied '{document.Name}' from the code editor.");
    }

    #endregion

    #region Assistant panel

    /// <summary>Width the assistant panel opens at, and returns to after being hidden.</summary>
    private GridLength _assistantWidth = new(420);

    private async void OnAssistantToggled(object sender, RoutedEventArgs e)
    {
        if (Assistant is null) return;

        if (AssistantToggle.IsChecked == true)
        {
            AssistantColumn.Width = _assistantWidth;
            SplitterColumn.Width = new GridLength(4);
            AssistantColumn.MinWidth = 320;

            Assistant.Visibility = Visibility.Visible;
            AssistantSplitter.Visibility = Visibility.Visible;

            // Started lazily so a session that never opens the assistant pays nothing.
            await Assistant.EnsureStartedAsync();
            Assistant.FocusComposer();
        }
        else
        {
            // Remember the width so reopening restores the size the user chose.
            if (AssistantColumn.Width.Value > 0) _assistantWidth = AssistantColumn.Width;

            AssistantColumn.MinWidth = 0;
            AssistantColumn.Width = new GridLength(0);
            SplitterColumn.Width = new GridLength(0);

            Assistant.Visibility = Visibility.Collapsed;
            AssistantSplitter.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// A layout from the assistant goes straight onto the canvas.
    /// </summary>
    /// <remarks>
    /// Applied without a confirmation prompt - being able to watch the design change as you ask
    /// for it is the point of docking the assistant. The previous document is kept so a single
    /// click undoes it, which is what makes applying immediately safe rather than destructive.
    /// </remarks>
    private void OnAssistantLayout(object? sender, UiDocument document)
    {
        RememberForRevert();

        LoadDocument(document, _path);
        MarkDirty();

        if (IsCodeMode) CodeSurface.Show(document);

        var warnings = document.Warnings();
        SetStatus(warnings.Count == 0
            ? $"Jack applied '{document.Name}'."
            : $"Jack applied '{document.Name}' - {string.Join("; ", warnings)}");
    }

    #endregion

    #region Revert

    private UiDocument? _documentBeforeChange;

    /// <summary>Snapshots the current document so one applied change can be undone.</summary>
    private void RememberForRevert()
    {
        _documentBeforeChange = _document;
        RevertButton.Visibility = Visibility.Visible;
    }

    private void OnRevertAssistantChange(object sender, RoutedEventArgs e)
    {
        if (_documentBeforeChange is not { } previous) return;

        _documentBeforeChange = null;
        RevertButton.Visibility = Visibility.Collapsed;

        LoadDocument(previous, _path);
        MarkDirty();

        if (IsCodeMode) CodeSurface.Show(previous);

        SetStatus("Reverted to the previous layout.");
    }

    #endregion

    #region Document lifecycle

    private static UiDocument CreateStarterDocument() => new()
    {
        Name = "MainScreen",
        Width = 800,
        Height = 480,
        BackgroundColor = "#0F1720",
        Children =
        [
            new UiNode
            {
                Type = UiWidgetType.Label,
                Name = "TitleLabel",
                Text = "Hello from LVGL.Net",
                Align = LvAlign.TopMid,
                Y = 28,
                TextColor = "#E6EDF3",
                FontSize = 28,
            },
            new UiNode
            {
                Type = UiWidgetType.Button,
                Name = "ActionButton",
                Text = "Press me",
                Align = LvAlign.Center,
                Width = 180,
                Height = 52,
                Radius = 10,
                BackgroundColor = "#38BDF8",
                TextColor = "#0F1720",
            },
        ],
    };

    private void LoadDocument(UiDocument document, string? path)
    {
        _document = document;
        _path = path;
        _selection = null;

        _suppressScreenSizeEvents = true;
        ScreenWidthBox.Text = document.Width.ToString(CultureInfo.InvariantCulture);
        ScreenHeightBox.Text = document.Height.ToString(CultureInfo.InvariantCulture);
        _suppressScreenSizeEvents = false;

        RefreshOutline();
        BuildPropertyPanel(null);
        RebuildPreview();

        // Keep the code view in step when the document is replaced underneath it.
        if (IsCodeMode) CodeSurface.Show(document);

        MarkClean();
    }

    private void OnNew(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscardChanges()) return;
        LoadDocument(CreateStarterDocument(), path: null);
        SetStatus("New layout created.");
    }

    private void OnOpen(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscardChanges()) return;

        var dialog = new OpenFileDialog
        {
            Title = "Open layout",
            Filter = $"LVGL layout (*{UiJson.FileExtension})|*{UiJson.FileExtension}|JSON (*.json)|*.json|All files (*.*)|*.*",
        };

        if (dialog.ShowDialog(this) != true) return;

        try
        {
            LoadDocument(UiJson.Load(dialog.FileName), dialog.FileName);
            SetStatus($"Opened {dialog.FileName}");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException)
        {
            MessageBox.Show(this, ex.Message, "Could not open the layout", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (_path is null) OnSaveAs(sender, e);
        else Save(_path);
    }

    private void OnSaveAs(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Save layout",
            FileName = _document.Name + UiJson.FileExtension,
            Filter = $"LVGL layout (*{UiJson.FileExtension})|*{UiJson.FileExtension}|All files (*.*)|*.*",
        };

        if (dialog.ShowDialog(this) == true) Save(dialog.FileName);
    }

    private void Save(string path)
    {
        try
        {
            UiJson.Save(_document, path);
            _path = path;
            MarkClean();
            SetStatus($"Saved {path}");
        }
        catch (IOException ex)
        {
            MessageBox.Show(this, ex.Message, "Could not save", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnExportCSharp(object sender, RoutedEventArgs e)
    {
        var problems = _document.Validate();
        if (problems.Count > 0)
        {
            MessageBox.Show(
                this,
                string.Join(Environment.NewLine, problems),
                "Fix these before exporting",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Export generated C#",
            FileName = _document.Name + ".Designer.cs",
            Filter = "C# source (*.cs)|*.cs|All files (*.*)|*.*",
        };

        if (dialog.ShowDialog(this) != true) return;

        var generator = new CSharpUiGenerator
        {
            ClassName = _document.Name,
            SourcePath = _path,
            Namespace = PromptForNamespace(),
        };

        try
        {
            File.WriteAllText(dialog.FileName, generator.Generate(_document));
            SetStatus($"Exported {dialog.FileName}");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            MessageBox.Show(this, ex.Message, "Could not export", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private string? PromptForNamespace()
    {
        // The namespace is the only thing export needs that the document does not already carry.
        var input = PromptWindow.Show(
            this,
            "Export C#",
            "Namespace for the generated class (leave empty for none):",
            "MyApp.Ui");

        return string.IsNullOrWhiteSpace(input) ? null : input.Trim();
    }

    private bool ConfirmDiscardChanges()
    {
        if (!_dirty) return true;

        var result = MessageBox.Show(
            this,
            "The layout has unsaved changes. Discard them?",
            "Unsaved changes",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        return result == MessageBoxResult.Yes;
    }

    private void MarkDirty()
    {
        _dirty = true;
        DocumentStatus.Text = (_path is null ? "(unsaved layout)" : Path.GetFileName(_path)) + " *";
    }

    private void MarkClean()
    {
        _dirty = false;
        DocumentStatus.Text = _path is null ? "(unsaved layout)" : Path.GetFileName(_path);
    }

    private void SetStatus(string message) => StatusText.Text = message;

    #endregion

    #region Outline and editing

    private void RefreshOutline()
    {
        OutlineTree.ItemsSource = null;
        OutlineTree.ItemsSource = _document.Children;
    }

    private void OnOutlineSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        BuildPropertyPanel(e.NewValue as UiNode);
        UpdateSelectionOverlay();
    }

    private void OnToolboxDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (ToolboxList.SelectedItem is not UiWidgetType type) return;

        var node = new UiNode
        {
            Type = type,
            Name = UniqueName(type),
            X = 20,
            Y = 20,
        };

        ApplyTypeDefaults(node);

        // Drop into the selected container when there is one, otherwise onto the screen.
        var parent = _selection?.Node;
        if (parent is { AcceptsChildren: true }) parent.Children.Add(node);
        else _document.Children.Add(node);

        MarkDirty();
        RefreshOutline();
        RebuildPreview();
        SetStatus($"Added {type}.");
    }

    private static void ApplyTypeDefaults(UiNode node)
    {
        switch (node.Type)
        {
            case UiWidgetType.Label:
                node.Text = "Label";
                break;
            case UiWidgetType.Button:
                node.Text = "Button";
                node.Width = 140;
                node.Height = 44;
                node.Radius = 8;
                break;
            case UiWidgetType.Checkbox:
                node.Text = "Checkbox";
                break;
            case UiWidgetType.Panel:
                node.Width = 200;
                node.Height = 140;
                node.Radius = 10;
                break;
            case UiWidgetType.Slider or UiWidgetType.Bar:
                node.Width = 200;
                node.Height = 10;
                node.Minimum = 0;
                node.Maximum = 100;
                node.Value = 50;
                break;
            case UiWidgetType.Arc:
                node.Width = 120;
                node.Height = 120;
                node.Minimum = 0;
                node.Maximum = 100;
                node.Value = 40;
                break;
            case UiWidgetType.Dropdown or UiWidgetType.Roller:
                node.Options = ["First", "Second", "Third"];
                node.Width = 160;
                break;
            case UiWidgetType.TextArea:
                node.Width = 220;
                node.Height = 48;
                break;
            case UiWidgetType.Chart:
                node.Width = 360;
                node.Height = 200;
                node.ChartType = LvChartType.Line;
                node.PointCount = 40;
                node.Minimum = 0;
                node.Maximum = 100;
                node.SeriesColors = ["#38BDF8"];
                break;
        }
    }

    private string UniqueName(UiWidgetType type)
    {
        var index = 1;
        string candidate;
        do
        {
            candidate = $"{type}{index++}";
        }
        while (_document.Find(candidate) is not null);

        return candidate;
    }

    private void OnDeleteNode(object sender, RoutedEventArgs e)
    {
        if (_selection?.Node is not { } node) return;

        if (!RemoveNode(_document.Children, node))
        {
            SetStatus("Could not find the selected widget to delete.");
            return;
        }

        _selection = null;
        MarkDirty();
        RefreshOutline();
        BuildPropertyPanel(null);
        RebuildPreview();
        SetStatus("Widget deleted.");
    }

    private static bool RemoveNode(List<UiNode> children, UiNode target)
    {
        if (children.Remove(target)) return true;

        foreach (var child in children)
        {
            if (RemoveNode(child.Children, target)) return true;
        }

        return false;
    }

    private void OnDuplicateNode(object sender, RoutedEventArgs e)
    {
        if (_selection?.Node is not { } node) return;

        var clone = DeepClone(node);
        clone.Name = clone.Name is null ? null : UniqueName(clone.Type);
        clone.X += 16;
        clone.Y += 16;

        var siblings = FindParentList(_document.Children, node) ?? _document.Children;
        siblings.Add(clone);

        MarkDirty();
        RefreshOutline();
        RebuildPreview();
        SetStatus("Widget duplicated.");
    }

    /// <summary>
    /// Clones through the document serializer, so a new field on <see cref="UiNode"/> is copied
    /// automatically instead of being silently dropped by a hand-written copy.
    /// </summary>
    private static UiNode DeepClone(UiNode node)
    {
        var wrapper = new UiDocument { Children = [node] };
        return UiJson.Parse(UiJson.ToJson(wrapper)).Children[0];
    }

    private static List<UiNode>? FindParentList(List<UiNode> children, UiNode target)
    {
        if (children.Contains(target)) return children;

        foreach (var child in children)
        {
            if (FindParentList(child.Children, target) is { } found) return found;
        }

        return null;
    }

    private void OnScreenSizeChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressScreenSizeEvents) return;

        if (int.TryParse(ScreenWidthBox.Text, out var width) && width is > 0 and <= 4096) _document.Width = width;
        if (int.TryParse(ScreenHeightBox.Text, out var height) && height is > 0 and <= 4096) _document.Height = height;

        MarkDirty();
        ScheduleRebuild();
    }

    #endregion

    #region Property panel

    private void BuildPropertyPanel(UiNode? node)
    {
        PropertyPanel.Children.Clear();

        if (node is null)
        {
            _selection = null;
            PropertyPanel.Children.Add(new TextBlock
            {
                Text = "Select a widget in the outline, or double-click a toolbox entry to add one.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)FindResource("InkMuted"),
                FontSize = 12,
            });
            return;
        }

        var model = new NodeViewModel(node);
        model.Edited += (_, _) =>
        {
            MarkDirty();
            ScheduleRebuild();
        };

        _selection = model;
        PropertyPanel.DataContext = model;

        AddHeader(model.TypeName);
        AddTextRow("Name", nameof(NodeViewModel.Name));
        AddTextRow("X", nameof(NodeViewModel.X));
        AddTextRow("Y", nameof(NodeViewModel.Y));
        AddTextRow("Width", nameof(NodeViewModel.Width));
        AddTextRow("Height", nameof(NodeViewModel.Height));
        AddComboRow("Align", nameof(NodeViewModel.Align), model.AlignOptions);

        if (model.ShowText) AddTextRow("Text", nameof(NodeViewModel.Text));

        AddHeader("Appearance");
        AddTextRow("Background", nameof(NodeViewModel.BackgroundColor), "#RRGGBB");
        AddTextRow("Text colour", nameof(NodeViewModel.TextColor), "#RRGGBB");
        AddTextRow("Border colour", nameof(NodeViewModel.BorderColor), "#RRGGBB");
        AddTextRow("Border width", nameof(NodeViewModel.BorderWidth));
        AddTextRow("Radius", nameof(NodeViewModel.Radius));
        AddTextRow("Padding", nameof(NodeViewModel.Padding));
        AddTextRow("Font size", nameof(NodeViewModel.FontSize), "12 / 14 / 16 / 20 / 24 / 28 / 36");
        AddCheckRow("Hidden", nameof(NodeViewModel.Hidden));

        if (model.ShowRange)
        {
            AddHeader("Range");
            AddTextRow("Minimum", nameof(NodeViewModel.Minimum));
            AddTextRow("Maximum", nameof(NodeViewModel.Maximum));
            if (!model.ShowChart) AddTextRow("Value", nameof(NodeViewModel.Value));
        }

        if (model.ShowOptions)
        {
            AddHeader("Options");
            AddMultilineRow("One per line", nameof(NodeViewModel.Options));
        }

        if (model.ShowChart)
        {
            AddHeader("Chart");
            AddComboRow("Type", nameof(NodeViewModel.ChartType), model.ChartTypeOptions);
            AddTextRow("Point count", nameof(NodeViewModel.PointCount));
            AddMultilineRow("Series colours", nameof(NodeViewModel.SeriesColors));
        }
    }

    private void AddHeader(string text) => PropertyPanel.Children.Add(new TextBlock
    {
        Text = text.ToUpperInvariant(),
        Style = (Style)FindResource("SectionHeader"),
        Margin = new Thickness(0, 12, 0, 4),
    });

    private void AddLabel(string text) => PropertyPanel.Children.Add(new TextBlock
    {
        Text = text,
        FontSize = 11,
        Foreground = (Brush)FindResource("InkMuted"),
        Margin = new Thickness(0, 4, 0, 2),
    });

    private void AddTextRow(string label, string property, string? hint = null)
    {
        AddLabel(hint is null ? label : $"{label}  ({hint})");

        var box = new TextBox();
        box.SetBinding(TextBox.TextProperty, new Binding(property)
        {
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
        });

        PropertyPanel.Children.Add(box);
    }

    private void AddMultilineRow(string label, string property)
    {
        AddLabel(label);

        var box = new TextBox
        {
            AcceptsReturn = true,
            Height = 84,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        box.SetBinding(TextBox.TextProperty, new Binding(property)
        {
            UpdateSourceTrigger = UpdateSourceTrigger.LostFocus,
        });

        PropertyPanel.Children.Add(box);
    }

    private void AddComboRow(string label, string property, IReadOnlyList<string> options)
    {
        AddLabel(label);

        var combo = new ComboBox { ItemsSource = options };
        combo.SetBinding(Selector.SelectedItemProperty, new Binding(property));

        PropertyPanel.Children.Add(combo);
    }

    private void AddCheckRow(string label, string property)
    {
        var check = new CheckBox
        {
            Content = label,
            Foreground = (Brush)FindResource("Ink"),
            Margin = new Thickness(0, 8, 0, 4),
        };

        check.SetBinding(ToggleButton.IsCheckedProperty, new Binding(property));
        PropertyPanel.Children.Add(check);
    }

    #endregion

    #region Preview

    private void ScheduleRebuild()
    {
        _rebuildDebounce.Stop();
        _rebuildDebounce.Start();
    }

    private void RebuildPreview()
    {
        _preview.Show(_document);
        OnPreviewSurfaceChanged();
    }

    private void OnPreviewSurfaceChanged()
    {
        PreviewImage.Source = _preview.Bitmap;

        var unavailable = _preview.UnavailableReason;
        PreviewUnavailablePanel.Visibility = unavailable is null ? Visibility.Collapsed : Visibility.Visible;
        PreviewUnavailableText.Text = unavailable ?? string.Empty;

        if (_preview.Bitmap is { } bitmap)
        {
            OverlayCanvas.Width = bitmap.PixelWidth;
            OverlayCanvas.Height = bitmap.PixelHeight;
        }

        UpdateSelectionOverlay();
    }

    private void UpdateSelectionOverlay()
    {
        OverlayCanvas.Children.Clear();

        if (_preview.GetBounds(_selection?.Node) is not { } bounds) return;

        var outline = new Rectangle
        {
            Width = Math.Max(1, bounds.Width),
            Height = Math.Max(1, bounds.Height),
            Stroke = (Brush)FindResource("Accent"),
            StrokeThickness = 1,
            StrokeDashArray = [3, 2],
            SnapsToDevicePixels = true,
        };

        Canvas.SetLeft(outline, bounds.X);
        Canvas.SetTop(outline, bounds.Y);
        OverlayCanvas.Children.Add(outline);
    }

    #endregion
}
