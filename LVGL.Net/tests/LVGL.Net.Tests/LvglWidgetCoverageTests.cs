using Lvgl.Drawing;
using Lvgl.Interop;
using Lvgl.Widgets;

namespace Lvgl.Tests;

/// <summary>
/// Integration tests for the 0.2 widget set, images, fonts, animations and timers, against a real
/// LVGL build. They no-op without the native library unless <c>LVGLNET_REQUIRE_NATIVE</c> is set.
/// </summary>
[Collection(LvglCollection.Name)]
public sealed class LvglWidgetCoverageTests(LvglFixture lvgl) : IDisposable
{
    private readonly string _scratch = Directory.CreateTempSubdirectory("lvglnet-tests-").FullName;

    private bool Skip => lvgl.ShouldSkip();

    private LvScreen Screen => lvgl.Application.Screen;

    public void Dispose()
    {
        try { Directory.Delete(_scratch, recursive: true); }
        catch (IOException) { /* a decoder may still hold the file on Windows; temp gets cleaned anyway */ }
    }

    #region Images

    [Fact]
    public void An_image_from_managed_pixels_renders_those_pixels()
    {
        if (Skip) return;
        lvgl.ResetScreen();

        LvImageSource source = null!;
        lvgl.Invoke(() =>
        {
            source = LvImageSource.Solid(40, 30, LvColor.FromRgb(0x22CC66u));
            var image = new LvImage(Screen, source);
            image.SetPosition(20, 20);
        });

        Assert.True(lvgl.RenderFrame());
        Assert.Equal(0x22CC66u, lvgl.PixelAt(35, 30));
        Assert.Equal(0x000000u, lvgl.PixelAt(100, 100));

        // Widgets first, then the pixels they point at - the documented teardown order.
        lvgl.ResetScreen();
        lvgl.Invoke(source.Dispose);
    }

    [Fact]
    public void Translucent_pixels_blend_with_what_is_behind_them()
    {
        if (Skip) return;
        lvgl.ResetScreen();

        LvImageSource source = null!;
        lvgl.Invoke(() =>
        {
            source = LvImageSource.Solid(40, 40, LvColor.FromRgb(0xFF0000u), alpha: 128);
            new LvImage(Screen, source).SetPosition(10, 10);
        });

        Assert.True(lvgl.RenderFrame());

        // Half-transparent red over black: roughly half intensity, and certainly not opaque.
        var pixel = lvgl.PixelAt(30, 30);
        var red = (pixel >> 16) & 0xFF;
        Assert.InRange(red, 100u, 156u);

        lvgl.ResetScreen();
        lvgl.Invoke(source.Dispose);
    }

    [Fact]
    public void A_png_file_is_decoded_by_the_native_library()
    {
        if (Skip) return;
        lvgl.ResetScreen();

        var path = TestImages.WritePng(Path.Combine(_scratch, "green.png"), 32, 24, 0x10B981u);
        LvImage image = null!;

        lvgl.Invoke(() =>
        {
            image = new LvImage(Screen);
            image.SetFile(path);
            image.SetPosition(40, 40);
        });

        Assert.True(lvgl.RenderFrame());
        lvgl.Invoke(() =>
        {
            Assert.Equal(32, image.SourceWidth);
            Assert.Equal(24, image.SourceHeight);
        });
        Assert.Equal(0x10B981u, lvgl.PixelAt(50, 50));
    }

    [Fact]
    public void A_bmp_file_is_decoded_by_the_native_library()
    {
        if (Skip) return;
        lvgl.ResetScreen();

        var path = TestImages.WriteBmp(Path.Combine(_scratch, "blue.bmp"), 20, 20, 0x2563EBu);
        lvgl.Invoke(() => new LvImage(Screen) { Source = LvImageSource.FromFile(path) }.SetPosition(60, 60));

        Assert.True(lvgl.RenderFrame());
        Assert.Equal(0x2563EBu, lvgl.PixelAt(70, 70));
    }

