using Lvgl.Drawing;
using Lvgl.Gallery.Shell;
using Lvgl.Widgets;

namespace Lvgl.Gallery.Scientific;

/// <summary>
/// A two-channel oscilloscope on a synthesised signal: pick the waveform, the frequency and the
/// noise, and the instrument measures frequency, peak-to-peak and RMS from the samples it draws.
/// </summary>
internal sealed class Oscilloscope : Demo
{
    private const int Points = 240;
    private const int SampleRate = 2000;   // samples per second, one per chart point

    private static readonly LvColor Screen = LvColor.FromRgb(0x0E1A2Bu);
    private static readonly LvColor Graticule = LvColor.FromRgb(0x23344Du);
    private static readonly LvColor Channel1 = LvColor.FromRgb(0x5CE1E6u);
    private static readonly LvColor Channel2 = LvColor.FromRgb(0xFFC857u);

    private static readonly string[] Waveforms = ["Sine", "Square", "Triangle", "Sawtooth"];

    private readonly Random _noiseSource = new(7);
    private readonly double[] _window = new double[Points];
    private LvChartSeries _ch1 = null!, _ch2 = null!;
    private LvLabel _frequency = null!, _peakToPeak = null!, _rms = null!, _freqValue = null!;
    private LvSlider _frequencySlider = null!, _noiseSlider = null!;
    private LvSwitch _ch2Switch = null!;
    private readonly List<LvButton> _waveButtons = [];
    private int _waveform;
    private double _phase, _phase2;
    private int _filled;

    public override Category Category => Category.Scientific;
    public override string Title => "Oscilloscope";
    public override string Summary => "A live two-channel trace from a signal generator, with frequency, peak-to-peak and RMS measured from the samples.";
    public override string BuiltWith => "LvChart  LvTimer  LvSlider  LvSwitch";

    protected override void Build(LvObject stage, int width, int height)
    {
        var screenWidth = width - 250;

        // The instrument screen: dark glass, a 10 x 8 graticule.
        var glass = Box(stage, 0, 0, screenWidth, height, Screen, 14);

        var chart = new LvChart(glass);
        chart.SetSize(screenWidth - 24, height - 70);
        chart.SetPosition(12, 12);
        chart.Type = LvChartType.Line;
        chart.PointCount = Points;
        chart.UpdateMode = LvChartUpdateMode.Circular;
        chart.SetRange(LvChartAxis.PrimaryY, -1200, 1200);
        chart.SetDivisionLines(7, 9);
        chart.SetBackgroundOpacity(0);
        chart.SetBorderWidth(0);
        chart.SetPadding(0);
        chart.SetLineColor(Graticule);
        chart.SetLineWidth(2, LvPart.Items);
        chart.SetPartSize(0, 0, LvPart.Indicator);

        _ch1 = chart.AddSeries(Channel1);
        _ch2 = chart.AddSeries(Channel2);
        _ch1.Fill(LvChartSeries.NoValue);
        _ch2.Fill(LvChartSeries.NoValue);

        // Measurements along the bottom of the glass, in the channel's colour.
        _frequency = Readout(glass, 16, "FREQ");
        _peakToPeak = Readout(glass, 16 + (screenWidth - 32) / 3, "VPP");
        _rms = Readout(glass, 16 + 2 * (screenWidth - 32) / 3, "RMS");

        // Generator controls.
        var controls = Bare(new LvPanel(stage, 230, height));
        controls.SetPosition(width - 230, 0);

        Caption(controls, "Waveform").SetPosition(0, 0);
        var waves = Bare(new LvPanel(controls, 230, 100));
        waves.SetPosition(0, 22);
        waves.SetFlexFlow(LvFlexFlow.RowWrap);
        waves.SetGap(8, 8);
        for (var i = 0; i < Waveforms.Length; i++)
        {
            var button = Button(waves, Waveforms[i], 111, 40, Palette.Card, Palette.Ink, 15);
            button.SetBorderWidth(1);
            button.SetBorderColor(Palette.Rule);
            var index = i;
            button.Clicked += (_, _) => SelectWaveform(index);
            _waveButtons.Add(button);
        }

        Caption(controls, "Frequency").SetPosition(0, 140);
        _freqValue = Text(controls, "", 20, Face.Display, Accent);
        _freqValue.Align(LvAlign.TopRight, 0, 134);
        _frequencySlider = Slider(controls, 172, 20, 200, 50);
        _frequencySlider.ValueChanged += (_, _) => _freqValue.Text = $"{_frequencySlider.Value} Hz";
        _freqValue.Text = $"{_frequencySlider.Value} Hz";

        Caption(controls, "Noise").SetPosition(0, 212);
        _noiseSlider = Slider(controls, 244, 0, 100, 8);

        Caption(controls, "Channel 2  ·  90°").SetPosition(0, 286);
        _ch2Switch = new LvSwitch(controls) { IsOn = true };
        _ch2Switch.Align(LvAlign.TopRight, 0, 280);
        _ch2Switch.SetBackgroundColor(Accent, LvPart.Indicator, LvState.Checked);
        _ch2Switch.ValueChanged += (_, _) => { if (!_ch2Switch.IsOn) _ch2.Fill(LvChartSeries.NoValue); };

        var legend = Text(controls, "Channel 2 shows the generator\na quarter period behind.", 13, Face.Body, Palette.InkFaint);
        legend.SetPosition(0, 326);

        SelectWaveform(0);

        // Twelve samples per tick at 60 ticks a second keeps the trace moving at a steady rate
        // without redrawing the chart for every sample.
        Every(16, () => Sample(12));
        if (Context.Screenshot) Sample(Points);
    }

