using Lvgl.Drawing;
using Lvgl.Gallery.Shell;
using Lvgl.Widgets;

namespace Lvgl.Gallery.Multimedia;

/// <summary>
/// Photo filters on a generated landscape: pick a look, tune brightness, contrast and saturation,
/// and drag the divider to compare with the original. The histogram underneath updates with every
/// change. All of it is plain loops over a pixel span.
/// </summary>
internal sealed class PhotoLab : Demo
{
    // Red, green and blue at 70% opacity, so overlapping channels stay readable.
    private static readonly uint[] Channels = [0xB0E0464Eu, 0xB014A06Bu, 0xB02F62E8u];
    private static readonly string[] Looks = ["Original", "Mono", "Sepia", "Warm", "Cool", "Poster", "Invert", "Edges"];

    private uint[] _original = [];
    private readonly int[] _histogram = new int[3 * 64];
    private LvCanvas _photo = null!, _histogramCanvas = null!;
    private LvSlider _brightness = null!, _contrast = null!, _saturation = null!, _split = null!;
    private readonly List<LvButton> _lookButtons = [];
    private int _look;

    public override Category Category => Category.Multimedia;
    public override string Title => "Photo lab";
    public override string Summary => "Eight looks and three adjustments over a generated photo, with a before/after divider and a live RGB histogram.";
    public override string BuiltWith => "LvCanvas  LvSlider  Span<uint>";

    protected override void Build(LvObject stage, int width, int height)
    {
        var photoWidth = width - 250;
        var photoHeight = height - 40;

        _photo = new LvCanvas(stage, photoWidth, photoHeight);
        _original = Landscape(photoWidth, photoHeight);

        // The before/after divider runs under the photo, like a scrubber.
        _split = new LvSlider(stage);
        _split.SetSize(photoWidth - 16, 6);
        _split.SetPosition(8, photoHeight + 18);
        _split.SetRange(0, photoWidth);
        _split.SetValue(photoWidth / 2, animate: false);
        _split.SetBackgroundColor(Palette.Sunken);
        _split.SetBackgroundColor(Palette.InkFaint, LvPart.Indicator);
        _split.SetBackgroundColor(Palette.Ink, LvPart.Knob);
        _split.ValueChanged += (_, _) => Render();

        var panel = Bare(new LvPanel(stage, 230, height));
        panel.SetPosition(width - 230, 0);

        Caption(panel, "Look").SetPosition(0, 0);
        var looks = Bare(new LvPanel(panel, 230, 92));
        looks.SetPosition(0, 20);
        looks.SetFlexFlow(LvFlexFlow.RowWrap);
        looks.SetGap(6, 6);
        for (var i = 0; i < Looks.Length; i++)
        {
            var button = Button(looks, Looks[i], 53, 40, Palette.Card, Palette.Ink, 12);
            button.SetBorderColor(Palette.Rule);
            var index = i;
            button.Clicked += (_, _) => { _look = index; Paint(); Render(); };
            _lookButtons.Add(button);
        }

        _brightness = Adjustment(panel, "Brightness", 124);
        _contrast = Adjustment(panel, "Contrast", 176);
        _saturation = Adjustment(panel, "Saturation", 228);

        Caption(panel, "Histogram").SetPosition(0, 286);
        _histogramCanvas = new LvCanvas(panel, 230, height - 310);
        _histogramCanvas.SetPosition(0, 308);

        _look = Context.Screenshot ? 3 : 0;
        if (Context.Screenshot) _contrast.SetValue(18, animate: false);
        Paint();
        Render();
    }

    private LvSlider Adjustment(LvObject parent, string name, int y)
    {
        Caption(parent, name).SetPosition(0, y);
        var slider = new LvSlider(parent);
        slider.SetSize(214, 6);
        slider.SetPosition(8, y + 28);
        slider.SetRange(-50, 50);
        slider.SetValue(0, animate: false);
        slider.SetBackgroundColor(Palette.Sunken);
        slider.SetBackgroundColor(Accent, LvPart.Indicator);
        slider.SetBackgroundColor(Accent, LvPart.Knob);
        slider.ValueChanged += (_, _) => Render();
        return slider;
    }