    [Fact]
    public void A_screenshot_round_trips_through_LVGLs_own_png_decoder()
    {
        if (Skip) return;
        lvgl.ResetScreen();

        lvgl.Invoke(() =>
        {
            var panel = new LvPanel(Screen, 60, 40);
            panel.SetPosition(10, 10);
            panel.SetBackgroundColor(LvColor.FromRgb(0xE11D48u));
            panel.SetBorderWidth(0);
            panel.SetRadius(0);
        });
        Assert.True(lvgl.RenderFrame());

        // Write what LVGL drew, then have LVGL decode that file and draw it elsewhere: the
        // encoder is right if the colour survives both trips.
        var shot = Path.Combine(_scratch, "shot.png");
        lvgl.Invoke(() => lvgl.Backend.SavePng(shot));
        lvgl.ResetScreen();

        lvgl.Invoke(() => new LvImage(Screen) { Source = LvImageSource.FromFile(shot) }.SetPosition(0, 0));
        Assert.True(lvgl.RenderFrame());

        Assert.Equal(0xE11D48u, lvgl.PixelAt(30, 25));
    }

    [Fact]
    public void A_missing_image_file_is_rejected_before_LVGL_sees_it()
    {
        Assert.Throws<FileNotFoundException>(() => LvImageSource.FromFile(Path.Combine(_scratch, "nope.png")));
    }

    [Fact]
    public void Paths_handed_to_LVGL_carry_the_drive_letter()
    {
        var lvglPath = LvFileSystem.ToLvglPath(Path.Combine(_scratch, "x.png"));

        Assert.StartsWith("A:", lvglPath, StringComparison.Ordinal);
        Assert.DoesNotContain('\\', lvglPath);
    }

    [Fact]
    public void A_symbol_image_and_transforms_are_accepted()
    {
        if (Skip) return;
        lvgl.ResetScreen();

        lvgl.Invoke(() =>
        {
            var image = new LvImage(Screen);
            image.SetSymbol(LvSymbols.Ok);
            image.SetTextColor(LvColor.White);

            var pixels = LvImageSource.Solid(16, 16, LvColor.White);
            var rotated = new LvImage(Screen, pixels) { Rotation = 450, Scale = 512 };
            Assert.Equal(450, rotated.Rotation);
            Assert.Equal(512, rotated.Scale);

            rotated.Delete();
            pixels.Dispose();
        });

        Assert.True(lvgl.RenderFrame());
    }

    #endregion

    #region Containers and navigation

    [Fact]
    public void A_tab_view_hosts_a_page_per_tab()
    {
        if (Skip) return;
        lvgl.ResetScreen();

        lvgl.Invoke(() =>
        {
            var tabs = new LvTabView(Screen);
            tabs.SetSize(LvglFixture.Width, LvglFixture.Height);

            var first = tabs.AddTab("Overview");
            var second = tabs.AddTab("Network");
            tabs.AddTab("About");

            Assert.Equal(3, tabs.TabCount);
            Assert.Same(second, tabs.Tabs[1]);
            Assert.Equal(1, second.Index);

            var label = new LvLabel(second, "on page two");
            Assert.Same(second, label.Parent);

            tabs.ActiveTab = 2;
            Assert.Equal(2, tabs.ActiveTab);

            second.Activate();
            Assert.Equal(1, tabs.ActiveTab);

            tabs.SetTabTitle(0, "Home");
            tabs.TabBarPosition = LvDir.Left;
            Assert.Throws<ArgumentOutOfRangeException>(() => tabs.TabBarPosition = LvDir.All);
            Assert.Throws<ArgumentOutOfRangeException>(() => tabs.SetActiveTab(3, animate: false));
            Assert.True(first.IsAlive);
        });

        Assert.True(lvgl.RenderFrame());
    }

    [Fact]
    public void A_list_button_raises_clicked_and_keeps_its_text()
    {
        if (Skip) return;
        lvgl.ResetScreen();

        var clicks = 0;
        LvListButton wifi = null!;

        lvgl.Invoke(() =>
        {
            var list = new LvList(Screen);
            list.SetSize(240, 200);
            list.SetPosition(0, 0);

            list.AddText("Connectivity");
            wifi = list.AddButton("Wi-Fi", LvSymbols.Wifi);
            list.AddButton("Bluetooth", LvSymbols.Bluetooth);

            Assert.Equal("Wi-Fi", wifi.Text);
            wifi.Text = "Wireless";
            Assert.Equal("Wireless", wifi.Text);

            wifi.Clicked += (_, _) => clicks++;
            list.UpdateLayout();
        });

        lvgl.RenderFrame();

        var bounds = default(LvArea);
        lvgl.Invoke(() => bounds = wifi.GetBounds());
        lvgl.ClickAt(bounds.X1 + bounds.Width / 2, bounds.Y1 + bounds.Height / 2);

        Assert.True(clicks > 0, "The list button never raised Clicked.");
    }