    private LvLabel Readout(LvObject glass, int x, string name)
    {
        var caption = Text(glass, name, 11, Face.Mono, LvColor.FromRgb(0x7F93AEu));
        caption.Align(LvAlign.BottomLeft, x, -32);
        var value = Text(glass, "--", 20, Face.Display, Channel1);
        value.Align(LvAlign.BottomLeft, x, -8);
        return value;
    }

    private LvSlider Slider(LvObject parent, int y, int min, int max, int value)
    {
        var slider = new LvSlider(parent);
        slider.SetSize(210, 8);
        slider.SetPosition(8, y);
        slider.SetRange(min, max);
        slider.SetValue(value, animate: false);
        slider.SetBackgroundColor(Palette.Sunken);
        slider.SetBackgroundColor(Accent, LvPart.Indicator);
        slider.SetBackgroundColor(Accent, LvPart.Knob);
        return slider;
    }

    private void SelectWaveform(int index)
    {
        _waveform = index;
        for (var i = 0; i < _waveButtons.Count; i++)
        {
            _waveButtons[i].SetBackgroundColor(i == index ? Accent : Palette.Card);
            _waveButtons[i].SetTextColor(i == index ? Palette.White : Palette.Ink);
            _waveButtons[i].SetBorderWidth(i == index ? 0 : 1);
        }
    }

    private void Sample(int count)
    {
        var step = _frequencySlider.Value / (double)SampleRate;
        var noise = _noiseSlider.Value / 100.0 * 0.35;

        for (var i = 0; i < count; i++)
        {
            _phase = (_phase + step) % 1;
            _phase2 = (_phase + 0.25) % 1;

            var value = Wave(_phase) + (_noiseSource.NextDouble() * 2 - 1) * noise;
            _ch1.AddPoint((int)(value * 1000));
            _ch2.AddPoint(_ch2Switch.IsOn ? (int)(Wave(_phase2) * 600) : LvChartSeries.NoValue);

            Array.Copy(_window, 1, _window, 0, Points - 1);
            _window[^1] = value;
            _filled = Math.Min(_filled + 1, Points);
        }

        Measure();
    }

    private double Wave(double phase) => _waveform switch
    {
        0 => Math.Sin(phase * 2 * Math.PI),
        1 => phase < 0.5 ? 1 : -1,
        2 => phase < 0.5 ? 4 * phase - 1 : 3 - 4 * phase,
        _ => 2 * phase - 1,
    };

    // What the instrument would compute from its own window of samples.
    private void Measure()
    {
        if (_filled < Points) return;

        double min = double.MaxValue, max = double.MinValue, squares = 0;
        int crossings = 0, first = -1, last = -1;
        var armed = false;

        for (var i = 0; i < Points; i++)
        {
            var v = _window[i];
            min = Math.Min(min, v);
            max = Math.Max(max, v);
            squares += v * v;

            // A rising crossing with hysteresis, so noise around zero is not counted as edges -
            // the same trick as a scope's trigger.
            if (v < -0.2) armed = true;
            else if (armed && v > 0.2)
            {
                armed = false;
                crossings++;
                if (first < 0) first = i;
                last = i;
            }
        }

        // Period from the first to the last edge in the window: finer than counting edges.
        _frequency.Text = crossings >= 2 ? $"{(crossings - 1) * SampleRate / (double)(last - first):0.0} Hz" : "-- Hz";
        _peakToPeak.Text = $"{max - min:0.00} V";
        _rms.Text = $"{Math.Sqrt(squares / Points):0.000} V";
    }
}
