using Lvgl.Drawing;
using Lvgl.Gallery.Shell;
using Lvgl.Widgets;

namespace Lvgl.Gallery.Scientific;

/// <summary>
/// Plots a family of functions on graph paper and redraws as the sliders move. The Fourier family
/// shows a square wave assembled from its odd harmonics - drag "Terms" to watch it sharpen.
/// </summary>
internal sealed class FunctionPlotter : Demo
{
    private static readonly string[] Families = ["Fourier", "Damped", "Sinc", "Gaussian", "Lissajous"];

    private LvCanvas _plot = null!;
    private LvLabel _formula = null!;
    private LvSlider _first = null!, _second = null!;
    private LvLabel _firstValue = null!, _secondValue = null!, _firstName = null!, _secondName = null!;
    private readonly List<LvButton> _familyButtons = [];
    private int _family;

    private uint _ink, _grid, _axis, _paper;

    public override Category Category => Category.Scientific;
    public override string Title => "Function plotter";
    public override string Summary => "Five function families drawn pixel by pixel on a canvas; every slider move redraws the plot.";
    public override string BuiltWith => "LvCanvas  LvSlider  LvButton";

    protected override void Build(LvObject stage, int width, int height)
    {
        _ink = LvCanvas.Argb(Accent);
        _grid = LvCanvas.Argb(Categories.Mix(Accent, Palette.White, 0.88));
        _axis = LvCanvas.Argb(Categories.Mix(Accent, Palette.White, 0.55));
        _paper = LvCanvas.Argb(Palette.Card);

        var plotWidth = width - 250;
        var frame = Card(stage, 0, 0, plotWidth, height);
        _plot = new LvCanvas(frame, plotWidth - 2, height - 52);
        _plot.SetPosition(1, 1);

        _formula = Text(frame, "", 13, Face.Mono, Palette.Ink);
        _formula.Align(LvAlign.BottomLeft, 16, -14);

        var range = Text(frame, "x: -2pi .. 2pi", 12, Face.Mono, Palette.InkFaint);
        range.Align(LvAlign.TopRight, -10, 8);

        // Controls on the right.
        var controls = Bare(new LvPanel(stage, 230, height));
        controls.SetPosition(width - 230, 0);

        Caption(controls, "Family").SetPosition(0, 0);
        var families = Bare(new LvPanel(controls, 230, 160));
        families.SetPosition(0, 22);
        families.SetFlexFlow(LvFlexFlow.RowWrap);
        families.SetGap(8, 8);

        for (var i = 0; i < Families.Length; i++)
        {
            var button = Button(families, Families[i], 111, 44, Palette.Card, Palette.Ink, 15);
            button.SetBorderWidth(1);
            button.SetBorderColor(Palette.Rule);
            var index = i;
            button.Clicked += (_, _) => SelectFamily(index);
            _familyButtons.Add(button);
        }

        (_firstName, _firstValue, _first) = Parameter(controls, 196);
        (_secondName, _secondValue, _second) = Parameter(controls, 290);

        _first.ValueChanged += (_, _) => Redraw();
        _second.ValueChanged += (_, _) => Redraw();

        SelectFamily(0);
        if (Context.Screenshot) { _first.SetValue(7, animate: false); Redraw(); }
    }

    private (LvLabel Name, LvLabel Value, LvSlider Slider) Parameter(LvObject parent, int y)
    {
        var name = Caption(parent, "");
        name.SetPosition(0, y);

        var value = Text(parent, "", 22, Face.Display, Accent);
        value.Align(LvAlign.TopRight, 0, y - 6);

        var slider = new LvSlider(parent);
        slider.SetSize(210, 8);
        slider.SetPosition(8, y + 42);
        slider.SetBackgroundColor(Palette.Sunken);
        slider.SetBackgroundColor(Accent, LvPart.Indicator);
        slider.SetBackgroundColor(Accent, LvPart.Knob);
        return (name, value, slider);
    }

    private void SelectFamily(int index)
    {
        _family = index;
        for (var i = 0; i < _familyButtons.Count; i++)
        {
            _familyButtons[i].SetBackgroundColor(i == index ? Accent : Palette.Card);
            _familyButtons[i].SetTextColor(i == index ? Palette.White : Palette.Ink);
            _familyButtons[i].SetBorderWidth(i == index ? 0 : 1);
        }

        // Each family names its two parameters and their ranges.
        (string, int, int, int, string, int, int, int) spec = index switch
        {
            0 => ("Terms", 1, 25, 3, "Frequency", 1, 4, 1),
            1 => ("Frequency", 1, 12, 4, "Damping", 0, 100, 25),
            2 => ("Width", 1, 20, 6, "Frequency", 1, 8, 2),
            3 => ("Spread", 5, 100, 35, "Offset", -50, 50, 0),
            _ => ("Ratio a", 1, 7, 3, "Ratio b", 1, 7, 2),
        };

        _firstName.Text = spec.Item1.ToUpperInvariant();
        _first.SetRange(spec.Item2, spec.Item3);
        _first.SetValue(spec.Item4, animate: false);
        _secondName.Text = spec.Item5.ToUpperInvariant();
        _second.SetRange(spec.Item6, spec.Item7);
        _second.SetValue(spec.Item8, animate: false);
        Redraw();
    }