    [Fact]
    public void A_modal_message_box_closes_and_deletes_itself()
    {
        if (Skip) return;
        lvgl.ResetScreen();

        lvgl.Invoke(() =>
        {
            var box = LvMessageBox.Show("Delete profile", "This cannot be undone.", "Cancel", "Delete");
            Assert.Equal(2, box.Buttons.Count);
            Assert.All(box.Buttons, button => Assert.True(button.IsAlive));

            box.Close();
            lvgl.Application.RunFrame();

            Assert.False(box.IsAlive);
            box.Close(); // a second close is harmless
        });
    }

    [Fact]
    public void A_tile_view_moves_between_tiles()
    {
        if (Skip) return;
        lvgl.ResetScreen();

        lvgl.Invoke(() =>
        {
            var view = new LvTileView(Screen);
            view.SetSize(LvglFixture.Width, LvglFixture.Height);

            var home = view.AddTile(0, 0, LvDir.Right);
            var detail = view.AddTile(1, 0, LvDir.Left);
            new LvLabel(detail, "details");

            view.ShowTile(0, 0);
            Assert.Same(home, view.ActiveTile);

            view.ShowTile(1, 0);
            Assert.Same(detail, view.ActiveTile);
            Assert.Equal(1, detail.Column);
        });
    }

    [Fact]
    public void A_keyboard_types_into_its_text_area()
    {
        if (Skip) return;
        lvgl.ResetScreen();

        lvgl.Invoke(() =>
        {
            var field = new LvTextArea(Screen) { IsSingleLine = true };
            var keyboard = new LvKeyboard(Screen) { TextArea = field, Mode = LvKeyboardMode.Number, ShowPopovers = true };

            Assert.Same(field, keyboard.TextArea);
            Assert.Equal(field.Handle, LvglNative.lv_keyboard_get_textarea(keyboard.Handle));
            Assert.Equal(LvKeyboardMode.Number, keyboard.Mode);

            field.Delete();
            lvgl.Application.RunFrame();
            Assert.Null(keyboard.TextArea);
        });
    }

    #endregion

    #region Indicators

    [Fact]
    public void Indicator_widgets_hold_their_settings()
    {
        if (Skip) return;
        lvgl.ResetScreen();

        lvgl.Invoke(() =>
        {
            var spinner = new LvSpinner(Screen);
            spinner.SetAnimation(1200, 75);
            Assert.Equal(1200, spinner.Duration);
            Assert.Equal(75, spinner.ArcSweep);

            var led = new LvLed(Screen) { Color = LvColor.FromRgb(0xF59E0Bu), Brightness = 200 };
            Assert.Equal(200, led.Brightness);
            Assert.Equal(0xF59E0Bu, led.Color.Rgb);
            led.Off();
            Assert.True(led.Brightness < 200);
            led.On();
            Assert.Equal(255, led.Brightness);

            var scale = new LvScale(Screen) { Mode = LvScaleMode.RoundInner, TotalTickCount = 21, MajorTickEvery = 5, ShowLabels = true, AngleRange = 270, Rotation = 135 };
            scale.SetRange(0, 100);
            Assert.Equal(LvScaleMode.RoundInner, scale.Mode);
            Assert.Equal(21, scale.TotalTickCount);
            Assert.Equal(5, scale.MajorTickEvery);
            Assert.Equal(0, scale.Minimum);
            Assert.Equal(100, scale.Maximum);
        });

        Assert.True(lvgl.RenderFrame());
    }

    [Fact]
    public void A_table_sizes_itself_to_its_rows()
    {
        if (Skip) return;
        lvgl.ResetScreen();

        lvgl.Invoke(() =>
        {
            var table = new LvTable(Screen);
            table.SetRows([["Sensor", "Value"], ["CPU", "48 °C"], ["GPU", "51 °C", "warm"]]);

            Assert.Equal(3, table.RowCount);
            Assert.Equal(3, table.ColumnCount);
            Assert.Equal("48 °C", table[1, 1]);
            Assert.Equal("warm", table[2, 2]);

            table[0, 2] = "Note";
            Assert.Equal("Note", table[0, 2]);

            table.SetColumnWidth(0, 120);
            Assert.Equal(120, table.GetColumnWidth(0));

            Assert.Throws<ArgumentOutOfRangeException>(() => table[5, 0]);
        });
    }

