using Lvgl.Gallery.Shell;
using Lvgl.Widgets;

namespace Lvgl.Gallery.Scientific;

/// <summary>
/// The Mandelbrot set, rendered a band of rows at a time so the interface stays responsive on a
/// Raspberry Pi. Tap anywhere to zoom in there; the escape counts are smoothed into a gradient.
/// </summary>
internal sealed class Mandelbrot : Demo
{
    private const int RowsPerTick = 12;

    private static readonly (string Name, uint[] Stops)[] Palettes =
    [
        ("Ink", [0x0B1026, 0x2F62E8, 0xF4F7FF, 0xF2A516, 0x0B1026]),
        ("Ember", [0x14060A, 0x8E1B3B, 0xF06B2B, 0xFFE8B0, 0x14060A]),
        ("Lagoon", [0x02141A, 0x0A8C98, 0xBDF2E6, 0x2F62E8, 0x02141A]),
    ];

    private LvCanvas _canvas = null!;
    private LvLabel _position = null!, _zoomLabel = null!, _status = null!;
    private readonly List<LvButton> _paletteButtons = [];
    private uint[] _gradient = [];
    private double _centreX = -0.6, _centreY = 0, _scale = 3.2;   // scale = width of the view in the plane
    private int _maxIterations = 160;
    private int _nextRow;
    private long _renderStarted;

    public override Category Category => Category.Scientific;
    public override string Title => "Mandelbrot";
    public override string Summary => "Tap to zoom. Rendered in bands of rows between frames, so the screen stays responsive while it draws.";
    public override string BuiltWith => "LvCanvas  LvTimer  LvEventArgs.TryGetPointer";

    protected override void Build(LvObject stage, int width, int height)
    {
        var canvasWidth = width - 230;
        _canvas = new LvCanvas(stage, canvasWidth, height);
        _canvas.SetRadius(14);
        _canvas.IsClickable = true;
        _canvas.Clicked += (_, e) =>
        {
            if (!e.TryGetPointer(out var x, out var y)) return;
            var bounds = _canvas.GetBounds();
            ZoomAt(x - bounds.X1, y - bounds.Y1, 0.35);
        };

        var panel = Bare(new LvPanel(stage, 210, height));
        panel.SetPosition(width - 210, 0);

        Caption(panel, "Centre").SetPosition(0, 0);
        _position = Text(panel, "", 14, Face.Mono, Palette.Ink);
        _position.SetPosition(0, 20);

        Caption(panel, "Magnification").SetPosition(0, 68);
        _zoomLabel = Text(panel, "", 30, Face.Display, Accent);
        _zoomLabel.SetPosition(0, 86);

        Caption(panel, "Colours").SetPosition(0, 140);
        for (var i = 0; i < Palettes.Length; i++)
        {
            var button = Button(panel, Palettes[i].Name, 64, 36, Palette.Card, Palette.Ink, 14);
            button.SetPosition(i * 73, 162);
            button.SetBorderColor(Palette.Rule);
            var index = i;
            button.Clicked += (_, _) => { UsePalette(index); Restart(); };
            _paletteButtons.Add(button);
        }

        var zoomOut = Button(panel, "Zoom out", 210, 44, Tint, Accent, 16);
        zoomOut.SetPosition(0, 222);
        zoomOut.Clicked += (_, _) => ZoomAt(_canvas.BufferWidth / 2, _canvas.BufferHeight / 2, 2.5);

        var reset = Button(panel, "Whole set", 210, 44, Palette.Card, Palette.Ink, 16);
        reset.SetBorderWidth(1);
        reset.SetBorderColor(Palette.Rule);
        reset.SetPosition(0, 274);
        reset.Clicked += (_, _) => { (_centreX, _centreY, _scale) = (-0.6, 0, 3.2); Restart(); };

        _status = Text(panel, "", 12, Face.Mono, Palette.InkMuted);
        _status.Align(LvAlign.BottomLeft, 0, 0);

        UsePalette(0);

        if (Context.Screenshot)
        {
            // Seahorse valley, drawn in one go.
            (_centreX, _centreY, _scale) = (-0.7435, 0.1314, 0.012);
            Restart();
            while (_nextRow < _canvas.BufferHeight) RenderBand(_canvas.BufferHeight);
        }
        else
        {
            Restart();
        }

        Every(16, () => RenderBand(RowsPerTick));
    }

