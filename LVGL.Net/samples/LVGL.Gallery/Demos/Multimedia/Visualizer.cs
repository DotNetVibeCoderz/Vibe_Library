using Lvgl.Drawing;
using Lvgl.Gallery.Shell;
using Lvgl.Widgets;

namespace Lvgl.Gallery.Multimedia;

/// <summary>
/// An audio spectrum analyser driven by a synthesised track: kick, bass line, chords and hats each
/// excite their own frequency bands. Three views - bars with falling peak caps, a mirrored wave and
/// a radial burst - all drawn into one canvas at 60 frames a second without allocating.
/// </summary>
internal sealed class Visualizer : Demo
{
    private const int Bands = 48;

    private static readonly string[] Modes = ["Bars", "Mirror", "Radial"];
    private static readonly int[] BassLine = [3, 3, 5, 2];

    private readonly double[] _level = new double[Bands];
    private readonly double[] _peak = new double[Bands];
    private readonly uint[] _ramp = new uint[256];
    private readonly Random _random = new(3);
    private readonly List<LvButton> _modeButtons = [];
    private LvCanvas _canvas = null!;
    private LvSlider _gain = null!, _decay = null!;
    private LvLabel _beat = null!;
    private int _mode;
    private double _time;
    private uint _background;

    public override Category Category => Category.Multimedia;
    public override string Title => "Visualizer";
    public override string Summary => "A 48-band spectrum analyser on a synthesised beat, in three views - drawn into one canvas each frame without allocating.";
    public override string BuiltWith => "LvCanvas  LvTimer  LvSlider";

    protected override void Build(LvObject stage, int width, int height)
    {
        _background = LvCanvas.Argb(LvColor.FromRgb(0x14081Cu));

        // Magenta through amber to near-white: the ramp the bars are coloured with by height.
        var low = Accent;
        var mid = LvColor.FromRgb(0xF2A516u);
        for (var i = 0; i < 256; i++)
        {
            var t = i / 255.0;
            var colour = t < 0.6 ? Categories.Mix(low, mid, t / 0.6) : Categories.Mix(mid, LvColor.FromRgb(0xFFF4D6u), (t - 0.6) / 0.4);
            _ramp[i] = LvCanvas.Argb(colour);
        }

        _canvas = new LvCanvas(stage, width - 230, height);

        var panel = Bare(new LvPanel(stage, 210, height));
        panel.SetPosition(width - 210, 0);

        Caption(panel, "View").SetPosition(0, 0);
        for (var i = 0; i < Modes.Length; i++)
        {
            var button = Button(panel, Modes[i], 64, 40, Palette.Card, Palette.Ink, 14);
            button.SetPosition(i * 73, 22);
            button.SetBorderColor(Palette.Rule);
            var index = i;
            button.Clicked += (_, _) => SelectMode(index);
            _modeButtons.Add(button);
        }

        Caption(panel, "Gain").SetPosition(0, 92);
        _gain = Slider(panel, 120, 40, 200, 150);
        Caption(panel, "Fall-off").SetPosition(0, 156);
        _decay = Slider(panel, 184, 1, 30, 9);

        Caption(panel, "Tempo").SetPosition(0, 232);
        _beat = Text(panel, "124 BPM", 40, Face.Display, Accent);
        _beat.SetPosition(0, 252);

        var note = Text(panel, "The track is synthesised, so\nno audio device is needed.", 13, Face.Body, Palette.InkFaint);
        note.Align(LvAlign.BottomLeft, 0, 0);

        SelectMode(0);
        if (Context.Screenshot) for (var i = 0; i < 90; i++) Step();
        Every(16, Step);
    }

    private LvSlider Slider(LvObject parent, int y, int min, int max, int value)
    {
        var slider = new LvSlider(parent);
        slider.SetSize(194, 8);
        slider.SetPosition(8, y);
        slider.SetRange(min, max);
        slider.SetValue(value, animate: false);
        slider.SetBackgroundColor(Palette.Sunken);
        slider.SetBackgroundColor(Accent, LvPart.Indicator);
        slider.SetBackgroundColor(Accent, LvPart.Knob);
        return slider;
    }

    private void SelectMode(int index)
    {
        _mode = index;
        for (var i = 0; i < _modeButtons.Count; i++)
        {
            _modeButtons[i].SetBackgroundColor(i == index ? Accent : Palette.Card);
            _modeButtons[i].SetTextColor(i == index ? Palette.White : Palette.Ink);
            _modeButtons[i].SetBorderWidth(i == index ? 0 : 1);
        }
    }