    [Fact]
    public void A_spinbox_steps_within_its_range()
    {
        if (Skip) return;
        lvgl.ResetScreen();

        lvgl.Invoke(() =>
        {
            var spinbox = new LvSpinbox(Screen);
            spinbox.SetRange(0, 2000);
            spinbox.SetDigitFormat(4, decimalPlaces: 1);
            spinbox.Value = 1995;
            spinbox.Step = 10;

            spinbox.Increment();
            Assert.Equal(2000, spinbox.Value);   // clamped, not 2005

            spinbox.Decrement();
            Assert.Equal(1990, spinbox.Value);

            Assert.Throws<ArgumentOutOfRangeException>(() => spinbox.SetDigitFormat(3, decimalPlaces: 3));
        });
    }

    #endregion

    #region Geometry

    [Fact]
    public void Bounds_are_absolute_and_include_the_parents_padding()
    {
        if (Skip) return;
        lvgl.ResetScreen();

        lvgl.Invoke(() =>
        {
            var panel = new LvPanel(Screen, 200, 150);
            panel.SetPosition(50, 40);
            panel.SetPadding(10);
            panel.SetBorderWidth(0);

            var child = new LvPanel(panel, 30, 20);
            child.SetPosition(5, 6);

            Screen.UpdateLayout();

            var outer = panel.GetBounds();
            Assert.Equal(new LvArea(50, 40, 249, 189).ToString(), outer.ToString());

            // Relative X/Y are measured from the content area, so padding shifts the child.
            var inner = child.GetBounds();
            Assert.Equal(50 + 10 + 5, inner.X1);
            Assert.Equal(40 + 10 + 6, inner.Y1);
            Assert.Equal(30, inner.Width);
        });
    }

    [Fact]
    public void ZIndex_reorders_siblings()
    {
        if (Skip) return;
        lvgl.ResetScreen();

        lvgl.Invoke(() =>
        {
            var back = new LvPanel(Screen, 10, 10);
            var front = new LvPanel(Screen, 10, 10);

            Assert.True(front.ZIndex > back.ZIndex);

            front.ZIndex = 0;
            Assert.Equal(0, front.ZIndex);
            Assert.Equal(1, back.ZIndex);
        });
    }

    #endregion

    #region Fonts

    [Fact]
    public void A_truetype_font_loads_from_a_file_and_resizes()
    {
        if (Skip) return;
        var path = TestImages.FindTrueTypeFont();
        if (path is null) return;

        lvgl.ResetScreen();

        LvFont font = null!;
        lvgl.Invoke(() =>
        {
            font = LvFont.FromFile(path, 20);
            Assert.True(font.IsTrueType);

            var label = new LvLabel(Screen, "TrueType Ag");
            label.SetFont(font);
            label.UpdateLayout();
            var small = label.Height;

            font.Size = 40;
            label.Text = "TrueType Ag";   // re-measure with the new size
            label.UpdateLayout();
            Assert.True(label.Height > small, $"Label did not grow with the font: {small} -> {label.Height}.");
        });

        Assert.True(lvgl.RenderFrame());

        lvgl.ResetScreen();
        lvgl.Invoke(font.Dispose);
    }

    [Fact]
    public void A_truetype_font_loads_from_memory()
    {
        if (Skip) return;
        var path = TestImages.FindTrueTypeFont();
        if (path is null) return;

        var bytes = File.ReadAllBytes(path);
        lvgl.ResetScreen();

        lvgl.Invoke(() =>
        {
            using var font = LvFont.FromData(bytes, 18);
            var label = new LvLabel(Screen, "from memory");
            label.SetFont(font);
            label.UpdateLayout();
            Assert.True(label.Height > 0);
            label.Delete();
            lvgl.Application.RunFrame();
        });
    }

