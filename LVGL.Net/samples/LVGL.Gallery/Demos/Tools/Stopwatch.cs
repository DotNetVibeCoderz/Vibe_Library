using Lvgl.Drawing;
using Lvgl.Gallery.Shell;
using Lvgl.Widgets;

namespace Lvgl.Gallery.Tools;

/// <summary>
/// A stopwatch with laps. The ring sweeps once a minute; the lap list marks the fastest lap green
/// and the slowest red, which is what anyone timing laps actually wants to know.
/// </summary>
internal sealed class Stopwatch : Demo
{
    private readonly System.Diagnostics.Stopwatch _clock = new();
    private readonly List<TimeSpan> _laps = [];
    private TimeSpan _offset;

    private LvArc _ring = null!;
    private LvLabel _time = null!;
    private LvLabel _lapTime = null!;
    private LvButton _startStop = null!;
    private LvButton _lapReset = null!;
    private LvObject _lapList = null!;
    private int _rowWidth;

    public override Category Category => Category.Tools;
    public override string Title => "Stopwatch";
    public override string Summary => "Hundredths of a second on a 60-second ring, with laps ranked fastest to slowest as you go.";
    public override string BuiltWith => "LvArc  LvTimer  LvButton  LvLabel";

    private TimeSpan Elapsed => _clock.Elapsed + _offset;

    protected override void Build(LvObject stage, int width, int height)
    {
        var dial = Math.Min(height - 80, 340);

        _ring = new LvArc(stage);
        _ring.SetSize(dial, dial);
        _ring.SetPosition(20, 0);
        _ring.Rotation = 270;
        _ring.SetBackgroundAngles(0, 360);
        _ring.SetRange(0, 6000);
        _ring.Value = 0;
        _ring.IsClickable = false;
        _ring.SetArcWidth(14);
        _ring.SetArcWidth(14, LvPart.Indicator);
        _ring.SetArcColor(Palette.Sunken);
        _ring.SetArcColor(Accent, LvPart.Indicator);
        _ring.SetBackgroundOpacity(0, LvPart.Knob);

        _time = Text(_ring, "00:00.00", 56, Face.Display, Palette.Ink);
        _time.Align(LvAlign.Center, 0, -6);

        _lapTime = Text(_ring, "LAP  00:00.00", 13, Face.Mono, Palette.InkMuted);
        _lapTime.Align(LvAlign.Center, 0, 38);

        var buttonWidth = (dial - 16) / 2;
        _lapReset = Button(stage, "Reset", buttonWidth, 52, Palette.Card, Palette.Ink, 18);
        _lapReset.SetBorderWidth(1);
        _lapReset.SetBorderColor(Palette.Rule);
        _lapReset.SetPosition(20, height - 56);
        _lapReset.Clicked += (_, _) => LapOrReset();

        _startStop = Button(stage, "Start", buttonWidth, 52, size: 18);
        _startStop.SetPosition(20 + buttonWidth + 16, height - 56);
        _startStop.Clicked += (_, _) => StartOrStop();

        // Laps on the right, newest first.
        var listX = dial + 60;
        var card = Card(stage, listX, 0, width - listX, height);
        Caption(card, "Laps").SetPosition(20, 16);
        _rowWidth = width - listX - 40;
        _lapList = Bare(new LvPanel(card, _rowWidth, height - 56));
        _lapList.SetPosition(20, 44);
        _lapList.SetFlexFlow(LvFlexFlow.Column);
        _lapList.SetGap(2, 0);
        _lapList.IsScrollable = true;

        if (Context.Screenshot)
        {
            // A plausible run: four laps done, the fifth under way.
            foreach (var seconds in new[] { 62.41, 59.87, 64.02, 60.55 }) _laps.Add(TimeSpan.FromSeconds(seconds));
            _offset = TimeSpan.FromSeconds(62.41 + 59.87 + 64.02 + 60.55 + 23.18);
            StartOrStop();
        }

        ShowLaps();
        Every(33, Refresh);
        Refresh();
    }

    private void StartOrStop()
    {
        if (_clock.IsRunning) _clock.Stop();
        else _clock.Start();

        _startStop.Text = _clock.IsRunning ? "Stop" : "Start";
        _startStop.SetBackgroundColor(_clock.IsRunning ? Palette.Bad : Accent);
        _lapReset.Text = _clock.IsRunning ? "Lap" : "Reset";
    }

    private void LapOrReset()
    {
        if (_clock.IsRunning)
        {
            _laps.Add(Elapsed - Total(_laps));
        }
        else
        {
            _clock.Reset();
            _offset = TimeSpan.Zero;
            _laps.Clear();
        }

        ShowLaps();
        Refresh();
    }

    private void Refresh()
    {
        var elapsed = Elapsed;
        _time.Text = Format(elapsed);
        _lapTime.Text = $"LAP {_laps.Count + 1}  {Format(elapsed - Total(_laps))}";
        _ring.Value = (int)(elapsed.TotalMilliseconds / 10 % 6000);
    }

    private void ShowLaps()
    {
        _lapList.Clear();
        if (_laps.Count == 0)
        {
            Text(_lapList, "Press Lap while running to split the time.", 15, Face.Body, Palette.InkFaint);
            return;
        }

        var fastest = _laps.Min();
        var slowest = _laps.Max();

        for (var i = _laps.Count - 1; i >= 0; i--)
        {
            var lap = _laps[i];
            var colour = _laps.Count > 1 && lap == fastest ? Palette.Good
                : _laps.Count > 1 && lap == slowest ? Palette.Bad
                : Palette.Ink;

            var row = Bare(new LvPanel(_lapList, _rowWidth, 40));
            Text(row, $"Lap {i + 1}", 16, Face.Body, Palette.InkMuted).Align(LvAlign.LeftMid);
            Text(row, Format(lap), 22, Face.Display, colour).Align(LvAlign.RightMid);
            Box(row, 0, 39, _rowWidth, 1, Palette.Sunken);
        }
    }

    private static TimeSpan Total(List<TimeSpan> laps) => laps.Aggregate(TimeSpan.Zero, (sum, lap) => sum + lap);

    private static string Format(TimeSpan time) => $"{(int)time.TotalMinutes:00}:{time.Seconds:00}.{time.Milliseconds / 10:00}";
}
