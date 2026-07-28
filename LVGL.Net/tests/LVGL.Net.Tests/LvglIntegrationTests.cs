using Lvgl.Drawing;
using Lvgl.Interop;
using Lvgl.Styling;
using Lvgl.Ui;
using Lvgl.Widgets;

namespace Lvgl.Tests;

/// <summary>
/// End-to-end tests against a real LVGL build.
/// </summary>
/// <remarks>
/// These are the tests that justify the interop design: they exercise the pieces that cannot be
/// verified by reading the C header - the colour ABI workaround, the coordinate macro
/// reimplementation, event-code translation, and the callback plumbing in both directions.
/// They no-op when the native library has not been built.
/// </remarks>
[Collection(LvglCollection.Name)]
public class LvglIntegrationTests(LvglFixture lvgl)
{
    private bool Skip => !lvgl.IsAvailable;

    [Fact]
    public void The_loaded_library_matches_what_the_wrapper_expects()
    {
        if (Skip) return;

        lvgl.Invoke(() =>
        {
            Assert.True(LvglRuntime.IsInitialized);
            Assert.True(LvglRuntime.LvglVersion >= LvglRuntime.MinimumLvglVersion);
            Assert.Equal(32u, LvglRuntime.ColorDepth);
        });
    }

    [Fact]
    public void Every_stable_event_code_resolves_in_this_build()
    {
        if (Skip) return;

        // The whole point of routing event codes through the shim: if LVGL renames or drops one,
        // this fails loudly instead of silently subscribing to the wrong event.
        lvgl.Invoke(() =>
        {
            foreach (var code in Enum.GetValues<LvEventCode>())
            {
                var native = LvglNative.lvn_event_code((int)code);
                Assert.True(native >= 0, $"{code} is not supported by this LVGL build.");
            }
        });
    }

    [Fact]
    public void Widgets_are_created_and_read_back()
    {
        if (Skip) return;
        lvgl.ResetScreen();

        lvgl.Invoke(() =>
        {
            var label = new LvLabel(lvgl.Application.Screen, "hello");
            Assert.Equal("hello", label.Text);

            label.Text = "changed";
            Assert.Equal("changed", label.Text);

            var slider = new LvSlider(lvgl.Application.Screen);
            slider.SetRange(0, 200);
            slider.SetValue(75, animate: false);
            Assert.Equal(75, slider.Value);

            var textArea = new LvTextArea(lvgl.Application.Screen) { Text = "typed" };
            Assert.Equal("typed", textArea.Text);
        });
    }

    [Fact]
    public void Rendering_produces_pixels()
    {
        if (Skip) return;
        lvgl.ResetScreen();

        lvgl.Invoke(() => new LvLabel(lvgl.Application.Screen, "render me").Center());

        Assert.True(lvgl.RenderFrame(), "LVGL never flushed a frame.");
        Assert.True(lvgl.Application.Display.FlushCount > 0);
    }

    [Fact]
    public void A_background_colour_survives_the_round_trip_to_the_screen()
    {
        if (Skip) return;
        lvgl.ResetScreen();

        // This is the end-to-end check on the lv_color_t ABI workaround. lv_color_t is a 3-byte
        // struct whose by-value convention differs between Windows x64 and SysV, so colours go
        // through the shim as packed 0xRRGGBB. If that were wrong, the channels would come back
        // swapped or garbled here rather than failing at the call site.
        lvgl.Invoke(() =>
        {
            var panel = new LvPanel(lvgl.Application.Screen, 200, 150);
            panel.SetPosition(50, 50);
            panel.SetBackgroundColor(LvColor.FromRgb(0xFF0000u));
            panel.SetBorderWidth(0);
            panel.SetRadius(0);
        });

        Assert.True(lvgl.RenderFrame());
        Assert.Equal(0xFF0000u, lvgl.PixelAt(120, 120));
    }

    [Theory]
    [InlineData(0x00FF00u)]
    [InlineData(0x0000FFu)]
    [InlineData(0x38BDF8u)]
    [InlineData(0xFFFFFFu)]
    public void Arbitrary_colours_render_exactly(uint rgb)
    {
        if (Skip) return;
        lvgl.ResetScreen();

        lvgl.Invoke(() =>
        {
            var panel = new LvPanel(lvgl.Application.Screen, 200, 150);
            panel.SetPosition(50, 50);
            panel.SetBackgroundColor(LvColor.FromRgb(rgb));
            panel.SetBorderWidth(0);
            panel.SetRadius(0);
        });

        Assert.True(lvgl.RenderFrame());
        Assert.Equal(rgb, lvgl.PixelAt(120, 120));
    }

