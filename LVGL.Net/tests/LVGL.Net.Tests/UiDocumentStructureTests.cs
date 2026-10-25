using Lvgl.Ui;

namespace Lvgl.Tests;

/// <summary>Validation of the version-2 model: tabs, tiles, styles, keyboards, assets.</summary>
public class UiDocumentStructureTests
{
    [Fact]
    public void The_kitchen_sink_is_valid()
    {
        Assert.Empty(TestLayouts.KitchenSink(fontAsset: "assets/brand.ttf", imageAsset: "assets/logo.png").Validate());
    }

    [Fact]
    public void A_version_1_file_still_opens_and_is_stamped_current()
    {
        const string json = """{ "Version": 1, "Name": "Old", "Children": [ { "Type": "Label", "Text": "hi" } ] }""";

        var document = UiJson.Parse(json);

        Assert.Equal(UiDocument.CurrentVersion, document.Version);
        Assert.Empty(document.Validate());
    }

    [Fact]
    public void A_newer_format_is_refused_rather_than_half_read()
    {
        var json = UiJson.ToJson(new UiDocument { Version = UiDocument.CurrentVersion + 1 });
        Assert.Throws<InvalidDataException>(() => UiJson.Parse(json));
    }

    [Fact]
    public void Only_tabs_may_sit_in_a_tab_view()
    {
        var document = new UiDocument
        {
            Children =
            [
                new UiNode
                {
                    Type = UiWidgetType.TabView, Name = "Tabs",
                    Children = [new UiNode { Type = UiWidgetType.Label, Name = "Stray" }],
                },
            ],
        };

        Assert.Contains(document.Validate(), p => p.Contains("'Stray'", StringComparison.Ordinal) && p.Contains("not a Tab", StringComparison.Ordinal));
    }

    [Fact]
    public void A_tab_outside_a_tab_view_is_reported()
    {
        var topLevel = new UiDocument { Children = [new UiNode { Type = UiWidgetType.Tab, Name = "Loose" }] };
        var inPanel = new UiDocument
        {
            Children = [new UiNode { Type = UiWidgetType.Panel, Children = [new UiNode { Type = UiWidgetType.Tab, Name = "Loose" }] }],
        };

        Assert.Contains(topLevel.Validate(), p => p.Contains("TabView", StringComparison.Ordinal));
        Assert.Contains(inPanel.Validate(), p => p.Contains("TabView", StringComparison.Ordinal));
    }

    [Fact]
    public void Tiles_need_unique_cells()
    {
        var document = new UiDocument
        {
            Children =
            [
                new UiNode
                {
                    Type = UiWidgetType.TileView,
                    Children =
                    [
                        new UiNode { Type = UiWidgetType.Tile, Column = 1, Row = 0 },
                        new UiNode { Type = UiWidgetType.Tile, Column = 1, Row = 0 },
                    ],
                },
            ],
        };

        Assert.Contains(document.Validate(), p => p.Contains("share column 1, row 0", StringComparison.Ordinal));
    }

    [Fact]
    public void Leaf_widgets_cannot_have_children()
    {
        var document = new UiDocument
        {
            Children = [new UiNode { Type = UiWidgetType.Button, Name = "Go", Children = [new UiNode { Type = UiWidgetType.Label }] }],
        };

        Assert.Contains(document.Validate(), p => p.Contains("cannot contain other widgets", StringComparison.Ordinal));
    }

    [Fact]
    public void A_keyboard_must_target_a_text_area()
    {
        var document = new UiDocument
        {
            Children =
            [
                new UiNode { Type = UiWidgetType.Label, Name = "NotAField" },
                new UiNode { Type = UiWidgetType.Keyboard, Name = "Keys", Target = "NotAField" },
            ],
        };

        Assert.Contains(document.Validate(), p => p.Contains("not a TextArea", StringComparison.Ordinal));
    }

    [Fact]
    public void An_unconnected_keyboard_is_only_a_warning()
    {
        var document = new UiDocument { Children = [new UiNode { Type = UiWidgetType.Keyboard, Name = "Keys" }] };

        Assert.Empty(document.Validate());
        Assert.Contains(document.Warnings(), w => w.Contains("not connected", StringComparison.Ordinal));
    }

