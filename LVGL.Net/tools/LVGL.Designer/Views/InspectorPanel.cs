using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Lvgl.Designer.Core;
using Lvgl.Drawing;
using Lvgl.Ui;

namespace Lvgl.Designer.Views;

/// <summary>
/// The property inspector: the selected widget's properties, or the screen's when nothing is
/// selected, and a second page for the document's shared styles.
/// </summary>
/// <remarks>
/// <para>
/// Fields are built per widget kind - a slider shows a range, a chart its series - because a
/// property grid that lists everything for everyone hides the five fields that matter under
/// twenty that do nothing.
/// </para>
/// <para>
/// An empty optional field means "LVGL's default", not zero; that is the distinction
/// <see cref="UiNode"/>'s nullable properties carry. Edits apply as you type and undo per field.
/// </para>
/// </remarks>
public sealed class InspectorPanel : UserControl
{
    private readonly StackPanel _body = new() { Margin = new Thickness(14, 6, 14, 18), Spacing = 0 };
    private readonly RadioButton _propertiesTab;
    private readonly RadioButton _stylesTab;
    private readonly List<Action> _refreshers = [];

    private DesignerSession? _session;
    private UiStyle? _selectedStyle;
    private bool _building;

    public InspectorPanel()
    {
        _propertiesTab = Segment("Properties", isChecked: true);
        _stylesTab = Segment("Styles", isChecked: false);
        _propertiesTab.IsCheckedChanged += (_, _) => Rebuild();

        var tabs = new Border
        {
            Background = Brush("Mask"),
            BorderBrush = Brush("Trace"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(2),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { _propertiesTab, _stylesTab } },
        };

        var head = new Border { Child = tabs };
        head.Classes.Add("panel-head");

        var root = new DockPanel();
        DockPanel.SetDock(head, Dock.Top);
        root.Children.Add(head);
        root.Children.Add(new ScrollViewer { Content = _body, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });

        var panel = new Border { Child = root };
        panel.Classes.Add("panel");
        Content = panel;
    }

    private static RadioButton Segment(string text, bool isChecked)
    {
        var button = new RadioButton { Content = text, GroupName = "inspector", IsChecked = isChecked };
        button.Classes.Add("segment");
        return button;
    }

    /// <summary>Raised when a field needs a file from disk; the window shows the picker.</summary>
    public Func<string, Avalonia.Platform.Storage.FilePickerFileType, Task<string?>>? PickAsset { get; set; }

    public void Attach(DesignerSession session)
    {
        _session = session;
        session.SelectionChanged += (_, _) => Rebuild();
        session.Changed += (_, change) =>
        {
            // A drag on the canvas changes X/Y/W/H many times a second: update values in place
            // rather than rebuilding, so a focused field keeps its caret.
            if (change == DesignChange.Properties) RefreshValues();
            else Rebuild();
        };

        Rebuild();
    }

    /// <summary>Shows the Styles page, optionally with a style selected.</summary>
    public void ShowStyles(UiStyle? style = null)
    {
        _selectedStyle = style ?? _selectedStyle;
        _stylesTab.IsChecked = true;
        Rebuild();
    }

    #region Building

    private void Rebuild()
    {
        if (_session is null || _building) return;
        _building = true;

        try
        {
            _body.Children.Clear();
            _refreshers.Clear();

            if (_stylesTab.IsChecked == true) BuildStyles();
            else if (_session.Selection is { } node && _session.Document.Descendants().Contains(node)) BuildNode(node);
            else BuildScreen();
        }
        finally
        {
            _building = false;
        }
    }

    private void RefreshValues()
    {
        foreach (var refresh in _refreshers) refresh();
    }

    private void BuildScreen()
    {
        var document = _session!.Document;

        Header("SCREEN", null);
        Text("Class name", () => document.Name, value => _session.EditDocument("name", () => document.Name = string.IsNullOrWhiteSpace(value) ? "Screen" : value.Trim()), mono: true,
            validate: value => UiDocument.IsValidIdentifier(value ?? string.Empty) ? null : "Must be a valid C# identifier");
        Color("Background", () => document.BackgroundColor, value => _session.EditDocument("background", () => document.BackgroundColor = value));

        Note("Click a widget on the board or in the outline to edit it. Add widgets from Parts.");

        var problems = document.Validate();
        if (problems.Count > 0)
        {
            Header("PROBLEMS", null);
            foreach (var problem in problems) Note(problem, "Fault");
        }

        var warnings = document.Warnings();
        if (warnings.Count > 0)
        {
            Header("WORTH A LOOK", null);
            foreach (var warning in warnings) Note(warning, "Warn");
        }
    }