    private void ZoomAt(int px, int py, double factor)
    {
        var (x, y) = ToPlane(px, py);
        (_centreX, _centreY) = (x, y);
        _scale *= factor;
        Restart();
    }

    private void Restart()
    {
        // Deeper zooms need more iterations to resolve the boundary.
        _maxIterations = (int)Math.Clamp(120 + 60 * Math.Log2(3.2 / _scale), 120, 1500);
        _nextRow = 0;
        _renderStarted = Environment.TickCount64;

        _position.Text = $"{_centreX:+0.000000;-0.000000}\n{_centreY:+0.000000;-0.000000}i";
        var magnification = 3.2 / _scale;
        _zoomLabel.Text = magnification < 1000 ? $"{magnification:0.#}×" : $"{magnification:0.##E+0}×";
    }

    private (double X, double Y) ToPlane(int px, int py)
    {
        var w = _canvas.BufferWidth;
        var h = _canvas.BufferHeight;
        return (_centreX + (px - w / 2.0) / w * _scale, _centreY + (py - h / 2.0) / w * _scale);
    }

    private void RenderBand(int rows)
    {
        var w = _canvas.BufferWidth;
        var h = _canvas.BufferHeight;
        if (_nextRow >= h) return;

        var pixels = _canvas.Pixels;
        var end = Math.Min(h, _nextRow + rows);

        for (var py = _nextRow; py < end; py++)
        {
            for (var px = 0; px < w; px++)
            {
                var (cx, cy) = ToPlane(px, py);
                pixels[py * w + px] = Colour(cx, cy);
            }
        }

        _nextRow = end;
        _canvas.Invalidate();

        _status.Text = _nextRow < h
            ? $"Rendering  {_nextRow * 100 / h}%"
            : $"{_maxIterations} iterations\nDrawn in {Environment.TickCount64 - _renderStarted} ms";
    }

    private uint Colour(double cx, double cy)
    {
        double x = 0, y = 0, x2 = 0, y2 = 0;
        var i = 0;
        while (x2 + y2 <= 256 && i < _maxIterations)
        {
            y = 2 * x * y + cy;
            x = x2 - y2 + cx;
            x2 = x * x;
            y2 = y * y;
            i++;
        }

        if (i >= _maxIterations) return 0xFF0B1026u;

        // Smooth (continuous) escape count, mapped onto the gradient.
        var smooth = i + 1 - Math.Log2(Math.Log2(x2 + y2) / 2);
        // The square root spreads the low counts and compresses the high ones, so the bands near
        // the boundary do not turn to noise.
        var index = (int)(Math.Sqrt(Math.Max(smooth, 0)) * 22) % _gradient.Length;
        return _gradient[index < 0 ? 0 : index];
    }

    private void UsePalette(int index)
    {
        var stops = Palettes[index].Stops;
        const int steps = 64;
        _gradient = new uint[(stops.Length - 1) * steps];

        for (var s = 0; s < stops.Length - 1; s++)
        {
            for (var t = 0; t < steps; t++)
            {
                var f = t / (double)steps;
                uint Lerp(int shift) => (uint)(((stops[s] >> shift) & 0xFF) * (1 - f) + ((stops[s + 1] >> shift) & 0xFF) * f);
                _gradient[s * steps + t] = 0xFF000000u | (Lerp(16) << 16) | (Lerp(8) << 8) | Lerp(0);
            }
        }

        for (var i = 0; i < _paletteButtons.Count; i++)
        {
            _paletteButtons[i].SetBackgroundColor(i == index ? Accent : Palette.Card);
            _paletteButtons[i].SetTextColor(i == index ? Palette.White : Palette.Ink);
            _paletteButtons[i].SetBorderWidth(i == index ? 0 : 1);
        }
    }
}
