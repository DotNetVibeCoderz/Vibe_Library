using Lvgl.Ui;

namespace Lvgl.Tests;

/// <summary>Layout documents shared by the model, generator, builder and plugin tests.</summary>
internal static class TestLayouts
{
    /// <summary>
    /// One of everything: every widget kind, a shared style, a font and an image asset, a keyboard
    /// bound to a field declared after it, tabs and tiles. If the generator or the builder mishandles
    /// any kind, a test built on this document notices.
    /// </summary>
    public static UiDocument KitchenSink(string? fontAsset = null, string? imageAsset = null) => new()
    {
        Name = "KitchenSink",
        Width = 800,
        Height = 480,
        BackgroundColor = "#0F1720",
        Styles =
        [
            new UiStyle { Name = "CardStyle", BackgroundColor = "#1A2430", Radius = 12, BorderWidth = 0, Padding = 8 },
            new UiStyle { Name = "TitleStyle", TextColor = "#E6EDF3", FontSize = 20, Font = fontAsset },
        ],
        Children =
        [
            new UiNode
            {
                Type = UiWidgetType.Keyboard, Name = "Keys", Target = "NameField", KeyboardMode = LvKeyboardMode.TextLower,
                Hidden = true,
            },
            new UiNode
            {
                Type = UiWidgetType.TabView, Name = "Tabs", X = 0, Y = 0, Width = 800, Height = 480, TabBarPosition = LvDir.Top,
                Children =
                [
                    new UiNode
                    {
                        Type = UiWidgetType.Tab, Name = "OverviewTab", Text = "Overview",
                        Children =
                        [
                            new UiNode { Type = UiWidgetType.Label, Name = "Title", Text = "Hello", Styles = ["TitleStyle"] },
                            new UiNode { Type = UiWidgetType.Button, Name = "Go", Text = "Go", X = 10, Y = 40, Width = 100, Height = 40 },
                            new UiNode { Type = UiWidgetType.Slider, Name = "Level", X = 10, Y = 100, Minimum = 0, Maximum = 100, Value = 30, AccentColor = "#38BDF8" },
                            new UiNode { Type = UiWidgetType.Bar, Name = "Progress", X = 10, Y = 130, Value = 60 },
                            new UiNode { Type = UiWidgetType.Arc, Name = "Dial", X = 220, Y = 40, Width = 120, Height = 120, Value = 40, AccentColor = "#F59E0B" },
                            new UiNode { Type = UiWidgetType.Switch, Name = "Power", X = 380, Y = 40, Value = 1 },
                            new UiNode { Type = UiWidgetType.Checkbox, Name = "Agree", Text = "Agree", X = 380, Y = 90 },
                            new UiNode { Type = UiWidgetType.Dropdown, Name = "Mode", X = 520, Y = 40, Options = ["Auto", "Manual"] },
                            new UiNode { Type = UiWidgetType.Roller, Name = "Hour", X = 520, Y = 100, Options = ["08", "09", "10"] },
                            new UiNode { Type = UiWidgetType.TextArea, Name = "NameField", X = 10, Y = 180, Width = 200, Text = "" },
                            new UiNode { Type = UiWidgetType.Chart, Name = "Trend", X = 10, Y = 240, Width = 300, Height = 120, ChartType = LvChartType.Curve, SeriesColors = ["#38BDF8"] },
                        ],
                    },
                    new UiNode
                    {
                        Type = UiWidgetType.Tab, Name = "DevicesTab", Text = "Devices",
                        Children =
                        [
                            new UiNode
                            {
                                Type = UiWidgetType.Panel, Name = "Card", X = 10, Y = 10, Width = 300, Height = 200, Styles = ["CardStyle"],
                                Children =
                                [
                                    new UiNode { Type = UiWidgetType.Led, Name = "Status", X = 10, Y = 10, AccentColor = "#22C55E", Value = 200 },
                                    new UiNode { Type = UiWidgetType.Spinner, Name = "Busy", X = 60, Y = 10, Width = 40, Height = 40, AccentColor = "#38BDF8" },
                                    new UiNode { Type = UiWidgetType.Image, Name = "Logo", X = 120, Y = 10, Source = imageAsset, Symbol = imageAsset is null ? "Ok" : null },
                                ],
                            },
                            new UiNode { Type = UiWidgetType.Scale, Name = "Gauge", X = 330, Y = 10, Width = 160, Height = 160, ScaleMode = LvScaleMode.RoundInner, Minimum = 0, Maximum = 100, TickCount = 21, MajorTickEvery = 5 },
                            new UiNode { Type = UiWidgetType.Table, Name = "Readings", X = 10, Y = 220, Options = ["Sensor | Value", "CPU | 48", "GPU | 51"] },
                            new UiNode { Type = UiWidgetType.Spinbox, Name = "SetPoint", X = 330, Y = 200, Digits = 4, DecimalPlaces = 1, Minimum = 0, Maximum = 2000, Value = 215 },
                            new UiNode { Type = UiWidgetType.List, Name = "Menu", X = 520, Y = 10, Width = 200, Height = 200, Options = ["# Network", "Wi-Fi", "Ethernet"] },
                        ],
                    },
                    new UiNode
                    {
                        Type = UiWidgetType.Tab, Name = "PagesTab", Text = "Pages",
                        Children =
                        [
                            new UiNode
                            {
                                Type = UiWidgetType.TileView, Name = "Pages", Width = 400, Height = 300,
                                Children =
                                [
                                    new UiNode { Type = UiWidgetType.Tile, Name = "HomeTile", Column = 0, Row = 0, Children = [new UiNode { Type = UiWidgetType.Label, Text = "Home" }] },
                                    new UiNode { Type = UiWidgetType.Tile, Name = "MoreTile", Column = 1, Row = 0 },
                                ],
                            },
                        ],
                    },
                ],
            },
        ],
    };
}
