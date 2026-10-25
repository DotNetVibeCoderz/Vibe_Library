using Lvgl.Drawing;
using Lvgl.Events;
using Lvgl.Gallery.Shell;
using Lvgl.Input;
using Lvgl.Widgets;

namespace Lvgl.Gallery.Tools;

/// <summary>
/// A calculator with a paper tape: every result is printed onto a roll on the left, newest at the
/// bottom, the way a desk adding machine keeps its history. Tap or type.
/// </summary>
internal sealed class Calculator : Demo
{
    private const string Times = "×";
    private const string Divide = "÷";
    private const string Minus = "−";

    private static readonly string[] Keys =
    [
        "C", "(", ")", Divide,
        "7", "8", "9", Times,
        "4", "5", "6", Minus,
        "1", "2", "3", "+",
        "0", ".", LvSymbols.Backspace, "=",
    ];

    private readonly List<string> _tape = [];
    private string _entry = "";
    private bool _evaluated;
    private LvLabel _expression = null!;
    private LvLabel _result = null!;
    private LvLabel _tapeText = null!;
    private LvPanel _tapeRoll = null!;

    public override Category Category => Category.Tools;
    public override string Title => "Calculator";
    public override string Summary => "Arithmetic with brackets and a printed tape of every result. Type on a keyboard or tap the keys.";
    public override string BuiltWith => "LvButton  LvLabel  LvGroup  LvKey";

    protected override void Build(LvObject stage, int width, int height)
    {
        var keypadWidth = Math.Min(380, width / 2);
        var tapeWidth = width - keypadWidth - 20;

        // The tape: a paper roll that feeds upward, newest result at the bottom edge.
        _tapeRoll = Card(stage, 0, 0, tapeWidth, height);
        _tapeRoll.SetBackgroundColor(LvColor.FromRgb(0xFCFBF7u));

        Caption(_tapeRoll, "Tape").SetPosition(20, 16);
        Box(_tapeRoll, 20, 40, tapeWidth - 40, 1, Palette.Rule);

        _tapeText = Text(_tapeRoll, "Results print here.", 14, Face.Mono, Palette.InkFaint);
        _tapeText.Width = tapeWidth - 40;
        _tapeText.SetTextAlign(LvTextAlign.Right);
        _tapeText.Align(LvAlign.BottomRight, -20, -18);

        // Display: what is being typed above, the running value below in large type.
        var display = Box(stage, tapeWidth + 20, 0, keypadWidth, 112, Palette.Ink, 14);

        _expression = Text(display, "", 18, Face.Mono, Palette.InkFaint);
        _expression.Width = keypadWidth - 32;
        _expression.SetTextAlign(LvTextAlign.Right);
        _expression.LongMode = LvLabelLongMode.Clip;
        _expression.Align(LvAlign.TopRight, -16, 14);

        _result = Text(display, "0", 52, Face.Display, Palette.White);
        _result.Align(LvAlign.BottomRight, -16, -6);

        // Keypad: a 4 x 5 grid. Operators wear the category colour, digits stay quiet.
        var keypad = Bare(new LvPanel(stage, keypadWidth, height - 124));
        keypad.SetPosition(tapeWidth + 20, 124);
        keypad.SetFlexFlow(LvFlexFlow.RowWrap);
        keypad.SetGap(8, 8);

        var keyWidth = (keypadWidth - 3 * 8) / 4;
        var keyHeight = (height - 124 - 4 * 8) / 5;

        foreach (var key in Keys)
        {
            var isOperator = key is Divide or Times or Minus or "+" or "=";
            var isFunction = key is "C" or "(" or ")" || key == LvSymbols.Backspace;

            var fill = isOperator ? Accent : isFunction ? Tint : Palette.Card;
            var ink = isOperator ? Palette.White : isFunction ? Accent : Palette.Ink;

            var button = key == LvSymbols.Backspace
                ? IconButton(keypad, key, keyWidth, keyHeight, fill, ink)
                : Button(keypad, key, keyWidth, keyHeight, fill, ink, size: 24);
            if (fill == Palette.Card) { button.SetBorderWidth(1); button.SetBorderColor(Palette.Rule); }
            if (key == "=") button.SetFont(Fonts[Face.Display, 30]);

            var pressed = key;
            button.Clicked += (_, _) => Press(pressed);
        }

        // Keyboard input: the stage joins the focus group and maps keys onto the same buttons.
        stage.AddHandler(LvEventCode.Key, OnKey);
        App.FocusGroup?.Add(stage);
        LvGroup.Focus(stage);

        if (Context.Screenshot)
        {
            foreach (var key in "1250*0.15=") Type(key);
            foreach (var key in "(64+36)/8=") Type(key);
            foreach (var key in "19.99*3") Type(key);
        }
    }

