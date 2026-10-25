using Lvgl.Gallery.Shell;
using Lvgl.Widgets;

namespace Lvgl.Gallery.Games;

/// <summary>
/// Noughts and crosses against the computer. "Perfect" searches the whole game tree (minimax), so
/// the best you can do is draw; "Casual" sometimes picks a random square.
/// </summary>
internal sealed class TicTacToe : Demo
{
    private readonly TicTacToeBoard _board = new();
    private readonly Random _random = new(9);
    private readonly LvButton[] _squares = new LvButton[9];
    private LvLabel _status = null!, _you = null!, _draws = null!, _computer = null!;
    private LvButton _perfect = null!, _casual = null!;
    private bool _isPerfect = true;
    private bool _awaitingReply;
    private int _wins, _drawCount, _losses;

    public override Category Category => Category.Games;
    public override string Title => "Noughts and crosses";
    public override string Summary => "You are X. The computer plays O with a full minimax search - on Perfect, a draw is the best you can do.";
    public override string BuiltWith => "LvButton  LvTimer  minimax";

    protected override void Build(LvObject stage, int width, int height)
    {
        var size = height;
        var cell = (size - 2 * 10) / 3;

        for (var i = 0; i < 9; i++)
        {
            var square = Button(stage, "", cell, cell, Palette.Card, Palette.Ink, 20);
            square.SetPosition(i % 3 * (cell + 10), i / 3 * (cell + 10));
            square.SetRadius(16);
            square.SetBorderWidth(1);
            square.SetBorderColor(Palette.Rule);
            square.SetFont(Fonts[Face.Display, 96]);
            var index = i;
            square.Clicked += (_, _) => Play(index);
            _squares[i] = square;
        }

        var side = Bare(new LvPanel(stage, width - size - 30, height));
        side.SetPosition(size + 30, 0);
        var sideWidth = width - size - 30;

        _status = Text(side, "", 30, Face.Display, Palette.Ink);
        _status.Width = sideWidth;
        _status.SetPosition(0, 0);

        _you = Tally(side, 0, sideWidth, "You");
        _draws = Tally(side, 1, sideWidth, "Draws");
        _computer = Tally(side, 2, sideWidth, "Computer");

        Caption(side, "Computer plays").SetPosition(0, 196);
        _perfect = Button(side, "Perfect", (sideWidth - 8) / 2, 40, size: 15);
        _perfect.SetPosition(0, 220);
        _perfect.Clicked += (_, _) => SetLevel(true);
        _casual = Button(side, "Casual", (sideWidth - 8) / 2, 40, size: 15);
        _casual.SetPosition((sideWidth + 8) / 2, 220);
        _casual.Clicked += (_, _) => SetLevel(false);

        var again = Button(side, "New round", sideWidth, 48, Palette.Ink, Palette.White, 16);
        again.Align(LvAlign.BottomLeft, 0, 0);
        again.Clicked += (_, _) => NewRound();

        SetLevel(true);
        Every(400, Reply);

        if (Context.Screenshot)
        {
            // A finished round the computer won along the top row.
            (_wins, _drawCount, _losses) = (1, 4, 2);
            foreach (var (square, mark) in new[] { (4, 'X'), (0, 'O'), (8, 'X'), (2, 'O'), (6, 'X'), (1, 'O') }) _board.Mark(square, mark);
        }

        Paint();
    }

    private LvLabel Tally(LvObject side, int index, int sideWidth, string name)
    {
        var tileWidth = (sideWidth - 16) / 3;
        var tile = Card(side, index * (tileWidth + 8), 78, tileWidth, 96);
        Caption(tile, name).SetPosition(12, 12);
        var value = Text(tile, "0", 40, Face.Display, Palette.Ink);
        value.SetPosition(12, 34);
        return value;
    }

    private void SetLevel(bool perfect)
    {
        _isPerfect = perfect;
        _perfect.SetBackgroundColor(perfect ? Accent : Palette.Sunken);
        _perfect.SetTextColor(perfect ? Palette.White : Palette.Ink);
        _casual.SetBackgroundColor(!perfect ? Accent : Palette.Sunken);
        _casual.SetTextColor(!perfect ? Palette.White : Palette.Ink);
    }

    private void Play(int index)
    {
        if (_awaitingReply || _board.Winner() is not null || !_board.Mark(index, 'X')) return;

        if (_board.Winner() is null)
        {
            // The reply comes a beat later, so it reads as a move rather than an echo.
            _awaitingReply = true;
            Paint();
            _status.Text = "Thinking...";
            return;
        }

        Finish();
    }

