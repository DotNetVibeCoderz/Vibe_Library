using Lvgl.Drawing;
using Lvgl.Events;
using Lvgl.Gallery.Shell;
using Lvgl.Input;
using Lvgl.Widgets;

namespace Lvgl.Gallery.Games;

/// <summary>
/// 2048: slide the tiles, equal tiles merge, reach 2048. Arrow keys or a swipe on the board. Tiles
/// are plain panels; a merge pops with a short size animation.
/// </summary>
internal sealed class Game2048 : Demo
{
    private const int Size = 4;
    private const int Gap = 10;

    private readonly Board2048 _game = new(new Random(2048));
    private readonly LvPanel[,] _tiles = new LvPanel[Size, Size];
    private readonly LvLabel[,] _labels = new LvLabel[Size, Size];
    private LvLabel _score = null!, _best = null!, _status = null!;
    private int _cell, _bestScore;

    public override Category Category => Category.Games;
    public override string Title => "2048";
    public override string Summary => "Slide with the arrow keys or swipe the board. Matching tiles merge; make a 2048 tile to win.";
    public override string BuiltWith => "LvPanel  LvAnimation  GestureDirection  LvKey";

    protected override void Build(LvObject stage, int width, int height)
    {
        var boardSize = height;
        _cell = (boardSize - Gap * (Size + 1)) / Size;

        var board = Box(stage, 0, 0, boardSize, boardSize, Categories.Mix(Accent, Palette.Ink, 0.25), 16);
        board.IsClickable = true;
        board.SetFlag(LvObjFlag.GestureBubble, false);   // receive swipes rather than pass them up
        board.AddHandler(LvEventCode.Gesture, (_, e) => Move(e.GestureDirection));

        for (var r = 0; r < Size; r++)
        {
            for (var c = 0; c < Size; c++)
            {
                var tile = Box(board, Gap + c * (_cell + Gap), Gap + r * (_cell + Gap), _cell, _cell, Palette.White, 10);
                tile.SetBackgroundOpacity(30);
                _tiles[r, c] = tile;
                _labels[r, c] = Text(tile, "", 40, Face.Display, Palette.Ink);
                _labels[r, c].Center();
            }
        }

        var side = Bare(new LvPanel(stage, width - boardSize - 30, height));
        side.SetPosition(boardSize + 30, 0);

        Caption(side, "Score").SetPosition(0, 0);
        _score = Text(side, "0", 54, Face.Display, Accent);
        _score.SetPosition(0, 16);
        Caption(side, "Best").SetPosition(150, 0);
        _best = Text(side, "0", 54, Face.Display, Palette.InkFaint);
        _best.SetPosition(150, 16);

        _status = Text(side, "", 16, Face.Body, Palette.InkMuted);
        _status.Width = width - boardSize - 30;
        _status.SetPosition(0, 100);

        var help = Text(side, "Arrow keys or swipe the board.\nTwo equal tiles that touch become one.", 14, Face.Body, Palette.InkFaint);
        help.SetPosition(0, 150);

        var restart = Button(side, "New game", 160, 46, size: 16);
        restart.Align(LvAlign.BottomLeft, 0, 0);
        restart.Clicked += (_, _) => { _game.Reset(); Paint(); };

        stage.AddHandler(LvEventCode.Key, OnKey);
        App.FocusGroup?.Add(stage);
        LvGroup.Focus(stage);

        if (Context.Screenshot)
        {
            _game.Load(new[,] { { 2, 8, 32, 128 }, { 0, 4, 16, 512 }, { 0, 2, 8, 64 }, { 0, 0, 4, 1024 } }, 9516);
            _bestScore = 20384;
        }

        Paint();
    }

    private void OnKey(object? sender, LvEventArgs e)
    {
        switch (e.Key)
        {
            case LvKey.Up: Move(LvDir.Top); break;
            case LvKey.Down: Move(LvDir.Bottom); break;
            case LvKey.Left: Move(LvDir.Left); break;
            case LvKey.Right: Move(LvDir.Right); break;
        }
    }

    private void Move(LvDir direction)
    {
        var merged = _game.Move(direction);
        if (merged is null) return;

        Paint();
        foreach (var (r, c) in merged)
        {
            // A pop on every merge: the tile swells slightly and settles.
            var tile = _tiles[r, c];
            new LvAnimation(tile, v => tile.SetSize(v, v), _cell + 10, _cell) { Duration = 160, Path = LvAnimPath.EaseOut }.Start();
        }
    }