    [Fact]
    public void Built_in_fonts_are_not_resizable_and_dispose_is_a_no_op()
    {
        if (Skip) return;

        lvgl.Invoke(() =>
        {
            var font = LvFont.Montserrat(20);
            Assert.False(font.IsTrueType);
            Assert.Throws<InvalidOperationException>(() => font.Size = 30);
            font.Dispose();
        });
    }

    #endregion

    #region Animations

    [Fact]
    public void An_animation_reaches_its_end_value_and_completes()
    {
        if (Skip) return;
        lvgl.ResetScreen();

        LvPanel panel = null!;
        LvAnimation animation = null!;
        var completed = 0;

        lvgl.Invoke(() =>
        {
            panel = new LvPanel(Screen, 20, 20);
            animation = new LvAnimation(panel, x => panel.X = x, 0, 120) { Duration = 120, Path = LvAnimPath.EaseOut };
            animation.Completed += (_, _) => completed++;
            animation.Start();
            Assert.True(animation.IsRunning);
        });

        lvgl.Pump(400);

        lvgl.Invoke(() =>
        {
            Assert.Equal(120, panel.X);
            Assert.False(animation.IsRunning);
        });
        Assert.Equal(1, completed);
    }

    [Fact]
    public void Deleting_the_target_stops_its_animations()
    {
        if (Skip) return;
        lvgl.ResetScreen();

        var failures = 0;
        void OnFailure(object? sender, LvglCallbackFailedEventArgs e) => failures++;
        LvglRuntime.CallbackFailed += OnFailure;

        try
        {
            LvAnimation animation = null!;
            lvgl.Invoke(() =>
            {
                var panel = new LvPanel(Screen, 20, 20);
                animation = new LvAnimation(panel, y => panel.Y = y, 0, 100) { Duration = 200, RepeatCount = LvAnimation.Infinite };
                animation.Start();

                panel.Delete();
            });

            lvgl.Pump(100);

            lvgl.Invoke(() => Assert.False(animation.IsRunning));
            Assert.Equal(0, failures);
        }
        finally
        {
            LvglRuntime.CallbackFailed -= OnFailure;
        }
    }

    [Fact]
    public void Stopping_one_animation_leaves_another_on_the_same_widget_running()
    {
        if (Skip) return;
        lvgl.ResetScreen();

        LvAnimation horizontal = null!;
        LvAnimation vertical = null!;
        LvPanel panel = null!;

        lvgl.Invoke(() =>
        {
            panel = new LvPanel(Screen, 20, 20);
            horizontal = new LvAnimation(panel, x => panel.X = x, 0, 100) { Duration = 1000 }.Start();
            vertical = new LvAnimation(panel, y => panel.Y = y, 0, 80) { Duration = 150 }.Start();

            horizontal.Stop();
            Assert.False(horizontal.IsRunning);
            Assert.True(vertical.IsRunning);
        });

        lvgl.Pump(400);

        lvgl.Invoke(() =>
        {
            Assert.Equal(80, panel.Y);
            Assert.True(panel.X < 100, "The stopped animation kept moving the widget.");
        });
    }

    [Fact]
    public void Helpers_animate_widget_properties()
    {
        if (Skip) return;
        lvgl.ResetScreen();

        LvBar bar = null!;
        LvPanel panel = null!;

        lvgl.Invoke(() =>
        {
            bar = new LvBar(Screen);
            bar.SetRange(0, 100);
            bar.AnimateValue(70, durationMs: 100);

            panel = new LvPanel(Screen, 10, 10);
            panel.AnimateWidth(60, durationMs: 100);
            panel.AnimateOpacity(255, 0, durationMs: 100);
        });

        lvgl.Pump(350);

        lvgl.Invoke(() =>
        {
            Assert.Equal(70, bar.Value);
            Assert.Equal(60, panel.Width);
        });
    }

    #endregion

    #region Allocation

