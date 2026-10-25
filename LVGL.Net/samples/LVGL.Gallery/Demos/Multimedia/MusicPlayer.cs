using Lvgl.Drawing;
using Lvgl.Gallery.Shell;
using Lvgl.Widgets;

namespace Lvgl.Gallery.Multimedia;

/// <summary>
/// The controls of a music player: cover art, transport, a seek bar that can be dragged, volume and
/// a playlist. There is no audio - a timer stands in for the decoder - but every control behaves.
/// Each cover is generated from the track's own colours on a canvas.
/// </summary>
internal sealed class MusicPlayer : Demo
{
    private sealed record Track(string Title, string Artist, int Seconds, uint From, uint To);

    private static readonly Track[] Playlist =
    [
        new("Copper Traces", "The Bus Pirates", 214, 0xD8336B, 0x2A0D45),
        new("Partial Refresh", "Vsync & the Tears", 187, 0x2F62E8, 0x0B1026),
        new("Thirty-Two Bits", "Framebuffer Ghosts", 241, 0xC9690E, 0x3B1205),
        new("Ninety Hertz Rain", "Lumen Atlas", 203, 0x0A8C98, 0x05202A),
        new("Single Thread", "Mutex Garden", 176, 0x6646D6, 0x150A3A),
        new("Idle Task", "Low Power Mode", 229, 0x0B8F63, 0x041F16),
    ];

    private LvCanvas _cover = null!;
    private LvLabel _title = null!, _artist = null!, _elapsed = null!, _remaining = null!;
    private LvSlider _seek = null!;
    private LvButton _play = null!;
    private readonly List<(LvButton Row, LvLabel Title, LvLabel Marker)> _rows = [];
    private int _track;
    private double _position;
    private bool _playing;
    private bool _seeking;

    public override Category Category => Category.Multimedia;
    public override string Title => "Music player";
    public override string Summary => "Transport, a draggable seek bar, volume and a playlist. Covers are generated on a canvas from each track's colours.";
    public override string BuiltWith => "LvCanvas  LvSlider  LvButton  LvTimer";

    protected override void Build(LvObject stage, int width, int height)
    {
        var coverSize = height - 150;
        var playerWidth = coverSize + 20 + 210;

        _cover = new LvCanvas(stage, coverSize, coverSize);
        _cover.SetPosition(0, 0);

        // Title, artist and transport to the right of the cover.
        var info = Bare(new LvPanel(stage, 210, coverSize));
        info.SetPosition(coverSize + 22, 0);

        Caption(info, "Now playing").SetPosition(0, 4);
        _title = Text(info, "", 34, Face.Display, Palette.Ink);
        _title.SetPosition(0, 26);
        _title.Width = 210;
        _title.LongMode = LvLabelLongMode.Wrap;
        _artist = Text(info, "", 17, Face.Body, Accent);
        _artist.SetPosition(0, 104);

        var previous = IconButton(info, LvSymbols.Previous, 56, 56, Palette.Card, Palette.Ink, 18);
        previous.SetRadius(28);
        previous.SetBorderWidth(1);
        previous.SetBorderColor(Palette.Rule);
        previous.SetPosition(0, coverSize - 76);
        previous.Clicked += (_, _) => Skip(-1);

        _play = IconButton(info, LvSymbols.Play, 76, 76, size: 24);
        _play.SetRadius(38);
        _play.SetPosition(66, coverSize - 86);
        _play.Clicked += (_, _) => SetPlaying(!_playing);

        var next = IconButton(info, LvSymbols.Next, 56, 56, Palette.Card, Palette.Ink, 18);
        next.SetRadius(28);
        next.SetBorderWidth(1);
        next.SetBorderColor(Palette.Rule);
        next.SetPosition(152, coverSize - 76);
        next.Clicked += (_, _) => Skip(+1);

        // Seek bar under everything: drag to scrub, release to jump.
        _seek = new LvSlider(stage);
        _seek.SetSize(playerWidth - 30, 8);
        _seek.SetPosition(8, coverSize + 36);
        _seek.SetBackgroundColor(Palette.Sunken);
        _seek.SetBackgroundColor(Accent, LvPart.Indicator);
        _seek.SetBackgroundColor(Accent, LvPart.Knob);
        _seek.AddHandler(LvEventCode.Pressed, (_, _) => _seeking = true);
        _seek.AddHandler(LvEventCode.Released, (_, _) => { _seeking = false; _position = _seek.Value; });

        _elapsed = Text(stage, "", 12, Face.Mono, Palette.InkMuted);
        _elapsed.SetPosition(0, coverSize + 60);
        _remaining = Text(stage, "", 12, Face.Mono, Palette.InkMuted);
        _remaining.SetPosition(playerWidth - 70, coverSize + 60);

        Icon(stage, LvSymbols.VolumeMid, 16, Palette.InkMuted).SetPosition(0, coverSize + 100);
        var volume = new LvSlider(stage);
        volume.SetSize(160, 6);
        volume.SetPosition(32, coverSize + 106);
        volume.SetValue(70, animate: false);
        volume.SetBackgroundColor(Palette.Sunken);
        volume.SetBackgroundColor(Palette.InkMuted, LvPart.Indicator);
        volume.SetBackgroundColor(Palette.InkMuted, LvPart.Knob);

        // The playlist.
        var listX = playerWidth + 14;
        var rowWidth = width - listX - 20;
        var list = Card(stage, listX, 0, width - listX, height);
        Caption(list, "Up next").SetPosition(18, 16);

        for (var i = 0; i < Playlist.Length; i++)
        {
            var track = Playlist[i];
            var row = new LvButton(list);
            row.SetSize(rowWidth, 54);
            row.SetPadding(0);
            row.SetPosition(10, 42 + i * 58);
            row.SetRadius(10);
            row.SetShadowColor(Palette.Card);
            row.SetBackgroundColor(Palette.Card);
            row.SetBackgroundColor(Tint, LvPart.Main, LvState.Pressed);

            Box(row, 10, 10, 34, 34, LvColor.FromRgb(track.From), 8);
            var title = Text(row, track.Title, 15, Face.Body, Palette.Ink);
            title.SetPosition(54, 8);
            // Dot needs a fixed height as well as a width; otherwise the label just wraps.
            title.SetSize(rowWidth - 110, 20);
            title.LongMode = LvLabelLongMode.Dot;
            var artist = Text(row, track.Artist, 12, Face.Body, Palette.InkMuted);
            artist.SetPosition(54, 29);
            artist.SetSize(rowWidth - 110, 16);
            artist.LongMode = LvLabelLongMode.Dot;
            var marker = Text(row, Time(track.Seconds), 12, Face.Mono, Palette.InkMuted);
            marker.Align(LvAlign.RightMid, -10, 0);

            var index = i;
            row.Clicked += (_, _) => { Load(index); SetPlaying(true); };
            _rows.Add((row, title, marker));
        }

        Load(Context.Screenshot ? 1 : 0);
        if (Context.Screenshot) { _position = 71; SetPlaying(true); }

        Every(250, Tick);
    }

