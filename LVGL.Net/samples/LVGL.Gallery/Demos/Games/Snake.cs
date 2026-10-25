using Lvgl.Drawing;
using Lvgl.Events;
using Lvgl.Gallery.Shell;
using Lvgl.Input;
using Lvgl.Widgets;

namespace Lvgl.Gallery.Games;

/// <summary>
/// Snake on a canvas. Steer with the arrow keys, by swiping on the board, or with the pad on the
/// right; each apple makes the snake longer and the game a little faster.
/// </summary>
internal sealed class Snake : Demo
{
    private const int Cell = 20;

    private readonly LinkedList<(int X, int Y)> _body = new();
    private readonly Random _random = new(5);
    private LvCanvas _board = null!;
    private LvLabel _score = null!, _best = null!, _message = null!;
    private LvTimer _clock = null!;
    private int _columns, _rows;
    private (int X, int Y) _direction = (1, 0), _queued = (1, 0), _apple;
    private int _points, _bestPoints;
    private bool _over, _paused;

    private uint _light, _dark, _snake, _head, _appleColour;

    public override Category Category => Category.Games;
    public override string Title => "Snake";
    public override string Summary => "Arrow keys, a swipe on the board or the on-screen pad. Each apple adds a segment and a little speed.";
    public override string BuiltWith => "LvCanvas  LvTimer  LvKey  GestureDirection";

    protected override void Build(LvObject stage, int width, int height)
    {
        _light = LvCanvas.Argb(Categories.Mix(Accent, Palette.White, 0.93));
        _dark = LvCanvas.Argb(Categories.Mix(Accent, Palette.White, 0.88));
        _snake = LvCanvas.Argb(Accent);
        _head = LvCanvas.Argb(Categories.Mix(Accent, Palette.Ink, 0.35));
        _appleColour = LvCanvas.Argb(Palette.Bad);

        _columns = (width - 230) / Cell;
        _rows = height / Cell;
        _board = new LvCanvas(stage, _columns * Cell, _rows * Cell);
        _board.IsClickable = true;
        _board.SetFlag(LvObjFlag.GestureBubble, false);   // receive swipes rather than pass them up
        _board.AddHandler(LvEventCode.Gesture, (_, e) => Steer(e.GestureDirection));

        _message = Text(stage, "", 30, Face.Display, Palette.Ink);
        _message.SetTextAlign(LvTextAlign.Center);
        _message.SetBackgroundColor(Palette.Card);
        _message.SetBackgroundOpacity(240);
        _message.SetRadius(14);
        _message.SetPadding(18);

        var side = Bare(new LvPanel(stage, 210, height));
        side.SetPosition(width - 210, 0);

        Caption(side, "Score").SetPosition(0, 0);
        _score = Text(side, "0", 54, Face.Display, Accent);
        _score.SetPosition(0, 16);
        Caption(side, "Best").SetPosition(120, 0);
        _best = Text(side, "0", 54, Face.Display, Palette.InkFaint);
        _best.SetPosition(120, 16);

        // A direction pad: four buttons around a pause button.
        Pad(side, LvSymbols.Up, 70, 110, () => Steer(LvDir.Top));
        Pad(side, LvSymbols.Left, 0, 180, () => Steer(LvDir.Left));
        Pad(side, LvSymbols.Right, 140, 180, () => Steer(LvDir.Right));
        Pad(side, LvSymbols.Down, 70, 250, () => Steer(LvDir.Bottom));
        var pause = IconButton(side, LvSymbols.Pause, 64, 64, Tint, Accent, 18);
        pause.SetPosition(70, 180);
        pause.Clicked += (_, _) => TogglePause();

        var restart = Button(side, "New game", 210, 44, Palette.Card, Palette.Ink, 16);
        restart.SetBorderWidth(1);
        restart.SetBorderColor(Palette.Rule);
        restart.Align(LvAlign.BottomLeft, 0, 0);
        restart.Clicked += (_, _) => NewGame();

        // Keys: the stage takes keyboard focus.
        stage.AddHandler(LvEventCode.Key, OnKey);
        App.FocusGroup?.Add(stage);
        LvGroup.Focus(stage);

        _clock = Every(140, Step);
        NewGame();

        if (Context.Screenshot)
        {
            // A game in progress: a long snake curling towards the apple.
            _body.Clear();
            foreach (var (x, y) in new[] { (14, 9), (13, 9), (12, 9), (11, 9), (11, 10), (11, 11), (11, 12), (12, 12), (13, 12), (14, 12), (15, 12), (16, 12), (17, 12), (17, 11), (17, 10), (17, 9), (17, 8), (17, 7), (16, 7), (15, 7), (14, 7) })
                _body.AddLast((x, y));
            _apple = (18, 9);
            _points = 18;
            _bestPoints = 31;
            _clock.Pause();
            Draw();
        }
    }