    [Fact]
    public void Styles_must_exist_and_be_unique()
    {
        var document = new UiDocument
        {
            Styles = [new UiStyle { Name = "Card" }, new UiStyle { Name = "Card" }, new UiStyle { Name = "bad name" }],
            Children = [new UiNode { Type = UiWidgetType.Panel, Styles = ["Missing"] }],
        };

        var problems = document.Validate();

        Assert.Contains(problems, p => p.Contains("'Card' is used more than once", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("'bad name'", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("'Missing', which does not exist", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("C:/images/logo.png")]
    [InlineData("/home/pi/logo.png")]
    [InlineData("../shared/logo.png")]
    public void Assets_must_stay_inside_the_layout_folder(string path)
    {
        var document = new UiDocument { Children = [new UiNode { Type = UiWidgetType.Image, Source = path }] };

        Assert.NotEmpty(document.Validate());
    }

    [Fact]
    public void Asset_paths_are_listed_once_each()
    {
        var document = TestLayouts.KitchenSink(fontAsset: "assets/brand.ttf", imageAsset: "assets/logo.png");
        document.Children.Add(new UiNode { Type = UiWidgetType.Image, Source = "assets/logo.png" });

        Assert.Equal(["assets/brand.ttf", "assets/logo.png"], document.AssetPaths().Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(11, 0)]
    [InlineData(4, 4)]
    public void Spinbox_digit_formats_are_checked(int digits, int places)
    {
        var document = new UiDocument
        {
            Children = [new UiNode { Type = UiWidgetType.Spinbox, Digits = digits, DecimalPlaces = places }],
        };

        Assert.NotEmpty(document.Validate());
    }

    [Fact]
    public void Image_symbols_must_be_real_symbols()
    {
        var good = new UiDocument { Children = [new UiNode { Type = UiWidgetType.Image, Symbol = "Wifi" }] };
        var bad = new UiDocument { Children = [new UiNode { Type = UiWidgetType.Image, Symbol = "Unicorn" }] };

        Assert.Empty(good.Validate());
        Assert.Contains(bad.Validate(), p => p.Contains("Unicorn", StringComparison.Ordinal));
        Assert.Contains("Wifi", UiSymbols.Names);
    }

    [Fact]
    public void Saved_json_holds_only_what_was_set()
    {
        var json = UiJson.ToJson(new UiDocument
        {
            Name = "Small",
            Children = [new UiNode { Type = UiWidgetType.Label, Text = "hi" }],
        });

        Assert.DoesNotContain("\"Options\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Styles\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Hidden\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Children\": []", json, StringComparison.Ordinal);

        // What was omitted reads back as its default, and what was set survives.
        var label = UiJson.Parse(json).Children[0];
        Assert.Empty(label.Options);
        Assert.False(label.Hidden);
        Assert.Equal("hi", label.Text);

        var hidden = UiJson.ToJson(new UiDocument { Children = [new UiNode { Type = UiWidgetType.Label, Hidden = true }] });
        Assert.True(UiJson.Parse(hidden).Children[0].Hidden);
    }

    [Fact]
    public void List_and_table_options_parse_into_entries()
    {
        var list = new UiNode { Type = UiWidgetType.List, Options = ["# Network", "Wi-Fi", "#Other", "About"] };
        var table = new UiNode { Type = UiWidgetType.Table, Options = ["A | B", "1|2|3"] };

        Assert.Equal([(true, "Network"), (false, "Wi-Fi"), (true, "Other"), (false, "About")], list.ListEntries());

        var rows = table.TableRows();
        Assert.Equal(["A", "B"], rows[0]);
        Assert.Equal(["1", "2", "3"], rows[1]);
    }

    [Fact]
    public void The_new_properties_round_trip_and_stay_null_when_unset()
    {
        var original = TestLayouts.KitchenSink(fontAsset: "assets/brand.ttf", imageAsset: "assets/logo.png");
        var restored = UiJson.Parse(UiJson.ToJson(original));

        Assert.Equal(2, restored.Styles.Count);
        Assert.Equal("assets/brand.ttf", restored.FindStyle("TitleStyle")!.Font);
        Assert.Equal("NameField", restored.Find("Keys")!.Target);
        Assert.Equal(LvScaleMode.RoundInner, restored.Find("Gauge")!.ScaleMode);
        Assert.Equal(1, restored.Find("MoreTile")!.Column);
        Assert.Equal(["CardStyle"], restored.Find("Card")!.Styles);

        var label = UiJson.Parse(UiJson.ToJson(new UiDocument { Children = [new UiNode { Type = UiWidgetType.Label }] })).Children[0];
        Assert.Null(label.AccentColor);
        Assert.Null(label.Font);
        Assert.Null(label.Digits);
        Assert.Empty(label.Styles);

        // Unset properties are not written at all, so a document stays as small as what was set.
        var json = UiJson.ToJson(new UiDocument { Children = [new UiNode { Type = UiWidgetType.Label }] });
        Assert.DoesNotContain("AccentColor", json, StringComparison.Ordinal);
    }
}