    private void Redraw()
    {
        int a = _first.Value, b = _second.Value;
        _firstValue.Text = a.ToString();
        _secondValue.Text = b.ToString();

        var w = _plot.BufferWidth;
        var h = _plot.BufferHeight;
        DrawPaper(w, h);

        if (_family == 4)
        {
            // Lissajous: a parametric curve, x = sin(a t + pi/2), y = sin(b t).
            _formula.Text = $"x = sin({a}t + pi/2),  y = sin({b}t)";
            int? lastX = null, lastY = null;
            for (var i = 0; i <= 2000; i++)
            {
                var t = i / 2000.0 * 2 * Math.PI;
                var px = (int)(w / 2 + Math.Sin(a * t + Math.PI / 2) * (h / 2 - 24));
                var py = (int)(h / 2 - Math.Sin(b * t) * (h / 2 - 24));
                if (lastX is { } lx && lastY is { } ly) Stroke(lx, ly, px, py);
                (lastX, lastY) = (px, py);
            }
        }
        else
        {
            _formula.Text = _family switch
            {
                0 => $"f(x) = 4/pi * sum[k=1..{a}] sin((2k-1){(b == 1 ? "" : b)}x)/(2k-1)",
                1 => $"f(x) = e^(-{b / 100.0:0.00}|x|) cos({a / 2.0:0.#}x)",
                2 => $"f(x) = sin({b}x / {a / 4.0:0.##}) / x",
                3 => $"f(x) = e^(-(x - {b / 10.0:0.0})^2 / {a / 10.0:0.0})",
                _ => "",
            };

            int? last = null;
            for (var px = 0; px < w; px++)
            {
                var x = (px / (double)(w - 1) - 0.5) * 4 * Math.PI;
                var y = Evaluate(x, a, b);
                var py = (int)(h / 2 - y * (h / 2 - 24) / 1.4);
                if (last is { } previous) Stroke(px - 1, previous, px, py);
                last = py;
            }
        }

        _plot.Invalidate();
    }

    private double Evaluate(double x, int a, int b) => _family switch
    {
        0 => Fourier(x, a, b),
        1 => Math.Exp(-b / 100.0 * Math.Abs(x)) * Math.Cos(a / 2.0 * x),
        2 => Math.Abs(x) < 1e-9 ? b / (a / 4.0) * 0.25 : Math.Sin(b * x / (a / 4.0)) / x * 0.25,
        _ => Math.Exp(-Math.Pow(x - b / 10.0, 2) / (a / 10.0)),
    };

    // 4/pi * sum of odd harmonics: converges on a square wave of amplitude 1.
    private static double Fourier(double x, int terms, int frequency)
    {
        var sum = 0.0;
        for (var k = 1; k <= terms; k++)
        {
            var n = 2 * k - 1;
            sum += Math.Sin(n * frequency * x) / n;
        }

        return 4 / Math.PI * sum;
    }

    private void DrawPaper(int w, int h)
    {
        _plot.Fill(_paper);

        // Fine grid every 1/4 unit of pi, axes through the origin.
        for (var i = -8; i <= 8; i++)
        {
            var px = (int)(w / 2 + i * (w - 1) / 16.0);
            _plot.FillRect(px, 0, 1, h, i == 0 ? _axis : _grid);
        }

        var step = (h / 2 - 24) / 1.4 / 2;
        for (var i = -6; i <= 6; i++)
        {
            var py = (int)(h / 2 + i * step);
            _plot.FillRect(0, py, w, 1, i == 0 ? _axis : _grid);
        }
    }

    // A two-pixel stroke: Bresenham with a neighbour below and to the right.
    private void Stroke(int x0, int y0, int x1, int y1)
    {
        _plot.DrawLine(x0, y0, x1, y1, _ink);
        _plot.DrawLine(x0 + 1, y0, x1 + 1, y1, _ink);
        _plot.DrawLine(x0, y0 + 1, x1, y1 + 1, _ink);
    }
}