    private void BuildNode(UiNode node)
    {
        var session = _session!;
        var document = session.Document;
        void Edit(string property, Action apply) => session.Edit(node, property, apply);

        Header(node.Type.ToString().ToUpperInvariant(), Designators.For(document, node));

        Text("Name", () => node.Name, value => Edit("name", () => node.Name = string.IsNullOrWhiteSpace(value) ? null : value.Trim()), mono: true,
            validate: value => string.IsNullOrEmpty(value) || UiDocument.IsValidIdentifier(value.Trim()) ? null : "Not a valid C# identifier, so no field is generated");

        var isPage = node.Type is UiWidgetType.Tab or UiWidgetType.Tile;

        if (!isPage)
        {
            Header("PLACEMENT", null);
            Placement(node);
            Choice("Align", ["(absolute)", .. Enum.GetNames<LvAlign>()], () => node.Align?.ToString() ?? "(absolute)",
                value => Edit("align", () => node.Align = Enum.TryParse<LvAlign>(value, out var align) ? align : null));

            if (node.Align is not null and not LvAlign.Default)
            {
                Note("With an alignment, X and Y are offsets from the anchor, not positions.");
            }
        }

        BuildContent(node, Edit);

        Header("APPEARANCE", null);
        Color("Background", () => node.BackgroundColor, value => Edit("bg", () => node.BackgroundColor = value));
        Color("Text", () => node.TextColor, value => Edit("text-color", () => node.TextColor = value));
        Color("Border", () => node.BorderColor, value => Edit("border-color", () => node.BorderColor = value));
        if (HasAccent(node.Type))
        {
            Color(node.Type == UiWidgetType.Led ? "Light" : "Accent", () => node.AccentColor, value => Edit("accent", () => node.AccentColor = value));
        }

        IntPair("Border px", () => node.BorderWidth, v => Edit("border", () => node.BorderWidth = v),
            "Radius", () => node.Radius, v => Edit("radius", () => node.Radius = v));
        IntPair("Padding", () => node.Padding, v => Edit("padding", () => node.Padding = v),
            "Font size", () => node.FontSize, v => Edit("font-size", () => node.FontSize = v));
        Asset("Font", () => node.Font, value => Edit("font", () => node.Font = value), Dialogs.Fonts,
            "Montserrat 12-36 built in; a TrueType file renders at Font size.");
        Check("Hidden at start", () => node.Hidden, value => Edit("hidden", () => node.Hidden = value));

        if (document.Styles.Count > 0)
        {
            Header("SHARED STYLES", null);
            foreach (var style in document.Styles)
            {
                var name = style.Name;
                Check(name, () => node.Styles.Contains(name), value => session.ToggleStyle(node, name, value));
            }
        }
        else
        {
            Header("SHARED STYLES", null);
            Action("Create a shared style", () => ShowStyles(session.AddStyle()));
        }
    }

    private static bool HasAccent(UiWidgetType type) => type is UiWidgetType.Slider or UiWidgetType.Bar or UiWidgetType.Arc
        or UiWidgetType.Spinner or UiWidgetType.Led or UiWidgetType.Switch or UiWidgetType.Checkbox;

