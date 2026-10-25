using System.Globalization;
using Lvgl.Gallery.Shell;
using Lvgl.Widgets;

namespace Lvgl.Gallery.Tools;

/// <summary>
/// Converts between units: pick a quantity, pick the two units, key in a value. Every other unit of
/// the same quantity is listed underneath, so one entry answers several questions.
/// </summary>
internal sealed class UnitConverter : Demo
{
    private LvLabel _input = null!;
    private LvLabel _output = null!;
    private LvDropdown _from = null!;
    private LvDropdown _to = null!;
    private LvObject _everyUnit = null!;
    private int _rowWidth;
    private readonly List<LvButton> _quantityButtons = [];
    private Quantity _quantity = UnitTables.Quantities[0];
    private string _entry = "1";

    public override Category Category => Category.Tools;
    public override string Title => "Unit converter";
    public override string Summary => "Length, mass, temperature, speed, data and energy - one value shown in every unit of its kind.";
    public override string BuiltWith => "LvDropdown  LvButton  LvLabel";

    protected override void Build(LvObject stage, int width, int height)
    {
        // Quantities down the left.
        var quantities = Bare(new LvPanel(stage, 150, height));
        quantities.SetFlexFlow(LvFlexFlow.Column);
        quantities.SetGap(6, 0);

        foreach (var quantity in UnitTables.Quantities)
        {
            var button = Button(quantities, quantity.Name, 150, 44, Palette.Card, Palette.Ink, 16);
            button.SetBorderWidth(1);
            button.SetBorderColor(Palette.Rule);
            var chosen = quantity;
            button.Clicked += (_, _) => Select(chosen);
            _quantityButtons.Add(button);
        }

        var x = 170;
        var cardWidth = (width - x - 56) / 2;

        // From and To, with a swap between them.
        var fromCard = Card(stage, x, 0, cardWidth, 128);
        Caption(fromCard, "From").SetPosition(18, 14);
        _input = Text(fromCard, _entry, 40, Face.Display, Palette.Ink);
        _input.SetPosition(18, 34);
        _from = UnitPicker(fromCard, cardWidth);

        var swap = IconButton(stage, LvSymbols.Loop, 40, 40, Tint, Accent, 16);
        swap.SetPosition(x + cardWidth + 8, 44);
        swap.SetRadius(20);
        swap.Clicked += (_, _) =>
        {
            (_from.SelectedIndex, _to.SelectedIndex) = (_to.SelectedIndex, _from.SelectedIndex);
            Recalculate();
        };

        var toCard = Box(stage, x + cardWidth + 56, 0, cardWidth, 128, Accent, 12);
        Text(toCard, "TO", 12, Face.Mono, Palette.White).SetPosition(18, 14);
        _output = Text(toCard, "", 40, Face.Display, Palette.White);
        _output.SetPosition(18, 34);
        _output.Width = cardWidth - 36;
        _output.LongMode = LvLabelLongMode.Dot;
        _to = UnitPicker(toCard, cardWidth);

        _from.ValueChanged += (_, _) => Recalculate();
        _to.ValueChanged += (_, _) => Recalculate();

        // Every unit, then the keypad.
        var listWidth = width - x - 290;
        var list = Card(stage, x, 144, listWidth, height - 144);
        Caption(list, "In every unit").SetPosition(18, 14);
        _rowWidth = listWidth - 36;
        _everyUnit = Bare(new LvPanel(list, _rowWidth, height - 144 - 48));
        _everyUnit.SetPosition(18, 40);
        _everyUnit.SetFlexFlow(LvFlexFlow.Column);
        _everyUnit.SetGap(6, 0);

        var keypad = Bare(new LvPanel(stage, 270, height - 144));
        keypad.SetPosition(width - 270, 144);
        keypad.SetFlexFlow(LvFlexFlow.RowWrap);
        keypad.SetGap(8, 8);

        var keyWidth = (270 - 16) / 3;
        var keyHeight = (height - 144 - 24) / 4;
        foreach (var key in new[] { "7", "8", "9", "4", "5", "6", "1", "2", "3", "±", "0", "." })
        {
            var button = Button(keypad, key, keyWidth, keyHeight, Palette.Card, Palette.Ink, 22);
            button.SetBorderWidth(1);
            button.SetBorderColor(Palette.Rule);
            var pressed = key;
            button.Clicked += (_, _) => Key(pressed);
        }

        var clear = Button(fromCard, "Clear", 64, 26, Tint, Accent, 13);
        clear.Align(LvAlign.TopRight, -12, 10);
        clear.Clicked += (_, _) => { _entry = "0"; Recalculate(); };

        Select(Context.Screenshot ? UnitTables.Quantities[2] : _quantity);
        if (Context.Screenshot) { _entry = "21.5"; _to.SelectedIndex = 1; Recalculate(); }
    }

