using Lvgl.Designer.Core;
using Lvgl.Ui;

namespace Lvgl.Designer.Tests;

public sealed class DesignerSessionTests : IDisposable
{
    private readonly ManualClock _clock = new();
    private readonly string _folder = Directory.CreateTempSubdirectory("lvglnet-designer-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); }
        catch (IOException) { }
    }

    private DesignerSession NewSession()
    {
        var session = new DesignerSession(_clock);
        session.Load(DesignerSession.StarterDocument(), path: null);
        return session;
    }

    [Fact]
    public void A_loaded_document_is_clean_with_nothing_to_undo()
    {
        var session = NewSession();

        Assert.False(session.IsDirty);
        Assert.False(session.CanUndo);
        Assert.Null(session.Selection);
    }

    [Fact]
    public void Adding_goes_into_the_selected_container_and_selects_the_new_widget()
    {
        var session = NewSession();
        var panel = session.Add(UiWidgetType.Panel);

        var label = session.Add(UiWidgetType.Label);

        Assert.Contains(label, panel.Children);
        Assert.Same(label, session.Selection);
        Assert.Equal("Label1", label.Name);
        Assert.True(session.IsDirty);
    }

    [Fact]
    public void Adding_beside_a_leaf_selection_goes_to_its_container()
    {
        var session = NewSession();
        var panel = session.Add(UiWidgetType.Panel);
        session.Add(UiWidgetType.Button);       // selected, inside the panel

        var slider = session.Add(UiWidgetType.Slider);

        Assert.Contains(slider, panel.Children);
    }

    [Fact]
    public void A_tab_needs_a_tab_view_and_a_new_tab_view_comes_with_one_tab()
    {
        var session = NewSession();
        session.Selection = null;

        Assert.Throws<InvalidOperationException>(() => session.Add(UiWidgetType.Tab));

        var tabs = session.Add(UiWidgetType.TabView);
        Assert.Single(tabs.Children);
        Assert.Equal(UiWidgetType.Tab, tabs.Children[0].Type);

        var second = session.Add(UiWidgetType.Tab);
        Assert.Equal(2, tabs.Children.Count);
        Assert.Same(second, tabs.Children[1]);
        Assert.Empty(session.Document.Validate());
    }

    [Fact]
    public void New_tiles_take_the_next_free_column()
    {
        var session = NewSession();
        var view = session.Add(UiWidgetType.TileView);

        var second = session.Add(UiWidgetType.Tile);
        session.Selection = view;
        var third = session.Add(UiWidgetType.Tile);

        Assert.Equal(1, second.Column);
        Assert.Equal(2, third.Column);
        Assert.Empty(session.Document.Validate());
    }

    [Fact]
    public void Delete_then_undo_restores_the_widget_and_the_selection()
    {
        var session = NewSession();
        var button = session.Document.Find("ActionButton")!;
        session.Selection = button;

        session.Delete(button);
        Assert.Null(session.Document.Find("ActionButton"));
        Assert.Null(session.Selection);

        session.Undo();
        Assert.NotNull(session.Document.Find("ActionButton"));

        session.Redo();
        Assert.Null(session.Document.Find("ActionButton"));
    }

    [Fact]
    public void Undo_restores_the_selection_by_position()
    {
        var session = NewSession();
        var button = session.Document.Find("ActionButton")!;
        session.Selection = button;

        session.MoveBy(button, 10, 0);
        session.Undo();

        Assert.Equal("ActionButton", session.Selection?.Name);
    }