    private void Paint()
    {
        for (var i = 0; i < _lookButtons.Count; i++)
        {
            _lookButtons[i].SetBackgroundColor(i == _look ? Accent : Palette.Card);
            _lookButtons[i].SetTextColor(i == _look ? Palette.White : Palette.Ink);
            _lookButtons[i].SetBorderWidth(i == _look ? 0 : 1);
        }
    }

    private void Render()
    {
        var w = _photo.BufferWidth;
        var h = _photo.BufferHeight;
        var output = _photo.Pixels;
        var split = _split.Value;

        var brightness = _brightness.Value * 2.0;
        var contrast = 1 + _contrast.Value / 50.0;
        var saturation = 1 + _saturation.Value / 50.0;
        Array.Clear(_histogram);

        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var i = y * w + x;
                var pixel = x < split ? Filter(x, y, w, h, brightness, contrast, saturation) : _original[i];
                output[i] = pixel;

                _histogram[(int)((pixel >> 16) & 0xFF) >> 2]++;
                _histogram[64 + ((int)((pixel >> 8) & 0xFF) >> 2)]++;
                _histogram[128 + ((int)(pixel & 0xFF) >> 2)]++;
            }
        }

        // The divider itself.
        if (split > 0 && split < w) _photo.FillRect(split - 1, 0, 2, h, 0xFFFFFFFFu);

        _photo.Invalidate();
        DrawHistogram();
    }

    private uint Filter(int x, int y, int w, int h, double brightness, double contrast, double saturation)
    {
        var source = _original[y * w + x];
        double r = (source >> 16) & 0xFF, g = (source >> 8) & 0xFF, b = source & 0xFF;

        switch (_look)
        {
            case 1: r = g = b = 0.299 * r + 0.587 * g + 0.114 * b; break;
            case 2:
            {
                var (sr, sg, sb) = (0.393 * r + 0.769 * g + 0.189 * b, 0.349 * r + 0.686 * g + 0.168 * b, 0.272 * r + 0.534 * g + 0.131 * b);
                (r, g, b) = (sr, sg, sb);
                break;
            }
            case 3: r *= 1.12; b *= 0.85; break;
            case 4: r *= 0.86; b *= 1.14; break;
            case 5: r = Math.Round(r / 64) * 64; g = Math.Round(g / 64) * 64; b = Math.Round(b / 64) * 64; break;
            case 6: r = 255 - r; g = 255 - g; b = 255 - b; break;
            case 7:
            {
                // Sobel on luminance.
                double Lum(int px, int py)
                {
                    var p = _original[Math.Clamp(py, 0, h - 1) * w + Math.Clamp(px, 0, w - 1)];
                    return 0.299 * ((p >> 16) & 0xFF) + 0.587 * ((p >> 8) & 0xFF) + 0.114 * (p & 0xFF);
                }

                var gx = -Lum(x - 1, y - 1) - 2 * Lum(x - 1, y) - Lum(x - 1, y + 1) + Lum(x + 1, y - 1) + 2 * Lum(x + 1, y) + Lum(x + 1, y + 1);
                var gy = -Lum(x - 1, y - 1) - 2 * Lum(x, y - 1) - Lum(x + 1, y - 1) + Lum(x - 1, y + 1) + 2 * Lum(x, y + 1) + Lum(x + 1, y + 1);
                r = g = b = 255 - Math.Min(255, Math.Sqrt(gx * gx + gy * gy));
                break;
            }
        }

        var grey = 0.299 * r + 0.587 * g + 0.114 * b;
        r = (grey + (r - grey) * saturation - 128) * contrast + 128 + brightness;
        g = (grey + (g - grey) * saturation - 128) * contrast + 128 + brightness;
        b = (grey + (b - grey) * saturation - 128) * contrast + 128 + brightness;

        return 0xFF000000u | ((uint)Math.Clamp(r, 0, 255) << 16) | ((uint)Math.Clamp(g, 0, 255) << 8) | (uint)Math.Clamp(b, 0, 255);
    }

    private void DrawHistogram()
    {
        var w = _histogramCanvas.BufferWidth;
        var h = _histogramCanvas.BufferHeight;
        _histogramCanvas.Fill(LvCanvas.Argb(Palette.Card));

        var peak = 1;
        foreach (var count in _histogram) peak = Math.Max(peak, count);

        var slot = w / 64.0;
        for (var c = 0; c < 3; c++)
        {
            for (var i = 0; i < 64; i++)
            {
                var bar = (int)(Math.Sqrt(_histogram[c * 64 + i] / (double)peak) * (h - 4));
                _histogramCanvas.FillRect((int)(i * slot), h - bar, Math.Max(1, (int)slot), bar, Channels[c]);
            }
        }

        _histogramCanvas.Invalidate();
    }

    // A generated photograph: dusk sky, a low sun, three ridges of hills and a lake reflecting them.
    private static uint[] Landscape(int w, int h)
    {
        var pixels = new uint[w * h];
        var horizon = (int)(h * 0.62);
        var random = new Random(11);

        static uint Rgb(double r, double g, double b) =>
            0xFF000000u | ((uint)Math.Clamp(r, 0, 255) << 16) | ((uint)Math.Clamp(g, 0, 255) << 8) | (uint)Math.Clamp(b, 0, 255);

        double Ridge(int x, double baseLine, double amplitude, double frequency, double seed) =>
            baseLine - amplitude * (0.6 * Math.Sin(x * frequency + seed) + 0.3 * Math.Sin(x * frequency * 2.3 + seed * 2) + 0.1 * Math.Sin(x * frequency * 5.1));

        for (var y = 0; y < horizon; y++)
        {
            var t = y / (double)horizon;
            for (var x = 0; x < w; x++)
            {
                // Sky: deep blue to rose to amber at the horizon, plus the sun's glow.
                double r = 40 + 200 * t * t, g = 50 + 110 * t * t, b = 120 + 40 * t - 60 * t * t;
                var dx = x - w * 0.68;
                var dy = y - horizon * 0.78;
                var d = Math.Sqrt(dx * dx + dy * dy);
                var glow = Math.Max(0, 1 - d / (w * 0.45));
                r += 120 * glow * glow; g += 80 * glow * glow; b += 20 * glow * glow;
                if (d < h * 0.08) (r, g, b) = (255, 236, 196);

                var hill = 0;
                if (y > Ridge(x, horizon * 0.80, h * 0.10, 0.011, 1.0)) hill = 1;
                if (y > Ridge(x, horizon * 0.90, h * 0.07, 0.017, 4.0)) hill = 2;
                if (y > Ridge(x, horizon * 0.98, h * 0.05, 0.027, 7.0)) hill = 3;
                if (hill > 0)
                {
                    var shade = 1.0 - hill * 0.22;
                    (r, g, b) = (110 * shade, 70 * shade, 110 * shade + 10);
                }

                pixels[y * w + x] = Rgb(r, g, b);
            }
        }

        // The lake: the scene above, mirrored, darkened, rippled.
        for (var y = horizon; y < h; y++)
        {
            var depth = (y - horizon) / (double)(h - horizon);
            var mirror = Math.Clamp(2 * horizon - y - 1, 0, horizon - 1);
            for (var x = 0; x < w; x++)
            {
                var ripple = (int)(Math.Sin(y * 0.9 + x * 0.05) * 2 * (0.4 + depth));
                var source = pixels[mirror * w + Math.Clamp(x + ripple, 0, w - 1)];
                var dim = 0.72 - 0.25 * depth;
                pixels[y * w + x] = Rgb(((source >> 16) & 0xFF) * dim, ((source >> 8) & 0xFF) * dim, (source & 0xFF) * dim + 12 + random.Next(4));
            }
        }

        return pixels;
    }
}