    [Fact]
    public void The_managed_coordinate_encoding_agrees_with_LVGL()
    {
        if (Skip) return;

        // LvCoord reimplements the LV_PCT and LV_SIZE_CONTENT macros, which cannot be called
        // through P/Invoke. This compares every value it produces against LVGL's own lv_pct and
        // the real LV_SIZE_CONTENT, so an upstream change fails here rather than quietly
        // misplacing widgets. v9 already changed both from v8 once.
        lvgl.Invoke(() =>
        {
            Assert.Equal(LvCoord.SizeContent, LvglNative.lvn_size_content());
            Assert.Equal(LvCoord.CoordMax, LvglNative.lvn_coord_max());

            foreach (var percent in (int[])[0, 1, 25, 50, 100, 200, 1000, -1, -25, -50, -100])
            {
                Assert.Equal(LvCoord.Percent(percent), LvglNative.lv_pct(percent));
            }
        });
    }

    [Fact]
    public void Percentage_coordinates_are_interpreted_by_LVGL_as_intended()
    {
        if (Skip) return;
        lvgl.ResetScreen();

        lvgl.Invoke(() =>
        {
            var parent = new LvPanel(lvgl.Application.Screen, 200, 100);
            parent.SetPadding(0);
            parent.SetBorderWidth(0);

            var child = new LvPanel(parent);
            child.SetSize(LvCoord.Percent(50), LvCoord.Percent(20));

            // Layout is otherwise resolved during the next refresh, so a freshly created widget
            // still reports 0.
            parent.UpdateLayout();

            Assert.Equal(100, child.Width);
            Assert.Equal(20, child.Height);
        });
    }

    [Fact]
    public void SizeContent_shrinks_a_container_to_its_child()
    {
        if (Skip) return;
        lvgl.ResetScreen();

        lvgl.Invoke(() =>
        {
            var box = new LvPanel(lvgl.Application.Screen);
            box.SetPadding(0);
            box.SetBorderWidth(0);
            box.SetSize(LvCoord.SizeContent, LvCoord.SizeContent);

            var child = new LvPanel(box);
            child.SetSize(80, 40);
            child.SetPosition(0, 0);

            box.UpdateLayout();

            Assert.Equal(80, box.Width);
            Assert.Equal(40, box.Height);
        });
    }

    [Fact]
    public void A_click_event_reaches_the_managed_handler()
    {
        if (Skip) return;
        lvgl.ResetScreen();

        var clicks = 0;
        LvButton button = null!;

        lvgl.Invoke(() =>
        {
            button = new LvButton(lvgl.Application.Screen, "tap");
            button.SetSize(160, 80);
            button.SetPosition(100, 100);
            button.Clicked += (_, _) => clicks++;
        });

        lvgl.RenderFrame();

        // LVGL samples the pointer during lv_timer_handler, so the press and release each need
        // frames to be seen.
        ClickAt(180, 140);

        Assert.True(clicks > 0, "The click never reached the managed handler.");
        Assert.True(button.IsAlive);
    }

    [Fact]
    public void Value_changed_fires_when_the_user_toggles_a_switch()
    {
        if (Skip) return;
        lvgl.ResetScreen();

        var changes = 0;
        LvSwitch toggle = null!;

        lvgl.Invoke(() =>
        {
            toggle = new LvSwitch(lvgl.Application.Screen);
            toggle.SetSize(120, 70);
            toggle.SetPosition(100, 100);
            toggle.ValueChanged += (_, _) => changes++;
        });

        lvgl.RenderFrame();
        ClickAt(160, 135);

        // Note this is driven by pointer input rather than by assigning IsOn: LVGL raises
        // LV_EVENT_VALUE_CHANGED for user interaction, not for a programmatic set.
        Assert.True(changes > 0, "The toggle never raised ValueChanged.");
        lvgl.Invoke(() => Assert.True(toggle.IsOn));
    }

    /// <summary>Presses and releases the pointer at a position, pumping LVGL either side.</summary>
    private void ClickAt(int x, int y) => lvgl.Invoke(() =>
    {
        lvgl.Backend.SetPointer(x, y, pressed: true);
        for (var i = 0; i < 5; i++) { lvgl.Application.RunFrame(); Thread.Sleep(10); }

        lvgl.Backend.SetPointer(x, y, pressed: false);
        for (var i = 0; i < 5; i++) { lvgl.Application.RunFrame(); Thread.Sleep(10); }
    });

    [Fact]
    public void Deleting_a_widget_marks_the_wrapper_dead()
    {
        if (Skip) return;
        lvgl.ResetScreen();

        lvgl.Invoke(() =>
        {
            var label = new LvLabel(lvgl.Application.Screen, "temporary");
            Assert.True(label.IsAlive);

            // Proves the delete event round-trips: the managed wrapper only learns the object is
            // gone because LVGL calls back into it.
            label.Delete();
            lvgl.Application.RunFrame();

            Assert.False(label.IsAlive);
            Assert.Throws<ObjectDisposedException>(() => label.Text);
        });
    }