    private void BuildContent(UiNode node, Action<string, Action> edit)
    {
        var document = _session!.Document;

        switch (node.Type)
        {
            case UiWidgetType.Label or UiWidgetType.Button or UiWidgetType.Checkbox or UiWidgetType.TextArea:
                Header("CONTENT", null);
                Text(node.Type == UiWidgetType.TextArea ? "Initial text" : "Text", () => node.Text, value => edit("text", () => node.Text = string.IsNullOrEmpty(value) ? null : value));
                if (node.Type is UiWidgetType.Checkbox) Check("Starts checked", () => node.Value is > 0, value => edit("value", () => node.Value = value ? 1 : null));
                break;

            case UiWidgetType.Switch:
                Header("CONTENT", null);
                Check("Starts on", () => node.Value is > 0, value => edit("value", () => node.Value = value ? 1 : null));
                break;

            case UiWidgetType.Tab:
                Header("TAB", null);
                Text("Title", () => node.Text, value => edit("text", () => node.Text = string.IsNullOrEmpty(value) ? null : value));
                Note("A tab is sized by its tab view. Drop widgets into it.");
                break;

            case UiWidgetType.Tile:
                Header("TILE", null);
                IntPair("Column", () => node.Column, v => edit("column", () => node.Column = v), "Row", () => node.Row, v => edit("row", () => node.Row = v));
                Note("Tiles fill their tile view; the user swipes between them by column and row.");
                break;

            case UiWidgetType.Slider or UiWidgetType.Bar or UiWidgetType.Arc or UiWidgetType.Spinbox:
                Header("RANGE", null);
                IntPair("Minimum", () => node.Minimum, v => edit("min", () => node.Minimum = v), "Maximum", () => node.Maximum, v => edit("max", () => node.Maximum = v));
                Int("Value", () => node.Value, v => edit("value", () => node.Value = v));
                if (node.Type == UiWidgetType.Spinbox)
                {
                    IntPair("Digits", () => node.Digits, v => edit("digits", () => node.Digits = v), "Decimals", () => node.DecimalPlaces, v => edit("decimals", () => node.DecimalPlaces = v));
                }
                break;

            case UiWidgetType.Dropdown or UiWidgetType.Roller:
                Header("OPTIONS", null);
                Lines("One per line", () => node.Options, value => edit("options", () => node.Options = value));
                break;

            case UiWidgetType.List:
                Header("ITEMS", null);
                Lines("One per line; # starts a heading", () => node.Options, value => edit("options", () => node.Options = value));
                break;

            case UiWidgetType.Table:
                Header("ROWS", null);
                Lines("One row per line; | between cells", () => node.Options, value => edit("options", () => node.Options = value));
                break;

            case UiWidgetType.Chart:
                Header("CHART", null);
                Choice("Type", ["(default)", .. Enum.GetNames<LvChartType>()], () => node.ChartType?.ToString() ?? "(default)",
                    value => edit("chart-type", () => node.ChartType = Enum.TryParse<LvChartType>(value, out var type) ? type : null));
                IntPair("Minimum", () => node.Minimum, v => edit("min", () => node.Minimum = v), "Maximum", () => node.Maximum, v => edit("max", () => node.Maximum = v));
                Int("Points", () => node.PointCount, v => edit("points", () => node.PointCount = v));
                Lines("Series colours, #RRGGBB per line", () => node.SeriesColors,
                    value => edit("series", () => node.SeriesColors = value.Select(Normalize).OfType<string>().ToList()));
                break;

            case UiWidgetType.Image:
                Header("PICTURE", null);
                Asset("File", () => node.Source, value => edit("source", () =>
                {
                    node.Source = value;
                    if (value is not null) node.Symbol = null;
                }), Dialogs.Images, "PNG or BMP, copied next to the layout.");
                Choice("Symbol", ["(none)", .. UiSymbols.Names], () => node.Symbol ?? "(none)", value => edit("symbol", () =>
                {
                    node.Symbol = value == "(none)" ? null : value;
                    if (node.Symbol is not null) node.Source = null;
                }));
                IntPair("Rotation", () => node.Rotation, v => edit("rotation", () => node.Rotation = v), "Scale", () => node.ImageScale, v => edit("scale", () => node.ImageScale = v));
                Note("Rotation is in tenths of a degree; scale 256 is 100 %.");
                break;

            case UiWidgetType.TabView:
                Header("TABS", null);
                Choice("Tab bar", ["(top)", nameof(LvDir.Top), nameof(LvDir.Bottom), nameof(LvDir.Left), nameof(LvDir.Right)],
                    () => node.TabBarPosition?.ToString() ?? "(top)",
                    value => edit("tab-bar", () => node.TabBarPosition = Enum.TryParse<LvDir>(value, out var dir) ? dir : null));
                Action("Add a tab", () => AddPage(node, UiWidgetType.Tab));
                break;

            case UiWidgetType.TileView:
                Header("TILES", null);
                Action("Add a tile", () => AddPage(node, UiWidgetType.Tile));
                break;

            case UiWidgetType.Keyboard:
                Header("KEYBOARD", null);
                var fields = document.Descendants().Where(n => n.Type == UiWidgetType.TextArea && n.Name is not null).Select(n => n.Name!).ToList();
                Choice("Types into", ["(none)", .. fields], () => node.Target ?? "(none)",
                    value => edit("target", () => node.Target = value == "(none)" ? null : value));
                Choice("Keys", ["(default)", .. Enum.GetNames<LvKeyboardMode>()], () => node.KeyboardMode?.ToString() ?? "(default)",
                    value => edit("mode", () => node.KeyboardMode = Enum.TryParse<LvKeyboardMode>(value, out var mode) ? mode : null));
                if (fields.Count == 0) Note("Name a TextArea to connect the keyboard to it.");
                break;

            case UiWidgetType.Scale:
                Header("SCALE", null);
                Choice("Shape", ["(default)", .. Enum.GetNames<LvScaleMode>()], () => node.ScaleMode?.ToString() ?? "(default)",
                    value => edit("scale-mode", () => node.ScaleMode = Enum.TryParse<LvScaleMode>(value, out var mode) ? mode : null));
                IntPair("Minimum", () => node.Minimum, v => edit("min", () => node.Minimum = v), "Maximum", () => node.Maximum, v => edit("max", () => node.Maximum = v));
                IntPair("Ticks", () => node.TickCount, v => edit("ticks", () => node.TickCount = v), "Major every", () => node.MajorTickEvery, v => edit("major", () => node.MajorTickEvery = v));
                break;

            case UiWidgetType.Led:
                Header("LIGHT", null);
                Int("Brightness", () => node.Value, v => edit("value", () => node.Value = v));
                break;
        }
    }

