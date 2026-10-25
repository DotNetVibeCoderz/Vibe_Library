using Lvgl.Gallery;
using Lvgl.Gallery.Games;
using Lvgl.Gallery.Shell;
using Lvgl.Gallery.Tools;
using Lvgl.Gallery.Work;
using Lvgl.Widgets;

namespace Lvgl.Tests;

/// <summary>
/// The gallery's engines - the parts of each demo that are not UI - plus its SQLite store and its
/// source highlighter. These run without the native library.
/// </summary>
public sealed class GalleryEngineTests
{
    [Theory]
    [InlineData("1+2*3", 7)]
    [InlineData("(1+2)*3", 9)]
    [InlineData("-4+10", 6)]
    [InlineData("2*-3", -6)]
    [InlineData("1250*0.15", 187.5)]
    [InlineData("(64+36)/8", 12.5)]
    [InlineData("10-4-3", 3)]
    [InlineData("100/10/2", 5)]
    public void The_calculator_follows_precedence_and_associativity(string expression, double expected)
    {
        Assert.True(CalculatorEngine.TryEvaluate(expression, out var value));
        Assert.Equal(expected, value, 9);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1+")]
    [InlineData("(2+3")]
    [InlineData("4/0")]
    [InlineData("2)")]
    public void The_calculator_rejects_what_it_cannot_evaluate(string expression) =>
        Assert.False(CalculatorEngine.TryEvaluate(expression, out _));

    [Theory]
    [InlineData("Temperature", "Celsius", "Fahrenheit", 100, 212)]
    [InlineData("Temperature", "Fahrenheit", "Kelvin", 32, 273.15)]
    [InlineData("Length", "Mile", "Metre", 1, 1609.344)]
    [InlineData("Data", "Gibibyte", "Mebibyte", 1, 1024)]
    [InlineData("Energy", "Kilowatt hour", "Joule", 1, 3.6e6)]
    public void Units_convert_through_their_base(string quantity, string from, string to, double value, double expected)
    {
        var units = UnitTables.Quantities.Single(q => q.Name == quantity).Units;
        var result = UnitTables.Convert(value, units.Single(u => u.Name == from), units.Single(u => u.Name == to));
        Assert.Equal(expected, result, 6);
    }

    [Fact]
    public void A_2048_row_merges_each_pair_once_towards_the_edge()
    {
        var board = new Board2048(new Random(1));
        board.Load(new[,] { { 2, 2, 2, 2 }, { 4, 4, 8, 0 }, { 0, 0, 0, 0 }, { 2, 0, 0, 2 } }, 0);

        var merged = board.Move(LvDir.Left);

        Assert.NotNull(merged);
        Assert.Equal([4, 4], new[] { board[0, 0], board[0, 1] });
        Assert.Equal([8, 8], new[] { board[1, 0], board[1, 1] });   // 4+4, then the 8 is not merged again
        Assert.Equal(4, board[3, 0]);
        Assert.Equal(8 + 8 + 4, board.Score);
    }

    [Fact]
    public void A_2048_move_that_changes_nothing_is_not_a_move()
    {
        var board = new Board2048(new Random(1));
        board.Load(new[,] { { 2, 4, 0, 0 }, { 0, 0, 0, 0 }, { 0, 0, 0, 0 }, { 0, 0, 0, 0 } }, 0);
        Assert.Null(board.Move(LvDir.Left));
        Assert.Null(board.Move(LvDir.Top));
    }

    [Fact]
    public void The_computer_takes_a_win_and_blocks_a_loss()
    {
        var board = new TicTacToeBoard();
        board.Mark(0, 'O'); board.Mark(1, 'O');
        board.Mark(3, 'X'); board.Mark(4, 'X');
        Assert.Equal(2, board.BestMove('O'));   // win beats block

        var defending = new TicTacToeBoard();
        defending.Mark(0, 'X'); defending.Mark(1, 'X'); defending.Mark(4, 'O');
        Assert.Equal(2, defending.BestMove('O'));
    }

    [Fact]
    public void The_perfect_computer_never_loses()
    {
        // Every possible sequence of X moves against minimax O: X never wins.
        static void Explore(TicTacToeBoard board)
        {
            for (var i = 0; i < 9; i++)
            {
                if (board[i] != ' ') continue;
                var copy = Copy(board);
                copy.Mark(i, 'X');
                Assert.NotEqual('X', copy.Winner());
                if (copy.Winner() is not null) continue;

                copy.Mark(copy.BestMove('O'), 'O');
                if (copy.Winner() is null) Explore(copy);
            }
        }

        static TicTacToeBoard Copy(TicTacToeBoard board)
        {
            var copy = new TicTacToeBoard();
            for (var i = 0; i < 9; i++) if (board[i] != ' ') copy.Mark(i, board[i]);
            return copy;
        }

        Explore(new TicTacToeBoard());
    }

    [Fact]
    public void The_highlighter_colours_code_and_leaves_hash_lines_plain()
    {
        var coloured = CSharpHighlighter.Colour("var x = new LvLabel(parent, \"hi\"); // note");

        Assert.Contains($"{CSharpHighlighter.Keyword.Rgb:X6} var#", coloured);
        Assert.Contains($"{CSharpHighlighter.TypeName.Rgb:X6} LvLabel#", coloured);
        Assert.Contains($"{CSharpHighlighter.StringLiteral.Rgb:X6} \"hi\"#", coloured);
        Assert.Contains($"{CSharpHighlighter.Comment.Rgb:X6} // note#", coloured);

        // LVGL's recolour syntax cannot escape '#', so such a line must not be recoloured.
        Assert.False(CSharpHighlighter.CanColour("#if DEBUG"));
        Assert.True(CSharpHighlighter.CanColour("var x = 1;"));
    }

    [Fact]
    public void Every_demo_embeds_its_own_source()
    {
        foreach (var demo in DemoCatalog.Create())
        {
            var source = SourceView.Load(demo.SourceFile);
            Assert.Contains($"class {demo.GetType().Name}", source);
        }
    }

    [Fact]
    public void Demo_titles_are_unique_and_every_category_has_demos()
    {
        var demos = DemoCatalog.Create();
        Assert.Equal(demos.Count, demos.Select(d => DemoCatalog.Slug(d.Title)).Distinct().Count());
        foreach (var category in Categories.All) Assert.Contains(demos, d => d.Category == category);
        Assert.Same(demos.First(d => d is Snake), DemoCatalog.Find(demos, "snake"));
    }

    [Fact]
    public void The_database_seeds_once_and_records_changes()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lvglnet-gallery-test-{Guid.NewGuid():N}.db");
        try
        {
            using (var db = new GalleryDatabase(path))
            {
                var products = db.Products();
                Assert.Equal(15, products.Count);
                Assert.Equal(12, db.SalesByMonth().Count);
                Assert.Equal(GalleryDatabase.Regions.Length, db.SalesByRegion().Count);

                var pi = products.Single(p => p.Name == "Raspberry Pi 5 (8 GB)");
                db.AdjustQuantity(pi.Id, -100);   // clamps at zero
                db.AddProduct("Test probe", "Sensors", 3, 1.5m);
                db.MoveWorkItem(db.WorkItems()[0].Id, "doing");

                Assert.Single(db.Products("probe"));
                Assert.Equal(0, db.Products("Pi 5").Single().Quantity);
            }

            // Reopened: the changes persisted and the seed did not run again.
            using var reopened = new GalleryDatabase(path);
            Assert.Equal(16, reopened.Products().Count);
            Assert.Equal("doing", reopened.WorkItems()[0].Status);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

/// <summary>Every demo, built and run against the real LVGL build, in Demo and Source view.</summary>
[Collection(LvglCollection.Name)]
public sealed class GalleryIntegrationTests(LvglFixture lvgl)
{
    [Fact]
    public void Every_demo_builds_runs_and_tears_down_cleanly()
    {
        if (lvgl.ShouldSkip()) return;
        lvgl.ResetScreen();

        var failures = new List<string>();
        void OnFailure(object? sender, LvglCallbackFailedEventArgs e) => failures.Add($"{e.Source}: {e.Exception.Message}");

        var database = Path.Combine(Path.GetTempPath(), $"lvglnet-gallery-it-{Guid.NewGuid():N}.db");
        Typeface fonts = null!;
        GalleryDatabase? db = null;
        LvglRuntime.CallbackFailed += OnFailure;

        try
        {
            lvgl.Invoke(() => fonts = new Typeface(Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts")));

            foreach (var prototype in DemoCatalog.Create())
            {
                Demo demo = null!;
                lvgl.Invoke(() =>
                {
                    // The stage the gallery gives a demo at 1024 x 600; larger than the fixture's
                    // surface, which only clips the drawing.
                    var stage = new LvPanel(lvgl.Application.Screen, 744, 428);
                    stage.SetPadding(0);
                    stage.UpdateLayout();

                    var context = new DemoContext(lvgl.Application, fonts, () => db ??= new GalleryDatabase(database));
                    demo = (Demo)Activator.CreateInstance(prototype.GetType())!;
                    demo.Mount(stage, context);
                });

                // Let its timers and animations run, then tear it down the way the shell does.
                lvgl.Pump(150);
                lvgl.Invoke(() =>
                {
                    lvgl.Application.Screen.Clear();
                    demo.Unmount();

                    var stage = new LvPanel(lvgl.Application.Screen, 744, 428);
                    stage.UpdateLayout();
                    SourceView.Build(stage, 744, 428, prototype.SourceFile, fonts);
                });

                lvgl.RenderFrame();
                lvgl.ResetScreen();
            }
        }
        finally
        {
            LvglRuntime.CallbackFailed -= OnFailure;
            lvgl.ResetScreen();
            lvgl.Invoke(() => fonts?.Dispose());
            db?.Dispose();
            File.Delete(database);
        }

        Assert.Empty(failures);
    }
}