    private void Paint()
    {
        for (var r = 0; r < Size; r++)
        {
            for (var c = 0; c < Size; c++)
            {
                var value = _game[r, c];
                var tile = _tiles[r, c];
                var label = _labels[r, c];

                tile.SetSize(_cell, _cell);
                if (value == 0)
                {
                    tile.SetBackgroundColor(Palette.White);
                    tile.SetBackgroundOpacity(30);
                    label.Text = "";
                    continue;
                }

                // Tiles warm from pale to the category colour, then deepen towards ink.
                var step = (int)Math.Log2(value);
                var colour = step <= 6
                    ? Categories.Mix(Palette.White, Accent, step / 6.0)
                    : Categories.Mix(Accent, LvColor.FromRgb(0x3B1A05u), (step - 6) / 6.0);
                tile.SetBackgroundColor(colour);
                tile.SetBackgroundOpacity(255);
                label.Text = value.ToString();
                label.SetTextColor(step <= 2 ? Palette.Ink : Palette.White);
                label.SetFont(Fonts[Face.Display, value < 128 ? 44 : value < 1024 ? 38 : 32]);
            }
        }

        _bestScore = Math.Max(_bestScore, _game.Score);
        _score.Text = _game.Score.ToString();
        _best.Text = _bestScore.ToString();
        _status.Text = _game.Won ? "2048! Keep going for a higher score." : _game.CanMove ? "" : "No moves left. Start a new game.";
        _status.SetTextColor(_game.Won ? Palette.Good : Palette.Bad);
    }
}

/// <summary>The rules of 2048, apart from any UI.</summary>
internal sealed class Board2048(Random random)
{
    private int[,] _cells = new int[4, 4];

    public int this[int row, int column] => _cells[row, column];
    public int Score { get; private set; }
    public bool Won { get; private set; }

    public bool CanMove
    {
        get
        {
            for (var r = 0; r < 4; r++)
                for (var c = 0; c < 4; c++)
                    if (_cells[r, c] == 0 || (c < 3 && _cells[r, c] == _cells[r, c + 1]) || (r < 3 && _cells[r, c] == _cells[r + 1, c])) return true;
            return false;
        }
    }

    public void Reset()
    {
        _cells = new int[4, 4];
        Score = 0;
        Won = false;
        Spawn();
        Spawn();
    }

    public void Load(int[,] cells, int score)
    {
        _cells = (int[,])cells.Clone();
        Score = score;
    }

    /// <summary>Slides every tile; returns the merged cells, or null when nothing moved.</summary>
    public List<(int Row, int Column)>? Move(LvDir direction)
    {
        var before = (int[,])_cells.Clone();
        var merged = new List<(int, int)>();

        for (var line = 0; line < 4; line++)
        {
            // Read each line in the direction of travel, so index 0 is the edge tiles slide to.
            (int R, int C) At(int i) => direction switch
            {
                LvDir.Left => (line, i),
                LvDir.Right => (line, 3 - i),
                LvDir.Top => (i, line),
                _ => (3 - i, line),
            };

            var values = new List<int>(4);
            for (var i = 0; i < 4; i++) { var (r, c) = At(i); if (_cells[r, c] != 0) values.Add(_cells[r, c]); }

            var result = new List<int>(4);
            for (var i = 0; i < values.Count; i++)
            {
                if (i + 1 < values.Count && values[i] == values[i + 1])
                {
                    var sum = values[i] * 2;
                    Score += sum;
                    Won |= sum == 2048;
                    merged.Add(At(result.Count));
                    result.Add(sum);
                    i++;
                }
                else
                {
                    result.Add(values[i]);
                }
            }

            for (var i = 0; i < 4; i++) { var (r, c) = At(i); _cells[r, c] = i < result.Count ? result[i] : 0; }
        }

        var moved = false;
        for (var r = 0; r < 4 && !moved; r++)
            for (var c = 0; c < 4 && !moved; c++)
                moved = before[r, c] != _cells[r, c];

        if (!moved) return null;
        Spawn();
        return merged;
    }

    private void Spawn()
    {
        var empty = new List<(int, int)>();
        for (var r = 0; r < 4; r++)
            for (var c = 0; c < 4; c++)
                if (_cells[r, c] == 0) empty.Add((r, c));

        if (empty.Count == 0) return;
        var (row, column) = empty[random.Next(empty.Count)];
        _cells[row, column] = random.Next(10) == 0 ? 4 : 2;
    }
}