    private void Pad(LvObject parent, string symbol, int x, int y, Action action)
    {
        var button = IconButton(parent, symbol, 64, 64, Palette.Card, Palette.Ink, 18);
        button.SetBorderWidth(1);
        button.SetBorderColor(Palette.Rule);
        button.SetPosition(x, y);
        button.Clicked += (_, _) => action();
    }

    private void OnKey(object? sender, LvEventArgs e)
    {
        switch (e.Key)
        {
            case LvKey.Up: Steer(LvDir.Top); break;
            case LvKey.Down: Steer(LvDir.Bottom); break;
            case LvKey.Left: Steer(LvDir.Left); break;
            case LvKey.Right: Steer(LvDir.Right); break;
            case ' ' or 'p': TogglePause(); break;
            case LvKey.Enter: if (_over) NewGame(); break;
        }
    }

    private void Steer(LvDir direction)
    {
        (int X, int Y) wanted = direction switch
        {
            LvDir.Top => (0, -1),
            LvDir.Bottom => (0, 1),
            LvDir.Left => (-1, 0),
            LvDir.Right => (1, 0),
            _ => _queued,
        };

        // No turning back on yourself.
        if (wanted.X != -_direction.X || wanted.Y != -_direction.Y) _queued = wanted;
    }

    private void NewGame()
    {
        _body.Clear();
        for (var i = 0; i < 4; i++) _body.AddLast((6 - i, _rows / 2));
        _direction = _queued = (1, 0);
        _points = 0;
        _over = _paused = false;
        _clock.Period = 140;
        _clock.Resume();
        PlaceApple();
        Draw();
    }

    private void TogglePause()
    {
        if (_over) return;
        _paused = !_paused;
        if (_paused) _clock.Pause(); else _clock.Resume();
        Draw();
    }

    private void Step()
    {
        _direction = _queued;
        var head = _body.First!.Value;
        var next = (X: head.X + _direction.X, Y: head.Y + _direction.Y);

        var hitWall = next.X < 0 || next.Y < 0 || next.X >= _columns || next.Y >= _rows;
        if (hitWall || _body.Contains(next))
        {
            _over = true;
            _clock.Pause();
            _bestPoints = Math.Max(_bestPoints, _points);
            Draw();
            return;
        }

        _body.AddFirst(next);
        if (next == _apple)
        {
            _points++;
            _clock.Period = Math.Max(60, 140 - _points * 4);
            PlaceApple();
        }
        else
        {
            _body.RemoveLast();
        }

        Draw();
    }

    private void PlaceApple()
    {
        do _apple = (_random.Next(_columns), _random.Next(_rows));
        while (_body.Contains(_apple));
    }

    private void Draw()
    {
        // A checkerboard field, the apple, then the snake with a darker head.
        for (var y = 0; y < _rows; y++)
            for (var x = 0; x < _columns; x++)
                _board.FillRect(x * Cell, y * Cell, Cell, Cell, (x + y) % 2 == 0 ? _light : _dark);

        _board.FillCircle(_apple.X * Cell + Cell / 2, _apple.Y * Cell + Cell / 2, Cell / 2 - 2, _appleColour);

        var first = true;
        foreach (var (x, y) in _body)
        {
            _board.FillRect(x * Cell + 1, y * Cell + 1, Cell - 2, Cell - 2, first ? _head : _snake);
            first = false;
        }

        _board.Invalidate();

        _score.Text = _points.ToString();
        _best.Text = Math.Max(_bestPoints, _points).ToString();

        _message.Text = _over ? $"Game over - {_points} apples\nPress New game or Enter" : _paused ? "Paused" : "";
        _message.IsHidden = _message.Text.Length == 0;
        if (!_message.IsHidden)
        {
            _message.UpdateLayout();
            _message.SetPosition((_columns * Cell - _message.Width) / 2, (_rows * Cell - _message.Height) / 2);
        }
    }
}
