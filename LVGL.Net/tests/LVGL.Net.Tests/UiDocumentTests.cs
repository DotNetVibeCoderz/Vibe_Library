using Lvgl.Ui;

namespace Lvgl.Tests;

public class UiDocumentTests
{
    private static UiDocument SampleDocument() => new()
    {
        Name = "Dashboard",
        Width = 800,
        Height = 480,
        BackgroundColor = "#0F1720",
        Children =
        [
            new UiNode
            {
                Type = UiWidgetType.Panel,
                Name = "Card",
                Width = 400,
                Height = 200,
                Radius = 12,
                Children =
                [
                    new UiNode { Type = UiWidgetType.Label, Name = "TitleLabel", Text = "Hello", FontSize = 20 },
                    new UiNode { Type = UiWidgetType.Slider, Name = "Level", Minimum = 0, Maximum = 100, Value = 40 },
                ],
            },
            new UiNode { Type = UiWidgetType.Chart, Name = "Trace", SeriesColors = ["#38BDF8", "#FB7185"] },
        ],
    };

    [Fact]
    public void Round_trips_through_json()
    {
        var original = SampleDocument();
        var restored = UiJson.Parse(UiJson.ToJson(original));

        Assert.Equal(original.Name, restored.Name);
        Assert.Equal(original.Width, restored.Width);
        Assert.Equal(original.BackgroundColor, restored.BackgroundColor);
        Assert.Equal(original.Descendants().Count(), restored.Descendants().Count());

        var slider = restored.Find("Level");
        Assert.NotNull(slider);
        Assert.Equal(UiWidgetType.Slider, slider.Type);
        Assert.Equal(40, slider.Value);
        Assert.Equal(100, slider.Maximum);

        var chart = restored.Find("Trace");
        Assert.NotNull(chart);
        Assert.Equal(2, chart.SeriesColors.Count);
    }

    [Fact]
    public void Unset_optional_properties_stay_unset_after_a_round_trip()
    {
        // The nullable properties carry the difference between "LVGL default" and "explicitly 0";
        // a round trip that turned null into 0 would silently restyle every saved layout.
        var document = new UiDocument { Children = [new UiNode { Type = UiWidgetType.Label }] };

        var restored = UiJson.Parse(UiJson.ToJson(document));
        var node = restored.Children[0];

        Assert.Null(node.Width);
        Assert.Null(node.Radius);
        Assert.Null(node.FontSize);
        Assert.Null(node.Align);
    }

    [Fact]
    public void Enums_are_written_as_names_so_files_survive_reordering()
    {
        var document = new UiDocument
        {
            Children = [new UiNode { Type = UiWidgetType.Chart, ChartType = LvChartType.Bar, Align = LvAlign.BottomRight }],
        };

        var json = UiJson.ToJson(document);

        Assert.Contains("\"Chart\"", json, StringComparison.Ordinal);
        Assert.Contains("\"Bar\"", json, StringComparison.Ordinal);
        Assert.Contains("\"BottomRight\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Descendants_walks_the_whole_tree_depth_first()
    {
        var names = SampleDocument().Descendants().Select(n => n.Name ?? string.Empty).ToArray();

        Assert.Equal(["Card", "TitleLabel", "Level", "Trace"], names);
    }

    [Fact]
    public void Validate_accepts_a_sound_document()
    {
        Assert.Empty(SampleDocument().Validate());
    }

    [Fact]
    public void Validate_reports_duplicate_names()
    {
        var document = new UiDocument
        {
            Children =
            [
                new UiNode { Type = UiWidgetType.Label, Name = "Same" },
                new UiNode { Type = UiWidgetType.Label, Name = "Same" },
            ],
        };

        Assert.Contains(document.Validate(), p => p.Contains("more than once", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_reports_names_that_are_not_identifiers()
    {
        var document = new UiDocument
        {
            Children = [new UiNode { Type = UiWidgetType.Label, Name = "my label" }],
        };

        Assert.Contains(document.Validate(), p => p.Contains("valid C# identifier", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_reports_inverted_ranges_and_bad_colours()
    {
        var document = new UiDocument
        {
            Children = [new UiNode { Type = UiWidgetType.Slider, Minimum = 100, Maximum = 0, BackgroundColor = "nope" }],
        };

        var problems = document.Validate();

        Assert.Contains(problems, p => p.Contains("greater than maximum", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("invalid background colour", StringComparison.Ordinal));
    }

    [Fact]
    public void A_newer_format_version_is_rejected_rather_than_misread()
    {
        var json = UiJson.ToJson(new UiDocument { Version = 99 });
        Assert.Throws<InvalidDataException>(() => UiJson.Parse(json));
    }

    [Theory]
    [InlineData("Valid", true)]
    [InlineData("_underscore", true)]
    [InlineData("With123", true)]
    [InlineData("1Leading", false)]
    [InlineData("has space", false)]
    [InlineData("has-dash", false)]
    [InlineData("", false)]
    public void IsValidIdentifier_matches_C_sharp_rules(string name, bool expected)
    {
        Assert.Equal(expected, UiDocument.IsValidIdentifier(name));
    }
}
