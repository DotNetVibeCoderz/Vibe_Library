using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Lvgl;
using Lvgl.Drawing;
using Lvgl.Ui;

namespace Lvgl.Designer;

/// <summary>
/// Editable view over a <see cref="UiNode"/> for the property panel.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="UiNode"/> is a plain serialisable record of the layout and deliberately has no
/// change notification - it is shared with the runtime loader and the code generator, neither of
/// which wants a WPF dependency. This wrapper adds the notification and the string-to-value
/// parsing the UI needs.
/// </para>
/// <para>
/// Optional properties are surfaced as text so that an empty box means "leave at the LVGL default"
/// rather than "set to zero", which is the distinction <see cref="UiNode"/>'s nullable fields
/// carry.
/// </para>
/// </remarks>
internal sealed class NodeViewModel : INotifyPropertyChanged
{
    private readonly UiNode _node;

    public NodeViewModel(UiNode node) => _node = node;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised after any edit, so the window can rebuild the preview.</summary>
    public event EventHandler? Edited;

    /// <summary>The wrapped node.</summary>
    public UiNode Node => _node;

    public string TypeName => _node.Type.ToString();

    public string? Name
    {
        get => _node.Name;
        set => Set(() => _node.Name = string.IsNullOrWhiteSpace(value) ? null : value.Trim());
    }

    /// <summary>Null when the name is unusable as a generated C# field.</summary>
    public string? NameError => string.IsNullOrEmpty(_node.Name) || UiDocument.IsValidIdentifier(_node.Name)
        ? null
        : "Not a valid C# identifier";

    public string X
    {
        get => _node.X.ToString(CultureInfo.InvariantCulture);
        set => Set(() => _node.X = ParseInt(value) ?? 0);
    }

    public string Y
    {
        get => _node.Y.ToString(CultureInfo.InvariantCulture);
        set => Set(() => _node.Y = ParseInt(value) ?? 0);
    }

    public string Width
    {
        get => Optional(_node.Width);
        set => Set(() => _node.Width = ParseInt(value));
    }

    public string Height
    {
        get => Optional(_node.Height);
        set => Set(() => _node.Height = ParseInt(value));
    }

    public IReadOnlyList<string> AlignOptions { get; } =
        ["(absolute)", .. Enum.GetNames<LvAlign>()];

    public string Align
    {
        get => _node.Align?.ToString() ?? "(absolute)";
        set => Set(() => _node.Align = Enum.TryParse<LvAlign>(value, out var align) ? align : null);
    }

    public string Text
    {
        get => _node.Text ?? string.Empty;
        set => Set(() => _node.Text = string.IsNullOrEmpty(value) ? null : value);
    }

    public string BackgroundColor
    {
        get => _node.BackgroundColor ?? string.Empty;
        set => Set(() => _node.BackgroundColor = NormalizeColor(value));
    }

    public string TextColor
    {
        get => _node.TextColor ?? string.Empty;
        set => Set(() => _node.TextColor = NormalizeColor(value));
    }

    public string BorderColor
    {
        get => _node.BorderColor ?? string.Empty;
        set => Set(() => _node.BorderColor = NormalizeColor(value));
    }

    public string BorderWidth
    {
        get => Optional(_node.BorderWidth);
        set => Set(() => _node.BorderWidth = ParseInt(value));
    }

    public string Radius
    {
        get => Optional(_node.Radius);
        set => Set(() => _node.Radius = ParseInt(value));
    }

    public string Padding
    {
        get => Optional(_node.Padding);
        set => Set(() => _node.Padding = ParseInt(value));
    }

    public string FontSize
    {
        get => Optional(_node.FontSize);
        set => Set(() => _node.FontSize = ParseInt(value));
    }

    public string Value
    {
        get => Optional(_node.Value);
        set => Set(() => _node.Value = ParseInt(value));
    }

    public string Minimum
    {
        get => Optional(_node.Minimum);
        set => Set(() => _node.Minimum = ParseInt(value));
    }

    public string Maximum
    {
        get => Optional(_node.Maximum);
        set => Set(() => _node.Maximum = ParseInt(value));
    }

    public bool Hidden
    {
        get => _node.Hidden;
        set => Set(() => _node.Hidden = value);
    }

    /// <summary>Drop-down and roller entries, one per line.</summary>
    public string Options
    {
        get => string.Join(Environment.NewLine, _node.Options);
        set => Set(() => _node.Options = SplitLines(value));
    }

    /// <summary>Chart series colours, one <c>#RRGGBB</c> per line.</summary>
    public string SeriesColors
    {
        get => string.Join(Environment.NewLine, _node.SeriesColors);
        set => Set(() => _node.SeriesColors = SplitLines(value).Select(NormalizeColor).OfType<string>().ToList());
    }

    public IReadOnlyList<string> ChartTypeOptions { get; } = ["(default)", .. Enum.GetNames<LvChartType>()];

    public string ChartType
    {
        get => _node.ChartType?.ToString() ?? "(default)";
        set => Set(() => _node.ChartType = Enum.TryParse<LvChartType>(value, out var type) ? type : null);
    }

    public string PointCount
    {
        get => Optional(_node.PointCount);
        set => Set(() => _node.PointCount = ParseInt(value));
    }

    // Visibility helpers for the property panel; simpler than a converter per property group.
    public bool ShowText => _node.Type is UiWidgetType.Label or UiWidgetType.Button or UiWidgetType.Checkbox or UiWidgetType.TextArea;

    public bool ShowRange => _node.Type is UiWidgetType.Slider or UiWidgetType.Bar or UiWidgetType.Arc or UiWidgetType.Chart;

    public bool ShowOptions => _node.Type is UiWidgetType.Dropdown or UiWidgetType.Roller;

    public bool ShowChart => _node.Type is UiWidgetType.Chart;

    private static string Optional(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    private static int? ParseInt(string? text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static List<string> SplitLines(string? text) => string.IsNullOrWhiteSpace(text)
        ? []
        : [.. text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    /// <summary>Keeps a malformed colour out of the document instead of failing validation later.</summary>
    private static string? NormalizeColor(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        return LvColor.TryParse(text, out var color) ? color.ToString() : null;
    }

    private void Set(Action apply, [CallerMemberName] string? propertyName = null)
    {
        apply();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NameError)));
        Edited?.Invoke(this, EventArgs.Empty);
    }
}