    /// <summary>
    /// Principle 3, enforced: redrawing - timers, animations, layout, rendering, the flush into the
    /// backend, chart samples, value changes - allocates nothing on the managed heap per frame.
    /// </summary>
    [Fact]
    public void The_render_path_does_not_allocate()
    {
        if (Skip) return;
        lvgl.ResetScreen();

        lvgl.Invoke(() =>
        {
            var bar = new LvBar(Screen);
            bar.SetRange(0, 100);
            bar.SetSize(300, 20);

            var chart = new LvChart(Screen) { PointCount = 50, UpdateMode = LvChartUpdateMode.Shift };
            chart.SetPosition(0, 40);
            chart.SetSize(300, 120);
            var series = chart.AddSeries(LvColor.Blue);

            var panel = new LvPanel(Screen, 20, 20);
            var position = 0;
            var animation = new LvAnimation(panel, x => panel.X = x, 0, 200) { Duration = 400, RepeatCount = LvAnimation.Infinite };
            animation.Start();

            using var timer = new LvTimer(5, _ => position++);

            void Frames(int count)
            {
                for (var i = 0; i < count; i++)
                {
                    bar.SetValue(i % 100, animate: false);
                    series.AddPoint(i % 100);
                    lvgl.Application.RunFrame();

                    // Real time has to pass for LVGL to refresh (every 16 ms) and animations to
                    // advance; without it most iterations would draw nothing and prove nothing.
                    Thread.Sleep(2);
                }
            }

            // Warm up first: JIT, tiering and LVGL's first-use caches are not per-frame costs.
            Frames(60);

            var flushesBefore = lvgl.Application.Display.FlushCount;
            var before = GC.GetAllocatedBytesForCurrentThread();
            Frames(150);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            var flushes = lvgl.Application.Display.FlushCount - flushesBefore;

            animation.Stop();

            Assert.True(allocated == 0, $"150 redrawn frames allocated {allocated} bytes ({allocated / 150.0:0.0} per frame).");
            Assert.True(position > 0, "The timer never ticked, so the test did not exercise it.");
            Assert.True(flushes >= 50, $"Only {flushes} flushes: the loop did not actually redraw enough to measure.");
        });
    }

    #endregion

    #region Timers

    [Fact]
    public void A_timer_ticks_its_repeat_count_and_then_pauses()
    {
        if (Skip) return;

        LvTimer timer = null!;
        lvgl.Invoke(() => timer = new LvTimer(20, _ => { }, repeatCount: 3));

        lvgl.Pump(300);

        lvgl.Invoke(() =>
        {
            Assert.Equal(3, timer.TickCount);
            Assert.True(timer.IsPaused);

            timer.Restart(repeatCount: 1);
        });

        lvgl.Pump(120);

        lvgl.Invoke(() =>
        {
            Assert.Equal(4, timer.TickCount);
            timer.Dispose();
            Assert.False(timer.IsAlive);
            timer.Dispose(); // idempotent
        });
    }

    [Fact]
    public void An_exception_in_a_timer_is_reported_not_propagated()
    {
        if (Skip) return;

        LvglCallbackFailedEventArgs? reported = null;
        void OnFailure(object? sender, LvglCallbackFailedEventArgs e) => reported = e;
        LvglRuntime.CallbackFailed += OnFailure;

        try
        {
            LvTimer timer = null!;
            lvgl.Invoke(() => timer = new LvTimer(10, _ => throw new InvalidOperationException("boom"), repeatCount: 1));

            lvgl.Pump(100);
            lvgl.Invoke(timer.Dispose);

            Assert.NotNull(reported);
            Assert.Equal("timer", reported!.Source);
            Assert.IsType<InvalidOperationException>(reported.Exception);
        }
        finally
        {
            LvglRuntime.CallbackFailed -= OnFailure;
        }
    }

    [Fact]
    public void An_exception_in_an_event_handler_is_reported()
    {
        if (Skip) return;
        lvgl.ResetScreen();

        LvglCallbackFailedEventArgs? reported = null;
        void OnFailure(object? sender, LvglCallbackFailedEventArgs e) => reported = e;
        LvglRuntime.CallbackFailed += OnFailure;

        try
        {
            lvgl.Invoke(() =>
            {
                var button = new LvButton(Screen, "throws");
                button.SetSize(100, 60);
                button.SetPosition(100, 100);
                button.Clicked += (_, _) => throw new InvalidOperationException("handler failed");
            });

            lvgl.RenderFrame();
            lvgl.ClickAt(150, 130);

            Assert.NotNull(reported);
            Assert.Contains("Clicked", reported!.Source, StringComparison.Ordinal);
        }
        finally
        {
            LvglRuntime.CallbackFailed -= OnFailure;
        }
    }

    #endregion
}