    private void Step()
    {
        _time += 1 / 60.0;
        Synthesise();
        Draw();
    }

    // Energy per band from four instruments on a 124 BPM grid.
    private void Synthesise()
    {
        const double beat = 60.0 / 124;
        var phase = _time % beat / beat;
        var bar = (int)(_time / beat) % 4;

        var kick = Math.Exp(-phase * 9);
        var hat = Math.Exp(-((phase + 0.5) % 1) * 18);
        var chord = 0.55 + 0.25 * Math.Sin(_time * 0.9) + (bar == 0 ? 0.15 : 0);
        var bassNote = BassLine[bar];

        var gain = _gain.Value / 100.0;
        var fall = _decay.Value / 400.0;

        for (var b = 0; b < Bands; b++)
        {
            var f = b / (double)(Bands - 1);
            var energy =
                kick * Math.Exp(-Math.Pow((f - 0.04) / 0.06, 2)) +
                0.7 * Math.Exp(-Math.Pow((b - bassNote * 2) / 2.2, 2)) * (0.6 + 0.4 * kick) +
                chord * 0.5 * Math.Exp(-Math.Pow((f - 0.42) / 0.14, 2)) * (0.8 + 0.2 * Math.Sin(_time * 7 + b)) +
                hat * 0.55 * Math.Exp(-Math.Pow((f - 0.86) / 0.1, 2)) +
                0.04 * _random.NextDouble();

            var target = Math.Clamp(energy * gain, 0, 1);
            _level[b] = target > _level[b] ? target : Math.Max(target, _level[b] - fall);
            _peak[b] = Math.Max(_level[b], _peak[b] - 0.006);
        }
    }

    private void Draw()
    {
        var w = _canvas.BufferWidth;
        var h = _canvas.BufferHeight;
        _canvas.Fill(_background);

        switch (_mode)
        {
            case 0:
            {
                var slot = w / (double)Bands;
                for (var b = 0; b < Bands; b++)
                {
                    var x = (int)(b * slot) + 2;
                    var bw = Math.Max(2, (int)slot - 4);
                    var top = (int)(h - 24 - _level[b] * (h - 48));

                    // Segmented like a hardware analyser: 6-pixel cells with 2-pixel gaps.
                    for (var y = h - 24; y > top; y -= 8)
                    {
                        var colour = _ramp[Math.Clamp((h - 24 - y) * 255 / (h - 48), 0, 255)];
                        _canvas.FillRect(x, y - 6, bw, 6, colour);
                    }

                    var cap = (int)(h - 24 - _peak[b] * (h - 48));
                    _canvas.FillRect(x, cap - 10, bw, 3, 0xFFFFFFFFu);
                }

                break;
            }

            case 1:
            {
                var mid = h / 2;
                var slot = w / (double)Bands;
                for (var b = 0; b < Bands; b++)
                {
                    var x = (int)(b * slot) + 3;
                    var half = (int)(_level[b] * (h / 2 - 20));
                    var colour = _ramp[(int)(_level[b] * 255)];
                    _canvas.FillRect(x, mid - half, Math.Max(2, (int)slot - 6), half * 2 + 1, colour);
                }

                _canvas.FillRect(0, mid, w, 1, _ramp[40]);
                break;
            }

            default:
            {
                int cx = w / 2, cy = h / 2;
                var inner = Math.Min(w, h) / 6;
                var outer = Math.Min(w, h) / 2 - 16;
                for (var b = 0; b < Bands * 2; b++)
                {
                    var level = _level[b < Bands ? b : Bands * 2 - 1 - b];
                    var angle = b / (double)(Bands * 2) * 2 * Math.PI - Math.PI / 2;
                    var length = inner + level * (outer - inner);
                    var colour = _ramp[(int)(level * 255)];
                    for (var r = inner; r < length; r += 2)
                    {
                        _canvas.FillRect(cx + (int)(Math.Cos(angle) * r) - 1, cy + (int)(Math.Sin(angle) * r) - 1, 3, 3, colour);
                    }
                }

                _canvas.FillCircle(cx, cy, (int)(inner * (0.7 + 0.25 * _level[1])), _ramp[60]);
                break;
            }
        }

        _canvas.Invalidate();
    }
}