    private void Reply()
    {
        if (!_awaitingReply) return;
        _awaitingReply = false;

        var move = !_isPerfect && _random.Next(3) == 0 ? _board.RandomEmpty(_random) : _board.BestMove('O');
        if (move >= 0) _board.Mark(move, 'O');
        Finish();
    }

    private void Finish()
    {
        switch (_board.Winner())
        {
            case 'X': _wins++; break;
            case 'O': _losses++; break;
            case '-': _drawCount++; break;
        }

        Paint();
    }

    private void NewRound()
    {
        _board.Clear();
        Paint();
    }

    private void Paint()
    {
        var line = _board.WinningLine();
        for (var i = 0; i < 9; i++)
        {
            var mark = _board[i];
            _squares[i].Text = mark == ' ' ? "" : mark.ToString();
            _squares[i].SetTextColor(mark == 'X' ? Accent : Palette.Ink);
            var winning = line is not null && Array.IndexOf(line, i) >= 0;
            _squares[i].SetBackgroundColor(winning ? Tint : Palette.Card);
            _squares[i].SetBorderColor(winning ? Accent : Palette.Rule);
        }

        _status.Text = _board.Winner() switch
        {
            'X' => "You win this one.",
            'O' => "The computer wins.",
            '-' => "A draw.",
            _ => _board.IsEmpty ? "Your move - you are X." : "Your move.",
        };

        _you.Text = _wins.ToString();
        _draws.Text = _drawCount.ToString();
        _computer.Text = _losses.ToString();
    }
}

/// <summary>A 3 x 3 board and a minimax search over it.</summary>
internal sealed class TicTacToeBoard
{
    private static readonly int[][] Lines =
    [
        [0, 1, 2], [3, 4, 5], [6, 7, 8], [0, 3, 6], [1, 4, 7], [2, 5, 8], [0, 4, 8], [2, 4, 6],
    ];

    private readonly char[] _cells = "         ".ToCharArray();

    public char this[int index] => _cells[index];
    public bool IsEmpty => Array.TrueForAll(_cells, c => c == ' ');

    public void Clear() => Array.Fill(_cells, ' ');

    public bool Mark(int index, char player)
    {
        if (_cells[index] != ' ') return false;
        _cells[index] = player;
        return true;
    }

    /// <summary>'X' or 'O' for a win, '-' for a draw, null while the game is open.</summary>
    public char? Winner()
    {
        var line = WinningLine();
        if (line is not null) return _cells[line[0]];
        return Array.IndexOf(_cells, ' ') < 0 ? '-' : null;
    }

    public int[]? WinningLine() =>
        Lines.FirstOrDefault(l => _cells[l[0]] != ' ' && _cells[l[0]] == _cells[l[1]] && _cells[l[1]] == _cells[l[2]]);

    public int RandomEmpty(Random random)
    {
        var empty = Enumerable.Range(0, 9).Where(i => _cells[i] == ' ').ToArray();
        return empty.Length == 0 ? -1 : empty[random.Next(empty.Length)];
    }

    /// <summary>The move that maximises <paramref name="player"/>'s outcome, preferring quicker wins.</summary>
    public int BestMove(char player)
    {
        var best = -1;
        var bestScore = int.MinValue;
        for (var i = 0; i < 9; i++)
        {
            if (_cells[i] != ' ') continue;
            _cells[i] = player;
            var score = -Negamax(Opponent(player), 1);
            _cells[i] = ' ';
            if (score > bestScore) (best, bestScore) = (i, score);
        }

        return best;
    }

    // Score from the point of view of the side to move: +10 for a win, minus depth so a faster
    // win (or a slower loss) is preferred.
    private int Negamax(char toMove, int depth)
    {
        var winner = Winner();
        if (winner == '-') return 0;
        if (winner is { } w) return w == toMove ? 10 - depth : depth - 10;

        var best = int.MinValue;
        for (var i = 0; i < 9; i++)
        {
            if (_cells[i] != ' ') continue;
            _cells[i] = toMove;
            best = Math.Max(best, -Negamax(Opponent(toMove), depth + 1));
            _cells[i] = ' ';
        }

        return best;
    }

    private static char Opponent(char player) => player == 'X' ? 'O' : 'X';
}