    private void OnKey(object? sender, LvEventArgs e)
    {
        switch (e.Key)
        {
            case LvKey.Enter: Press("="); break;
            case LvKey.Backspace or LvKey.Delete: Press(LvSymbols.Backspace); break;
            case LvKey.Escape: Press("C"); break;
            default: if (e.Key < 128) Type((char)e.Key); break;
        }
    }

    private void Type(char key)
    {
        var mapped = key switch { '*' or 'x' => Times, '/' => Divide, '-' => Minus, '=' => "=", 'c' => "C", _ => key.ToString() };
        if (Array.IndexOf(Keys, mapped) >= 0) Press(mapped);
    }

    private void Press(string key)
    {
        switch (key)
        {
            case "C":
                _entry = "";
                break;
            case "=":
                if (_entry.Length == 0) return;
                var value = CalculatorEngine.TryEvaluate(Normalise(_entry), out var result) ? CalculatorEngine.Format(result) : "Error";
                Print($"{_entry}\n= {value}");
                _entry = value == "Error" ? "" : value;
                _expression.Text = "";
                _result.Text = value;
                _evaluated = true;
                return;
            default:
                // After "=", an operator carries the result on; anything else starts afresh.
                if (_evaluated && (char.IsDigit(key[0]) || key is "(" or ".")) _entry = "";
                if (key == LvSymbols.Backspace) { if (_entry.Length > 0) _entry = _entry[..^1]; }
                else _entry += key;
                break;
        }

        _evaluated = false;
        _expression.Text = _entry;
        _result.Text = CalculatorEngine.TryEvaluate(Normalise(_entry), out var preview) ? CalculatorEngine.Format(preview) : (_entry.Length == 0 ? "0" : _result.Text);
    }

    private void Print(string line)
    {
        _tape.Add(line);
        // Keep what fits; older entries have fed off the top of the roll.
        if (_tape.Count > 6) _tape.RemoveAt(0);
        _tapeText.Text = string.Join("\n\n", _tape);
        _tapeText.SetTextColor(Palette.InkMuted);
    }

    private static string Normalise(string text) =>
        text.Replace(Times, "*", StringComparison.Ordinal).Replace(Divide, "/", StringComparison.Ordinal).Replace(Minus, "-", StringComparison.Ordinal);
}

/// <summary>
/// A recursive-descent evaluator for + - * / and brackets, with unary minus. Kept apart from the
/// UI so it can be tested on its own.
/// </summary>
internal static class CalculatorEngine
{
    public static bool TryEvaluate(string text, out double value)
    {
        var position = 0;
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        try
        {
            value = Sum(text, ref position);
            return position == text.Length && double.IsFinite(value);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static string Format(double value) =>
        Math.Abs(value) >= 1e12 || (value != 0 && Math.Abs(value) < 1e-6)
            ? value.ToString("0.######E+0", System.Globalization.CultureInfo.InvariantCulture)
            : Math.Round(value, 10).ToString("0.##########", System.Globalization.CultureInfo.InvariantCulture);

    private static double Sum(string s, ref int i)
    {
        var value = Product(s, ref i);
        while (i < s.Length && s[i] is '+' or '-')
        {
            var op = s[i++];
            var right = Product(s, ref i);
            value = op == '+' ? value + right : value - right;
        }

        return value;
    }

    private static double Product(string s, ref int i)
    {
        var value = Unary(s, ref i);
        while (i < s.Length && s[i] is '*' or '/')
        {
            var op = s[i++];
            var right = Unary(s, ref i);
            value = op == '*' ? value * right : value / right;
        }

        return value;
    }

    private static double Unary(string s, ref int i)
    {
        if (i < s.Length && s[i] == '-') { i++; return -Unary(s, ref i); }
        if (i < s.Length && s[i] == '+') { i++; return Unary(s, ref i); }
        return Atom(s, ref i);
    }

    private static double Atom(string s, ref int i)
    {
        if (i < s.Length && s[i] == '(')
        {
            i++;
            var inner = Sum(s, ref i);
            if (i >= s.Length || s[i] != ')') throw new FormatException("Unclosed bracket.");
            i++;
            return inner;
        }

        var start = i;
        while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.')) i++;
        if (start == i) throw new FormatException("Expected a number.");
        return double.Parse(s.AsSpan(start, i - start), System.Globalization.CultureInfo.InvariantCulture);
    }
}