    private void Load(int index)
    {
        _track = (index + Playlist.Length) % Playlist.Length;
        _position = 0;
        var track = Playlist[_track];

        _title.Text = track.Title;
        _artist.Text = track.Artist;
        _seek.SetRange(0, track.Seconds);
        PaintCover(track);

        for (var i = 0; i < _rows.Count; i++)
        {
            var current = i == _track;
            _rows[i].Row.SetBackgroundColor(current ? Tint : Palette.Card);
            _rows[i].Title.SetTextColor(current ? Accent : Palette.Ink);
            _rows[i].Title.SetFont(Fonts[current ? Face.Strong : Face.Body, 15]);
        }

        Tick();
    }

    private void Skip(int delta) => Load(_track + delta);

    private void SetPlaying(bool playing)
    {
        _playing = playing;
        _play.Text = playing ? LvSymbols.Pause : LvSymbols.Play;
    }

    private void Tick()
    {
        var track = Playlist[_track];
        if (_playing && !_seeking)
        {
            _position += 0.25;
            if (_position >= track.Seconds) { Skip(+1); return; }
        }

        var shown = _seeking ? _seek.Value : _position;
        if (!_seeking) _seek.SetValue((int)_position, animate: false);
        _elapsed.Text = Time((int)shown);
        _remaining.Text = "-" + Time(track.Seconds - (int)shown);
    }

    // Generative cover: a diagonal gradient, concentric rings and a band of stripes, all derived
    // from the track's two colours, so every cover differs and none needs an image file.
    private void PaintCover(Track track)
    {
        var size = _cover.BufferWidth;
        var pixels = _cover.Pixels;
        var a = LvColor.FromRgb(track.From);
        var b = LvColor.FromRgb(track.To);

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var t = (x + y) / (2.0 * size);
                var colour = Categories.Mix(b, a, 1 - t);

                // Rings centred lower-left, alternating light and dark.
                var dx = x - size * 0.3;
                var dy = y - size * 0.72;
                var ring = (int)(Math.Sqrt(dx * dx + dy * dy) / (size / 14.0));
                if (ring < 9 && ring % 2 == 0) colour = Categories.Mix(colour, Palette.White, 0.12);

                // A band of stripes across the upper right.
                if (y < size * 0.38 && x > size * 0.45 && (x + y) / 9 % 2 == 0) colour = Categories.Mix(colour, b, 0.35);

                pixels[y * size + x] = LvCanvas.Argb(colour);
            }
        }

        _cover.Invalidate();
    }

    private static string Time(int seconds) => $"{seconds / 60}:{seconds % 60:00}";
}