    private void AddPage(UiNode host, UiWidgetType type)
    {
        _session!.Selection = host;
        _session.Add(type);
    }

    private void BuildStyles()
    {
        var session = _session!;
        var document = session.Document;

        Header("SHARED STYLES", null);
        Note("Define a look once and apply it to many widgets. A widget's own properties win over its styles.");

        if (_selectedStyle is not null && !document.Styles.Contains(_selectedStyle)) _selectedStyle = null;
        _selectedStyle ??= document.Styles.FirstOrDefault();

        var list = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 4) };
        foreach (var style in document.Styles)
        {
            var chip = new ToggleButton
            {
                Content = style.Name,
                IsChecked = ReferenceEquals(style, _selectedStyle),
                Margin = new Thickness(0, 0, 6, 6),
                Padding = new Thickness(9, 3),
            };
            chip.Click += (_, _) =>
            {
                _selectedStyle = style;
                Rebuild();
            };
            list.Children.Add(chip);
        }

        var add = new Button { Content = "+ New style", Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(9, 3) };
        add.Classes.Add("ghost");
        add.Click += (_, _) =>
        {
            _selectedStyle = session.AddStyle();
            Rebuild();
        };
        list.Children.Add(add);
        _body.Children.Add(list);

        if (_selectedStyle is not { } selected) return;

        var users = document.Descendants().Count(n => n.Styles.Contains(selected.Name));
        Header("STYLE", users == 1 ? "1 widget" : $"{users} widgets");

        Text("Name", () => selected.Name, value =>
        {
            if (value is not null && session.RenameStyle(selected, value)) return null;
            return "Use a unique C# identifier";
        }, mono: true);

        void Edit(string property, Action apply) => session.EditStyle(selected, property, apply);

        Color("Background", () => selected.BackgroundColor, value => Edit("bg", () => selected.BackgroundColor = value));
        Int("Opacity 0-255", () => selected.BackgroundOpacity, v => Edit("opa", () => selected.BackgroundOpacity = v));
        Color("Text", () => selected.TextColor, value => Edit("text", () => selected.TextColor = value));
        Color("Border", () => selected.BorderColor, value => Edit("border-color", () => selected.BorderColor = value));
        IntPair("Border px", () => selected.BorderWidth, v => Edit("border", () => selected.BorderWidth = v),
            "Radius", () => selected.Radius, v => Edit("radius", () => selected.Radius = v));
        IntPair("Padding", () => selected.Padding, v => Edit("padding", () => selected.Padding = v),
            "Font size", () => selected.FontSize, v => Edit("font-size", () => selected.FontSize = v));
        Asset("Font", () => selected.Font, value => Edit("font", () => selected.Font = value), Dialogs.Fonts, null);

        var remove = new Button { Content = $"Delete {selected.Name}", Margin = new Thickness(0, 16, 0, 0) };
        remove.Classes.Add("danger");
        remove.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(this) is not Window window) return;
            var confirmed = users == 0 || await Dialogs.ConfirmAsync(window, "Delete style",
                $"{selected.Name} is used by {users} widget(s). They keep their own properties and lose this style.", "Delete style", destructive: true);
            if (!confirmed) return;

            session.RemoveStyle(selected);
            _selectedStyle = null;
            Rebuild();
        };
        _body.Children.Add(remove);
    }

    #endregion

    #region Field builders

    private void Header(string text, string? designator)
    {
        var row = new DockPanel { Margin = new Thickness(0, 16, 0, 6) };

        if (designator is not null)
        {
            var tag = new TextBlock { Text = designator, VerticalAlignment = VerticalAlignment.Center };
            tag.Classes.Add("designator");
            DockPanel.SetDock(tag, Dock.Right);
            row.Children.Add(tag);
        }

        var label = new TextBlock { Text = text };
        label.Classes.Add("head");
        row.Children.Add(label);

        _body.Children.Add(row);
    }

    private void Note(string text, string? brush = null)
    {
        var note = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) };
        note.Classes.Add("small");
        if (brush is null) note.Classes.Add("dim");
        else note.Foreground = Brush(brush);
        _body.Children.Add(note);
    }

    private void Action(string text, Action action)
    {
        var button = new Button { Content = text, Margin = new Thickness(0, 4, 0, 4) };
        button.Click += (_, _) => action();
        _body.Children.Add(button);
    }

    private Grid Row(string label, Control editor)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("96,*"), Margin = new Thickness(0, 3) };

        var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        text.Classes.Add("dim");
        row.Children.Add(text);

        Grid.SetColumn(editor, 1);
        row.Children.Add(editor);
        _body.Children.Add(row);
        return row;
    }

    /// <summary>
    /// A text field. <paramref name="apply"/> returns an error message to show, or null when the
    /// value was accepted.
    /// </summary>
    private void Text(string label, Func<string?> get, Func<string?, string?> apply, bool mono = false)
    {
        var box = new TextBox { Text = get() ?? string.Empty };
        if (mono) box.Classes.Add("mono");

        box.TextChanged += (_, _) =>
        {
            // Avalonia raises TextChanged after the fact, also for the value the field was built
            // with; that is not an edit and must not dirty the document or add an undo step.
            if (_building || box.Text == (get() ?? string.Empty)) return;
            var error = apply(box.Text);
            box.Classes.Set("invalid", error is not null);
            ToolTip.SetTip(box, error);
        };

        _refreshers.Add(() =>
        {
            if (!box.IsFocused) box.Text = get() ?? string.Empty;
        });

        Row(label, box);
    }

    private void Text(string label, Func<string?> get, Action<string?> apply, bool mono = false, Func<string?, string?>? validate = null) =>
        Text(label, get, value =>
        {
            var error = validate?.Invoke(value);
            if (error is null) apply(value);
            return error;
        }, mono);

    private TextBox IntBox(Func<int?> get, Action<int?> apply)
    {
        var box = new TextBox { Text = Format(get()), HorizontalContentAlignment = HorizontalAlignment.Right };
        box.Classes.Add("mono");

        box.TextChanged += (_, _) =>
        {
            if (_building || box.Text == Format(get())) return;
            var text = box.Text?.Trim();

            if (string.IsNullOrEmpty(text))
            {
                box.Classes.Set("invalid", false);
                apply(null);
            }
            else if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                box.Classes.Set("invalid", false);
                apply(value);
            }
            else
            {
                box.Classes.Set("invalid", true);
            }
        };

        // Arrow keys nudge, Shift for tens: the fastest way to align things by eye.
        box.KeyDown += (_, e) =>
        {
            var step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 10 : 1;
            var delta = e.Key switch { Key.Up => step, Key.Down => -step, _ => 0 };
            if (delta == 0) return;

            box.Text = ((get() ?? 0) + delta).ToString(CultureInfo.InvariantCulture);
            box.CaretIndex = box.Text.Length;
            e.Handled = true;
        };

        _refreshers.Add(() =>
        {
            if (!box.IsFocused) box.Text = Format(get());
        });

        return box;
    }

    private static string Format(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    private void Int(string label, Func<int?> get, Action<int?> apply) => Row(label, IntBox(get, apply));

    private void IntPair(string firstLabel, Func<int?> firstGet, Action<int?> firstApply, string secondLabel, Func<int?> secondGet, Action<int?> secondApply)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("96,*,10,Auto,6,*"), Margin = new Thickness(0, 3) };

        var first = new TextBlock { Text = firstLabel, VerticalAlignment = VerticalAlignment.Center };
        first.Classes.Add("dim");
        grid.Children.Add(first);

        var firstBox = IntBox(firstGet, firstApply);
        Grid.SetColumn(firstBox, 1);
        grid.Children.Add(firstBox);

        var second = new TextBlock { Text = secondLabel, VerticalAlignment = VerticalAlignment.Center };
        second.Classes.Add("dim");
        Grid.SetColumn(second, 3);
        grid.Children.Add(second);

        var secondBox = IntBox(secondGet, secondApply);
        Grid.SetColumn(secondBox, 5);
        grid.Children.Add(secondBox);

        _body.Children.Add(grid);
    }

    /// <summary>X, Y, W, H in one compact block, each with its letter in the mono face.</summary>
    private void Placement(UiNode node)
    {
        var session = _session!;
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("18,*,12,18,*"),
            RowDefinitions = new RowDefinitions("Auto,6,Auto"),
            Margin = new Thickness(0, 2, 0, 6),
        };

        void Cell(string letter, int row, int column, Func<int?> get, Action<int?> apply)
        {
            var tag = new TextBlock { Text = letter, VerticalAlignment = VerticalAlignment.Center };
            tag.Classes.Add("designator");
            Grid.SetRow(tag, row);
            Grid.SetColumn(tag, column);
            grid.Children.Add(tag);

            var box = IntBox(get, apply);
            Grid.SetRow(box, row);
            Grid.SetColumn(box, column + 1);
            grid.Children.Add(box);
        }

        Cell("X", 0, 0, () => node.X, v => session.Edit(node, "x", () => node.X = v ?? 0));
        Cell("Y", 0, 3, () => node.Y, v => session.Edit(node, "y", () => node.Y = v ?? 0));
        Cell("W", 2, 0, () => node.Width, v => session.Edit(node, "w", () => node.Width = v));
        Cell("H", 2, 3, () => node.Height, v => session.Edit(node, "h", () => node.Height = v));

        _body.Children.Add(grid);
    }

    private void Choice(string label, IReadOnlyList<string> options, Func<string> get, Action<string> apply)
    {
        var combo = new ComboBox { ItemsSource = options, SelectedItem = get(), HorizontalAlignment = HorizontalAlignment.Stretch };
        combo.SelectionChanged += (_, _) =>
        {
            if (_building || combo.SelectedItem is not string value || value == get()) return;
            apply(value);
        };

        _refreshers.Add(() =>
        {
            if (!combo.IsDropDownOpen) combo.SelectedItem = get();
        });

        Row(label, combo);
    }

    private void Check(string label, Func<bool> get, Action<bool> apply)
    {
        var check = new CheckBox { Content = label, IsChecked = get(), Margin = new Thickness(0, 3) };
        check.IsCheckedChanged += (_, _) =>
        {
            if (_building || (check.IsChecked == true) == get()) return;
            apply(check.IsChecked == true);
        };

        _refreshers.Add(() => check.IsChecked = get());
        _body.Children.Add(check);
    }

    private void Lines(string label, Func<List<string>> get, Action<List<string>> apply)
    {
        var caption = new TextBlock { Text = label, Margin = new Thickness(0, 4, 0, 4) };
        caption.Classes.Add("dim");
        caption.Classes.Add("small");
        _body.Children.Add(caption);

        var box = new TextBox
        {
            Text = string.Join(Environment.NewLine, get()),
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            MinHeight = 92,
            MaxHeight = 220,
        };
        box.Classes.Add("mono");

        box.TextChanged += (_, _) =>
        {
            if (_building || SplitLines(box.Text).SequenceEqual(get())) return;
            apply(SplitLines(box.Text));
        };

        _refreshers.Add(() =>
        {
            if (!box.IsFocused) box.Text = string.Join(Environment.NewLine, get());
        });

        _body.Children.Add(box);
    }

    private static List<string> SplitLines(string? text) => string.IsNullOrWhiteSpace(text)
        ? []
        : [.. text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    /// <summary>
    /// A colour: a swatch that opens a picker, and the hex text beside it. Empty means the
    /// LVGL theme's colour; a malformed value is refused rather than written into the document.
    /// </summary>
    private void Color(string label, Func<string?> get, Action<string?> apply)
    {
        var swatch = new Border
        {
            Width = 26,
            Height = 26,
            CornerRadius = new CornerRadius(4),
            BorderBrush = Brush("TraceBright"),
            BorderThickness = new Thickness(1),
        };

        var picker = new ColorView
        {
            IsAlphaEnabled = false,
            IsColorPaletteVisible = true,
            IsAccentColorsVisible = false,
            Width = 300,
        };

        var flyout = new Flyout { Content = picker, Placement = PlacementMode.LeftEdgeAlignedTop };
        var swatchButton = new Button
        {
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Content = swatch,
            Flyout = flyout,
        };
        ToolTip.SetTip(swatchButton, "Pick a colour");

        var box = new TextBox { PlaceholderText = "theme default", Margin = new Thickness(6, 0, 0, 0) };
        box.Classes.Add("mono");

        void Show(string? value)
        {
            if (value is not null && LvColor.TryParse(value, out var color))
            {
                swatch.Background = new SolidColorBrush(Avalonia.Media.Color.FromRgb(color.R, color.G, color.B));
            }
            else
            {
                swatch.Background = Brush("Mask");
            }
        }

        var updating = false;

        box.Text = get() ?? string.Empty;
        Show(get());

        box.TextChanged += (_, _) =>
        {
            if (_building || updating || box.Text == (get() ?? string.Empty)) return;
            var text = box.Text?.Trim();

            if (string.IsNullOrEmpty(text))
            {
                box.Classes.Set("invalid", false);
                apply(null);
                Show(null);
            }
            else if (LvColor.TryParse(text, out var color))
            {
                box.Classes.Set("invalid", false);
                apply(color.ToString());
                Show(color.ToString());
            }
            else
            {
                box.Classes.Set("invalid", true);
            }
        };

        picker.ColorChanged += (_, e) =>
        {
            if (_building || updating) return;
            var hex = $"#{e.NewColor.R:X2}{e.NewColor.G:X2}{e.NewColor.B:X2}";
            updating = true;
            box.Text = hex;
            updating = false;
            apply(hex);
            Show(hex);
        };

        flyout.Opening += (_, _) =>
        {
            if (get() is { } current && LvColor.TryParse(current, out var color))
            {
                updating = true;
                picker.Color = Avalonia.Media.Color.FromRgb(color.R, color.G, color.B);
                updating = false;
            }
        };

        _refreshers.Add(() =>
        {
            if (box.IsFocused) return;
            updating = true;
            box.Text = get() ?? string.Empty;
            updating = false;
            Show(get());
        });

        var editor = new DockPanel();
        DockPanel.SetDock(swatchButton, Dock.Left);
        editor.Children.Add(swatchButton);
        editor.Children.Add(box);
        Row(label, editor);
    }

    /// <summary>A file reference: shows the relative path, imports a file into the layout's folder.</summary>
    private void Asset(string label, Func<string?> get, Action<string?> apply, Avalonia.Platform.Storage.FilePickerFileType type, string? hint)
    {
        var path = new TextBlock
        {
            Text = get() ?? "none",
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        path.Classes.Add("mono");
        if (get() is null) path.Classes.Add("faint");

        var import = new Button { Content = "Import…", Padding = new Thickness(8, 3) };
        var clear = new Button { Content = "✕", Padding = new Thickness(6, 3), IsVisible = get() is not null };
        clear.Classes.Add("ghost");
        ToolTip.SetTip(clear, "Remove the file reference (the file stays on disk)");

        import.Click += async (_, _) =>
        {
            if (PickAsset is null) return;
            var relative = await PickAsset(label, type);
            if (relative is null) return;

            apply(relative);
            Rebuild();
        };

        clear.Click += (_, _) =>
        {
            apply(null);
            Rebuild();
        };

        var editor = new DockPanel();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Children = { clear, import } };
        DockPanel.SetDock(buttons, Dock.Right);
        editor.Children.Add(buttons);
        editor.Children.Add(path);
        Row(label, editor);

        if (hint is not null) Note(hint);
    }

    private static string? Normalize(string text) => LvColor.TryParse(text, out var color) ? color.ToString() : null;

    private static IBrush Brush(string key) =>
        Application.Current!.TryFindResource(key, out var value) && value is IBrush brush ? brush : Brushes.Gray;

    #endregion
}