    [Fact]
    public void Deleting_a_parent_kills_its_children()
    {
        if (Skip) return;
        lvgl.ResetScreen();

        lvgl.Invoke(() =>
        {
            var panel = new LvPanel(lvgl.Application.Screen, 100, 100);
            var child = new LvLabel(panel, "inside");

            panel.Delete();
            lvgl.Application.RunFrame();

            Assert.False(panel.IsAlive);
            Assert.False(child.IsAlive);
        });
    }

    [Fact]
    public void Charts_accept_series_and_samples()
    {
        if (Skip) return;
        lvgl.ResetScreen();

        lvgl.Invoke(() =>
        {
            var chart = new LvChart(lvgl.Application.Screen);
            chart.SetSize(300, 200);
            chart.Type = LvChartType.Line;
            chart.PointCount = 20;
            chart.UpdateMode = LvChartUpdateMode.Shift;
            chart.SetRange(LvChartAxis.PrimaryY, 0, 100);

            var series = chart.AddSeries(LvColor.Blue);
            Assert.NotEqual(0, series.Handle);
            Assert.Single(chart.Series);

            series.Fill(0);
            for (var i = 0; i < 50; i++) series.AddPoint(i * 2 % 100);

            chart.Refresh();
        });

        Assert.True(lvgl.RenderFrame());
    }

    [Fact]
    public void Shared_styles_apply_and_can_be_freed_after_the_widgets()
    {
        if (Skip) return;
        lvgl.ResetScreen();

        lvgl.Invoke(() =>
        {
            var style = new LvStyle().BackgroundColor(LvColor.FromRgb(0x00FF00u)).Radius(0).BorderWidth(0);

            var panel = new LvPanel(lvgl.Application.Screen, 200, 150);
            panel.SetPosition(50, 50);
            panel.AddStyle(style);

            for (var i = 0; i < 20; i++) { lvgl.Application.RunFrame(); Thread.Sleep(5); }

            Assert.Equal(0x00FF00u, lvgl.PixelAt(120, 120));

            // Order matters: the widget must go before the style it references.
            panel.Delete();
            lvgl.Application.RunFrame();
            style.Dispose();
        });
    }

    [Fact]
    public void Fonts_resolve_for_every_size_the_wrapper_offers()
    {
        if (Skip) return;

        lvgl.Invoke(() =>
        {
            foreach (var size in (int[])[12, 14, 16, 20, 24, 28, 36])
            {
                Assert.NotEqual(0, LvglNative.lvn_font_montserrat((uint)size));
            }
        });
    }

    [Fact]
    public void A_designer_document_builds_into_live_widgets()
    {
        if (Skip) return;
        lvgl.ResetScreen();

        var document = new UiDocument
        {
            Name = "Test",
            Width = LvglFixture.Width,
            Height = LvglFixture.Height,
            Children =
            [
                new UiNode
                {
                    Type = UiWidgetType.Panel,
                    Name = "Card",
                    X = 50,
                    Y = 50,
                    Width = 200,
                    Height = 150,
                    BackgroundColor = "#0000FF",
                    BorderWidth = 0,
                    Radius = 0,
                    Children =
                    [
                        new UiNode { Type = UiWidgetType.Label, Name = "Caption", Text = "from json" },
                    ],
                },
            ],
        };

        lvgl.Invoke(() =>
        {
            var builder = new UiBuilder();
            builder.Build(document, lvgl.Application.Screen);

            Assert.NotNull(builder.Find<LvPanel>("Card"));
            Assert.Equal("from json", builder.Find<LvLabel>("Caption")!.Text);
        });

        Assert.True(lvgl.RenderFrame());
        Assert.Equal(0x0000FFu, lvgl.PixelAt(120, 120));
    }

    [Fact]
    public void Work_posted_from_another_thread_runs_on_the_LVGL_thread()
    {
        if (Skip) return;
        lvgl.ResetScreen();

        LvLabel label = null!;
        lvgl.Invoke(() => label = new LvLabel(lvgl.Application.Screen, "before"));

        // Posted from the xUnit thread, which does not own LVGL.
        lvgl.Application.Post(() => label.Text = "after");

        lvgl.Invoke(() => lvgl.Application.RunFrame());

        lvgl.Invoke(() => Assert.Equal("after", label.Text));
    }

    [Fact]
    public void Touching_a_widget_from_the_wrong_thread_is_rejected()
    {
        if (Skip) return;
        lvgl.ResetScreen();

        LvLabel label = null!;
        lvgl.Invoke(() => label = new LvLabel(lvgl.Application.Screen, "guarded"));

        // Not marshalled: this runs on the xUnit thread and must be refused rather than silently
        // corrupting LVGL's unsynchronised global state.
        Assert.Throws<LvglThreadAccessException>(() => lvgl.Application.RunFrame());
    }
}
