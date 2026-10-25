using Lvgl.Ui;
using Lvgl.Widgets;

namespace Lvgl.Tests;

/// <summary>Builds version-2 layouts into a real LVGL instance.</summary>
[Collection(LvglCollection.Name)]
public sealed class UiBuilderIntegrationTests(LvglFixture lvgl) : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("lvglnet-layout-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); }
        catch (IOException) { }
    }

    [Fact]
    public void The_kitchen_sink_builds_every_node()
    {
        if (lvgl.ShouldSkip()) return;
        lvgl.ResetScreen();

        var assets = Directory.CreateDirectory(Path.Combine(_folder, "assets")).FullName;
        TestImages.WritePng(Path.Combine(assets, "logo.png"), 24, 24, 0xF43F5Eu);

        var font = TestImages.FindTrueTypeFont();
        if (font is not null) File.Copy(font, Path.Combine(assets, "brand.ttf"));

        var document = TestLayouts.KitchenSink(
            fontAsset: font is null ? null : "assets/brand.ttf",
            imageAsset: "assets/logo.png");

        lvgl.Invoke(() =>
        {
            using var builder = new UiBuilder { AssetDirectory = _folder };
            builder.Build(document, lvgl.Application.Screen);

            Assert.Empty(builder.Diagnostics);
            Assert.Equal(document.Descendants().Count(), builder.WidgetsByNode.Count);
            Assert.Equal(2, builder.Styles.Count);

            // Tabs and tiles come out as their real page types, parented by their hosts.
            var tabs = builder.Find<LvTabView>("Tabs")!;
            Assert.Equal(3, tabs.TabCount);
            Assert.IsType<LvTabPage>(builder.NamedWidgets["DevicesTab"]);
            Assert.IsType<LvTile>(builder.NamedWidgets["MoreTile"]);

            // The keyboard was declared before its field and still got wired.
            var keys = builder.Find<LvKeyboard>("Keys")!;
            Assert.Same(builder.Find<LvTextArea>("NameField"), keys.TextArea);

            Assert.Equal(215, builder.Find<LvSpinbox>("SetPoint")!.Value);
            Assert.Equal(3, builder.Find<LvTable>("Readings")!.RowCount);
            Assert.Equal(200, builder.Find<LvLed>("Status")!.Brightness);

            var logo = builder.Find<LvImage>("Logo")!;
            lvgl.Application.RunFrame();
            Assert.Equal(24, logo.SourceWidth);

            // Widgets first, then the builder's styles and fonts: the documented order.
            lvgl.Application.Screen.Clear();
        });

        Assert.True(lvgl.RenderFrame());
    }

    [Fact]
    public void A_missing_asset_is_a_diagnostic_not_a_crash()
    {
        if (lvgl.ShouldSkip()) return;
        lvgl.ResetScreen();

        var document = new UiDocument
        {
            Children =
            [
                new UiNode { Type = UiWidgetType.Image, Name = "Logo", Source = "assets/gone.png" },
                new UiNode { Type = UiWidgetType.Label, Name = "Caption", Text = "x", Font = "assets/gone.ttf" },
            ],
        };

        lvgl.Invoke(() =>
        {
            using var builder = new UiBuilder { AssetDirectory = _folder };
            builder.Build(document, lvgl.Application.Screen);

            Assert.Equal(2, builder.Diagnostics.Count);
            Assert.True(builder.Find<LvImage>("Logo")!.IsAlive);
            lvgl.Application.Screen.Clear();
        });
    }

    [Fact]
    public void A_shared_style_reaches_the_screen()
    {
        if (lvgl.ShouldSkip()) return;
        lvgl.ResetScreen();

        var document = new UiDocument
        {
            Styles = [new UiStyle { Name = "Red", BackgroundColor = "#FF0000", Radius = 0, BorderWidth = 0 }],
            Children =
            [
                new UiNode { Type = UiWidgetType.Panel, X = 20, Y = 20, Width = 80, Height = 60, Styles = ["Red"] },
                // A node's own property wins over its style.
                new UiNode { Type = UiWidgetType.Panel, X = 150, Y = 20, Width = 80, Height = 60, Styles = ["Red"], BackgroundColor = "#0000FF" },
            ],
        };

        UiBuilder builder = null!;
        lvgl.Invoke(() =>
        {
            builder = new UiBuilder();
            builder.Build(document, lvgl.Application.Screen);
        });

        Assert.True(lvgl.RenderFrame());
        Assert.Equal(0xFF0000u, lvgl.PixelAt(60, 50));
        Assert.Equal(0x0000FFu, lvgl.PixelAt(190, 50));

        lvgl.ResetScreen();
        lvgl.Invoke(builder.Dispose);
    }

    [Fact]
    public void A_builder_refuses_to_build_twice()
    {
        if (lvgl.ShouldSkip()) return;
        lvgl.ResetScreen();

        lvgl.Invoke(() =>
        {
            using var builder = new UiBuilder();
            builder.Build(new UiDocument(), lvgl.Application.Screen);
            Assert.Throws<InvalidOperationException>(() => builder.Build(new UiDocument(), lvgl.Application.Screen));
        });
    }
}