    [Fact]
    public void Quick_edits_to_one_property_undo_together_and_slow_ones_separately()
    {
        var session = NewSession();
        var label = session.Document.Find("TitleLabel")!;

        session.Edit(label, "text", () => label.Text = "H");
        _clock.Advance(TimeSpan.FromMilliseconds(100));
        session.Edit(label, "text", () => label.Text = "He");
        _clock.Advance(TimeSpan.FromMilliseconds(100));
        session.Edit(label, "text", () => label.Text = "Hey");

        _clock.Advance(TimeSpan.FromSeconds(5));
        session.Edit(label, "text", () => label.Text = "Hey there");

        session.Undo();
        Assert.Equal("Hey", session.Document.Find("TitleLabel")!.Text);

        session.Undo();
        Assert.Equal("Hello from LVGL.Net", session.Document.Find("TitleLabel")!.Text);
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void A_whole_drag_is_one_undo_step()
    {
        var session = NewSession();
        var button = session.Document.Find("ActionButton")!;

        for (var i = 0; i < 10; i++)
        {
            session.MoveBy(button, 4, 2, "gesture-1");
            _clock.Advance(TimeSpan.FromMilliseconds(30));
        }

        Assert.Equal(40, button.X);

        session.Undo();
        Assert.Equal(0, session.Document.Find("ActionButton")!.X);
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void Reparenting_respects_structure()
    {
        var session = NewSession();
        var panel = session.Add(UiWidgetType.Panel);
        session.Selection = null;
        var tabs = session.Add(UiWidgetType.TabView);
        var label = session.Document.Find("TitleLabel")!;

        Assert.True(session.Reparent(label, panel));
        Assert.Contains(label, panel.Children);

        // A panel cannot go into itself, a label cannot go into a tab view directly, and a tab
        // cannot leave its tab view.
        Assert.False(session.Reparent(panel, panel));
        Assert.False(session.Reparent(label, tabs));
        Assert.False(session.Reparent(tabs.Children[0], null));

        Assert.True(session.Reparent(label, tabs.Children[0]));
        Assert.Empty(session.Document.Validate());
    }

    [Fact]
    public void Reordering_within_a_list_accounts_for_the_removal()
    {
        var session = NewSession();
        var first = session.Document.Children[0];
        var second = session.Document.Children[1];

        Assert.True(session.Reparent(first, null, index: 2));

        Assert.Same(second, session.Document.Children[0]);
        Assert.Same(first, session.Document.Children[1]);
    }

    [Fact]
    public void Duplicates_get_fresh_names_and_a_nudge()
    {
        var session = NewSession();
        var panel = session.Add(UiWidgetType.Panel);
        session.Add(UiWidgetType.Label);

        var copy = session.Duplicate(panel);

        Assert.NotEqual(panel.Name, copy.Name);
        Assert.NotEqual(panel.Children[0].Name, copy.Children[0].Name);
        Assert.Equal(panel.X + 16, copy.X);
        Assert.Empty(session.Document.Validate());
    }

    [Fact]
    public void Resizing_an_aligned_widget_writes_offsets_that_hold_the_other_edge()
    {
        var session = NewSession();
        var button = session.Document.Find("ActionButton")!;   // centred, 180x52, offset 0,0

        // On an 800x480 screen the centred button sits at (310, 214). Drag its right edge 40 px.
        var start = new Rect(310, 214, 180, 52);
        var target = new Rect(310, 214, 220, 52);

        session.ResizeTo(button, 0, 0, start, target);

        // Centring would move a 220-wide button 20 px left; the offset puts it back.
        Assert.Equal(220, button.Width);
        Assert.Equal(20, button.X);
    }

    [Fact]
    public void Renaming_a_style_updates_its_users_and_removing_it_detaches_them()
    {
        var session = NewSession();
        var style = session.AddStyle();
        var label = session.Document.Find("TitleLabel")!;
        session.ToggleStyle(label, style.Name, applied: true);

        Assert.True(session.RenameStyle(style, "Heading"));
        Assert.Equal(["Heading"], label.Styles);

        Assert.False(session.RenameStyle(style, "not valid"));

        session.RemoveStyle(style);
        Assert.Empty(label.Styles);
        Assert.Empty(session.Document.Validate());
    }

    [Fact]
    public void Assets_need_a_saved_layout_and_are_copied_beside_it()
    {
        var session = NewSession();
        var image = Path.Combine(_folder, "logo.png");
        File.WriteAllBytes(image, [1, 2, 3]);

        Assert.Throws<InvalidOperationException>(() => session.ImportAsset(image));

        var layoutFolder = Directory.CreateDirectory(Path.Combine(_folder, "layout")).FullName;
        session.Save(Path.Combine(layoutFolder, "screen.lvgl.json"));

        var first = session.ImportAsset(image);
        var second = session.ImportAsset(image);

        Assert.Equal("assets/logo.png", first);
        Assert.Equal("assets/logo-2.png", second);
        Assert.True(File.Exists(Path.Combine(layoutFolder, "assets", "logo.png")));

        // Re-importing a file that already lives in the assets folder just references it.
        Assert.Equal("assets/logo.png", session.ImportAsset(Path.Combine(layoutFolder, "assets", "logo.png")));
    }

    [Fact]
    public void Export_writes_the_class_and_copies_the_assets_it_uses()
    {
        var session = NewSession();
        var layoutFolder = Directory.CreateDirectory(Path.Combine(_folder, "layout")).FullName;
        session.Save(Path.Combine(layoutFolder, "screen.lvgl.json"));

        var source = Path.Combine(_folder, "logo.png");
        File.WriteAllBytes(source, [1, 2, 3]);
        var relative = session.ImportAsset(source);

        session.Selection = null;
        var image = session.Add(UiWidgetType.Image);
        session.Edit(image, "source", () =>
        {
            image.Source = relative;
            image.Symbol = null;
        });

        var exportFolder = Directory.CreateDirectory(Path.Combine(_folder, "app")).FullName;
        var copied = session.ExportCSharp(Path.Combine(exportFolder, "MainScreen.Designer.cs"), "App.Ui");

        Assert.Equal(["assets/logo.png"], copied);
        Assert.True(File.Exists(Path.Combine(exportFolder, "assets", "logo.png")));
        Assert.Contains("namespace App.Ui;", File.ReadAllText(Path.Combine(exportFolder, "MainScreen.Designer.cs")), StringComparison.Ordinal);
    }

    [Fact]
    public void Screen_size_changes_are_clamped_and_undoable()
    {
        var session = NewSession();

        session.SetScreenSize(480, 320);
        Assert.Equal((480, 320), (session.Document.Width, session.Document.Height));

        session.SetScreenSize(0, 99999);
        Assert.Equal((16, 4096), (session.Document.Width, session.Document.Height));

        session.Undo();
        Assert.Equal((800, 480), (session.Document.Width, session.Document.Height));
    }

    [Fact]
    public void Every_part_can_be_added_and_keeps_the_document_valid()
    {
        var session = NewSession();

        foreach (var type in Enum.GetValues<UiWidgetType>())
        {
            session.Selection = type switch
            {
                UiWidgetType.Tab => session.Add(UiWidgetType.TabView),
                UiWidgetType.Tile => session.Add(UiWidgetType.TileView),
                _ => null,
            };

            session.Add(type);
        }

        Assert.Empty(session.Document.Validate());
    }

    [Fact]
    public void Designators_number_each_kind_in_document_order()
    {
        var session = NewSession();
        session.Selection = null;
        session.Add(UiWidgetType.Button);

        var all = Designators.All(session.Document);

        Assert.Equal("LBL1", all[session.Document.Find("TitleLabel")!]);
        Assert.Equal("BTN1", all[session.Document.Find("ActionButton")!]);
        Assert.Equal("BTN2", all[session.Document.Find("Button1")!]);
        Assert.Equal("BTN2", Designators.For(session.Document, session.Document.Find("Button1")!));
    }

    /// <summary>A clock the test moves by hand, for the undo coalescing window.</summary>
    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 26, 9, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
