using Lvgl.Ui;

namespace Lvgl.Tests;

/// <summary>
/// Advisory checks for layouts that build but do not look right.
/// </summary>
/// <remarks>
/// These exist because of a real failure: asked for a thermostat screen, the assistant produced a
/// layout with <c>Align=TopMid</c> and <c>X=240</c> on a 480-wide screen. That validated clean,
/// opened in the designer, and rendered the widget off screen - because with Align set, X and Y
/// are offsets from the anchor rather than absolute coordinates. Nothing caught it, so nothing
/// told the model to fix it.
/// </remarks>
public class UiDocumentWarningTests
{
    [Fact]
    public void Align_combined_with_absolute_looking_coordinates_is_flagged()
    {
        var document = new UiDocument
        {
            Width = 480,
            Height = 320,
            Children =
            [
                new UiNode { Type = UiWidgetType.Label, Name = "Title", Align = LvAlign.TopMid, X = 240, Y = 20 },
            ],
        };

        // Still valid - it is legal, just wrong-looking - but it must be reported.
        Assert.Empty(document.Validate());

        var warning = Assert.Single(document.Warnings());
        Assert.Contains("Title", warning, StringComparison.Ordinal);
        Assert.Contains("offsets from the anchor", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void A_small_alignment_nudge_is_not_flagged()
    {
        var document = new UiDocument
        {
            Width = 480,
            Height = 320,
            Children =
            [
                new UiNode { Type = UiWidgetType.Label, Name = "Title", Align = LvAlign.TopMid, X = 0, Y = 20 },
                new UiNode { Type = UiWidgetType.Button, Name = "Save", Align = LvAlign.BottomRight, X = -16, Y = -16 },
            ],
        };

        Assert.Empty(document.Warnings());
    }

    [Fact]
    public void Absolute_positioning_without_align_is_not_flagged()
    {
        // The same coordinates are perfectly correct when Align is absent.
        var document = new UiDocument
        {
            Width = 480,
            Height = 320,
            Children = [new UiNode { Type = UiWidgetType.Button, Name = "Heat", X = 240, Y = 280 }],
        };

        Assert.Empty(document.Warnings());
    }

    [Fact]
    public void A_widget_larger_than_the_screen_is_flagged()
    {
        var document = new UiDocument
        {
            Width = 320,
            Height = 240,
            Children = [new UiNode { Type = UiWidgetType.Panel, Name = "Huge", Width = 900, Height = 700 }],
        };

        var warnings = document.Warnings();

        Assert.Contains(warnings, w => w.Contains("900 wide", StringComparison.Ordinal));
        Assert.Contains(warnings, w => w.Contains("700 tall", StringComparison.Ordinal));
    }

    [Fact]
    public void A_widget_positioned_off_screen_is_flagged()
    {
        var document = new UiDocument
        {
            Width = 480,
            Height = 320,
            Children = [new UiNode { Type = UiWidgetType.Label, Name = "Lost", X = 700, Y = 10 }],
        };

        Assert.Contains(document.Warnings(), w => w.Contains("outside the 480x320 screen", StringComparison.Ordinal));
    }

    [Fact]
    public void A_sound_layout_produces_no_warnings()
    {
        var document = new UiDocument
        {
            Width = 480,
            Height = 320,
            Children =
            [
                new UiNode { Type = UiWidgetType.Label, Name = "Title", Align = LvAlign.TopMid, Y = 16, Width = 200, Height = 40 },
                new UiNode { Type = UiWidgetType.Arc, Name = "Target", Align = LvAlign.Center, Width = 180, Height = 180 },
                new UiNode { Type = UiWidgetType.Button, Name = "Heat", X = 40, Y = 260, Width = 100, Height = 40 },
            ],
        };

        Assert.Empty(document.Validate());
        Assert.Empty(document.Warnings());
    }

    [Fact]
    public void The_design_plugin_feeds_warnings_back_so_the_model_can_self_correct()
    {
        var plugin = new Lvgl.Assistant.Plugins.LvglDesignPlugin();

        var result = plugin.CreateLayout("""
            {
              "Name": "Thermostat",
              "Width": 480,
              "Height": 320,
              "Children": [
                { "Type": "Label", "Name": "Temp", "Align": "TopMid", "X": 240, "Y": 20 }
              ]
            }
            """);

        Assert.Contains("Warnings", result, StringComparison.Ordinal);
        Assert.Contains("create_layout again", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Both_generated_halves_use_the_same_class_name()
    {
        // They disagreed in a real run: generate_csharp was given a class name and
        // generate_event_handlers was not, so the two halves could not compile together.
        var plugin = new Lvgl.Assistant.Plugins.LvglDesignPlugin();

        const string layout = """
            {
              "Name": "MainScreen",
              "Width": 480,
              "Height": 320,
              "Children": [ { "Type": "Button", "Name": "HeatButton", "Text": "Heat" } ]
            }
            """;

        var built = plugin.GenerateCSharp(layout, "App.Ui", "ThermostatControl");
        var handlers = plugin.GenerateEventHandlers(layout, "App.Ui", "ThermostatControl");

        Assert.Contains("partial class ThermostatControl", built, StringComparison.Ordinal);
        Assert.Contains("partial class ThermostatControl", handlers, StringComparison.Ordinal);
        Assert.DoesNotContain("partial class MainScreen", handlers, StringComparison.Ordinal);
    }
}