    private LvDropdown UnitPicker(LvObject card, int cardWidth)
    {
        var picker = new LvDropdown(card);
        picker.SetSize(cardWidth - 36, 34);
        picker.Align(LvAlign.BottomLeft, 18, -12);
        // Montserrat, because the dropdown draws its arrow from the font's symbol range.
        picker.SetFont(Drawing.LvFont.Montserrat(14));
        picker.SetRadius(8);
        return picker;
    }

    private void Select(Quantity quantity)
    {
        _quantity = quantity;
        var names = quantity.Units.Select(u => $"{u.Name} ({u.Symbol})").ToArray();
        _from.Options = names;
        _to.Options = names;
        _from.SelectedIndex = 0;
        _to.SelectedIndex = Math.Min(1, names.Length - 1);

        for (var i = 0; i < _quantityButtons.Count; i++)
        {
            var on = UnitTables.Quantities[i] == quantity;
            _quantityButtons[i].SetBackgroundColor(on ? Accent : Palette.Card);
            _quantityButtons[i].SetTextColor(on ? Palette.White : Palette.Ink);
            _quantityButtons[i].SetBorderWidth(on ? 0 : 1);
        }

        Recalculate();
    }

    private void Key(string key)
    {
        if (key == "±") _entry = _entry.StartsWith('-') ? _entry[1..] : "-" + _entry;
        else if (key == "." && _entry.Contains('.')) return;
        else _entry = _entry == "0" && key != "." ? key : _entry + key;

        if (_entry.Length > 12) _entry = _entry[..12];
        Recalculate();
    }

    private void Recalculate()
    {
        _input.Text = _entry;
        if (!double.TryParse(_entry, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) value = 0;

        var from = _quantity.Units[_from.SelectedIndex];
        var to = _quantity.Units[_to.SelectedIndex];
        _output.Text = $"{UnitTables.Format(UnitTables.Convert(value, from, to))} {to.Symbol}";

        _everyUnit.Clear();
        foreach (var unit in _quantity.Units)
        {
            var row = Bare(new LvPanel(_everyUnit, _rowWidth, 22));
            Text(row, unit.Name, 15, Face.Body, unit == to ? Accent : Palette.InkMuted).Align(LvAlign.LeftMid);
            Text(row, $"{UnitTables.Format(UnitTables.Convert(value, from, unit))} {unit.Symbol}", 13, Face.Mono,
                unit == to ? Accent : Palette.Ink).Align(LvAlign.RightMid);
        }
    }
}

internal sealed record Unit(string Name, string Symbol, double Factor, double Offset = 0);

internal sealed record Quantity(string Name, Unit[] Units);

/// <summary>
/// Conversion through a base unit per quantity: <c>base = value * Factor + Offset</c>. The offset
/// is what temperature needs; everything else is a pure scale.
/// </summary>
internal static class UnitTables
{
    public static readonly Quantity[] Quantities =
    [
        new("Length", [new("Metre", "m", 1), new("Foot", "ft", 0.3048), new("Inch", "in", 0.0254), new("Kilometre", "km", 1000), new("Mile", "mi", 1609.344), new("Centimetre", "cm", 0.01)]),
        new("Mass", [new("Kilogram", "kg", 1), new("Pound", "lb", 0.45359237), new("Ounce", "oz", 0.028349523125), new("Gram", "g", 0.001), new("Tonne", "t", 1000)]),
        new("Temperature", [new("Celsius", "°C", 1), new("Fahrenheit", "°F", 5.0 / 9, -32 * 5.0 / 9), new("Kelvin", "K", 1, -273.15)]),
        new("Speed", [new("Metres per second", "m/s", 1), new("Kilometres per hour", "km/h", 1 / 3.6), new("Miles per hour", "mph", 0.44704), new("Knot", "kn", 0.514444)]),
        new("Data", [new("Byte", "B", 1), new("Kibibyte", "KiB", 1024), new("Mebibyte", "MiB", 1024 * 1024), new("Gibibyte", "GiB", 1024d * 1024 * 1024), new("Kilobyte", "kB", 1000), new("Megabyte", "MB", 1e6)]),
        new("Energy", [new("Joule", "J", 1), new("Kilowatt hour", "kWh", 3.6e6), new("Calorie", "cal", 4.184), new("Kilocalorie", "kcal", 4184), new("Watt hour", "Wh", 3600)]),
    ];

    public static double Convert(double value, Unit from, Unit to) => (value * from.Factor + from.Offset - to.Offset) / to.Factor;

    public static string Format(double value)
    {
        var magnitude = Math.Abs(value);
        if (magnitude != 0 && (magnitude >= 1e9 || magnitude < 1e-4)) return value.ToString("0.###E+0", CultureInfo.InvariantCulture);
        return Math.Round(value, magnitude >= 100 ? 2 : 4).ToString("#,0.####", CultureInfo.InvariantCulture);
    }
}
