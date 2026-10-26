using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lvgl.Assistant;
using Lvgl.Designer.Assistant;
using Lvgl.Designer.Views;
using Lvgl.Ui;

namespace Lvgl.Designer.Tests;

/// <summary>
/// Drives the real designer window with pointer and keyboard input on Avalonia's headless
/// platform, with LVGL rendering the preview underneath.
/// </summary>
/// <remarks>
/// Tests that need the LVGL preview skip when the native library is missing, unless
/// <c>LVGLNET_REQUIRE_NATIVE=1</c> - the same rule as the library's own integration tests.
/// </remarks>
public sealed class DesignerWindowTests : IDisposable
{
    private static readonly string? ScreenshotFolder = Environment.GetEnvironmentVariable("LVGLNET_SCREENSHOTS");
    private readonly string _assistantFolder = Directory.CreateTempSubdirectory("lvglnet-assistant-").FullName;

    public DesignerWindowTests()
    {
        // Keep the assistant's chats out of the user's real history.
        ChatPanel.OptionsFactory = () => new AssistantOptions
        {
            SessionDirectory = _assistantFolder,
            AttachmentDirectory = Path.Combine(_assistantFolder, "attachments"),
        };
    }

    public void Dispose()
    {
        try { Directory.Delete(_assistantFolder, recursive: true); }
        catch (IOException) { }
    }

    #region Harness

    private static MainWindow Open()
    {
        var window = new MainWindow { Width = 1480, Height = 900 };
        window.Show();
        Pump(120);
        return window;
    }

    /// <summary>Lets dispatcher timers (the preview's render tick, the rebuild debounce) run.</summary>
    private static void Pump(int milliseconds)
    {
        var clock = Stopwatch.StartNew();
        do
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }
        while (clock.ElapsedMilliseconds < milliseconds);

        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// Pumps until <paramref name="condition"/> holds. For results that arrive through the
    /// preview's rebuild timer, whose timing depends on how fast the machine is - a fixed wait
    /// that is enough on a desktop is not enough on a CI runner.
    /// </summary>
    private static bool PumpUntil(Func<bool> condition, int timeoutMs = 5000)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            if (clock.ElapsedMilliseconds > timeoutMs) return false;
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }

        return true;
    }

    private static bool NeedsPreview(MainWindow window)
    {
        // The first build reaches the preview through the rebuild timer after the window opens,
        // so wait for it to start or to say why it could not; a fixed wait lost this race on CI.
        PumpUntil(() => window.Preview.IsAvailable || window.Preview.UnavailableReason is not null);

        if (window.Preview.IsAvailable) return true;
        if (Environment.GetEnvironmentVariable("LVGLNET_REQUIRE_NATIVE") is "1" or "true")
        {
            // Everything that tells "never started" from "failed silently", for a CI-only failure.
            var status = window.FindControl<TextBlock>("StatusText")?.Text;
            var live = window.FindControl<TextBlock>("LiveText")?.Text;
            throw new InvalidOperationException(
                $"LVGLNET_REQUIRE_NATIVE is set but the preview is offline: '{window.Preview.UnavailableReason}'. " +
                $"Window visible: {window.IsVisible}; document: {window.Session.Document.Name} ({window.Session.Document.Children.Count} parts); " +
                $"bitmap: {window.Preview.Bitmap is not null}; LVGL initialised: {LvglRuntime.IsInitialized}; " +
                $"status: '{status}'; live: '{live}'.");
        }

        return false;
    }

    private static BoardCanvas Canvas(MainWindow window) => window.FindControl<BoardCanvas>("Canvas")!;

    /// <summary>Window coordinates of a device pixel on the board.</summary>
    private static Point OnBoard(MainWindow window, double x, double y)
    {
        var canvas = Canvas(window);
        return canvas.TranslatePoint(canvas.DeviceToCanvas(x, y), window)!.Value;
    }

    private static Point CenterOf(MainWindow window, string nodeName)
    {
        var bounds = window.Preview.GetBounds(window.Session.Document.Find(nodeName))!.Value;
        return OnBoard(window, bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
    }

    private static void Click(MainWindow window, Point point)
    {
        window.MouseMove(point, RawInputModifiers.None);
        window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
        Pump(40);
    }

    private static void Drag(MainWindow window, Point from, Vector by, int steps = 6)
    {
        window.MouseMove(from, RawInputModifiers.None);
        window.MouseDown(from, MouseButton.Left, RawInputModifiers.None);

        for (var i = 1; i <= steps; i++)
        {
            window.MouseMove(from + by * (i / (double)steps), RawInputModifiers.LeftMouseButton);
            Pump(10);
        }

        window.MouseUp(from + by, MouseButton.Left, RawInputModifiers.None);
        Pump(60);
    }

    private static void Key(MainWindow window, Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPress(key, modifiers, PhysicalKey.None, null);
        window.KeyRelease(key, modifiers, PhysicalKey.None, null);
        Pump(40);
    }

    private static void Screenshot(MainWindow window, string name)
    {
        if (string.IsNullOrEmpty(ScreenshotFolder)) return;

        Pump(60);
        Directory.CreateDirectory(ScreenshotFolder);
        window.CaptureRenderedFrame()?.Save(Path.Combine(ScreenshotFolder, name + ".png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    private static uint PixelAt(MainWindow window, Point point)
    {
        var frame = window.CaptureRenderedFrame()!;
        using var buffer = frame.Lock();
        unsafe
        {
            var row = (byte*)buffer.Address + (int)point.Y * buffer.RowBytes;
            var pixel = row + (int)point.X * 4;

            // The headless capture's byte order depends on the platform's preferred format.
            return buffer.Format == Avalonia.Platform.PixelFormat.Rgba8888
                ? ((uint)pixel[0] << 16) | ((uint)pixel[1] << 8) | pixel[2]
                : ((uint)pixel[2] << 16) | ((uint)pixel[1] << 8) | pixel[0];
        }
    }

    #endregion

    [AvaloniaFact]
    public void The_designer_opens_clean_with_the_preview_running()
    {
        var window = Open();

        Assert.False(window.Session.IsDirty, "Opening the designer must not count as an edit.");
        Assert.False(window.Session.CanUndo);

        if (NeedsPreview(window))
        {
            Assert.NotNull(window.Preview.Bitmap);
            Assert.Contains("9.", window.FindControl<TextBlock>("LiveText")!.Text, StringComparison.Ordinal);
        }

        Screenshot(window, "01-start");
        window.Close();
    }

    [AvaloniaFact]
    public void LVGL_pixels_reach_the_canvas()
    {
        var window = Open();
        if (!NeedsPreview(window)) return;

        // The starter button is #38BDF8; its centre on screen must be that colour, drawn by LVGL.
        var pixel = PixelAt(window, CenterOf(window, "ActionButton") + new Vector(-60, 0));
        Assert.Equal(0x38BDF8u, pixel);

        window.Close();
    }

    [AvaloniaFact]
    public void Clicking_a_widget_on_the_board_selects_it_and_empty_board_clears()
    {
        var window = Open();
        if (!NeedsPreview(window)) return;

        Click(window, CenterOf(window, "ActionButton"));
        Assert.Equal("ActionButton", window.Session.Selection?.Name);
        Screenshot(window, "02-selected");

        Click(window, OnBoard(window, 30, 400));
        Assert.Null(window.Session.Selection);

        window.Close();
    }

    [AvaloniaFact]
    public void Dragging_moves_on_the_grid_and_undoes_in_one_step()
    {
        var window = Open();
        if (!NeedsPreview(window)) return;

        var button = window.Session.Document.Find("ActionButton")!;
        var zoom = Canvas(window).Zoom;

        Drag(window, CenterOf(window, "ActionButton"), new Vector(43 * zoom, 21 * zoom));

        button = window.Session.Document.Find("ActionButton")!;
        Assert.True(button.X != 0 || button.Y != 0, "The drag did not move the widget.");

        // The left/top edge snaps to the 8 px via grid, wherever the widget started.
        var bounds = window.Preview.GetBounds(button)!.Value;
        Assert.Equal(0, bounds.X % 8);
        Assert.Equal(0, bounds.Y % 8);

        Key(window, Avalonia.Input.Key.Z, RawInputModifiers.Control);
        button = window.Session.Document.Find("ActionButton")!;
        Assert.Equal((0, 0), (button.X, button.Y));
        Assert.False(window.Session.CanUndo);

        window.Close();
    }

    [AvaloniaFact]
    public void Dragging_a_corner_pad_resizes_and_keeps_the_far_edge()
    {
        var window = Open();
        if (!NeedsPreview(window)) return;

        Click(window, CenterOf(window, "ActionButton"));
        var before = window.Preview.GetBounds(window.Session.Selection)!.Value;
        var zoom = Canvas(window).Zoom;

        Drag(window, OnBoard(window, before.Right, before.Bottom), new Vector(40 * zoom, 24 * zoom));

        var after = window.Preview.GetBounds(window.Session.Document.Find("ActionButton"))!.Value;

        Assert.True(after.Width > before.Width, $"Width {before.Width} -> {after.Width}");
        Assert.True(after.Height > before.Height, $"Height {before.Height} -> {after.Height}");

        // The button is centred; resizing must not let LVGL's re-centring drag the left edge along.
        Assert.Equal(before.X, after.X);
        Assert.Equal(before.Y, after.Y);
        Screenshot(window, "03-resized");

        window.Close();
    }

    [AvaloniaFact]
    public void Keyboard_nudges_duplicates_and_deletes()
    {
        var window = Open();
        if (!NeedsPreview(window)) return;

        Click(window, CenterOf(window, "ActionButton"));
        Canvas(window).Focus();

        Key(window, Avalonia.Input.Key.Right, RawInputModifiers.Shift);
        Assert.Equal(10, window.Session.Document.Find("ActionButton")!.X);

        Key(window, Avalonia.Input.Key.D, RawInputModifiers.Control);
        Assert.Equal(3, window.Session.Document.Descendants().Count());

        Key(window, Avalonia.Input.Key.Delete);
        Assert.Equal(2, window.Session.Document.Descendants().Count());

        window.Close();
    }

    [AvaloniaFact]
    public void A_part_click_adds_the_widget_and_the_outline_shows_its_designator()
    {
        var window = Open();

        var part = window.FindControl<StackPanel>("PartsList")!.Children.OfType<Button>()
            .First(b => b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Slider"));

        part.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Pump(80);

        var slider = window.Session.Document.Find("Slider1");
        Assert.NotNull(slider);
        Assert.Same(slider, window.Session.Selection);

        var outline = window.FindControl<OutlineView>("Outline")!;
        Assert.Contains(outline.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "SLD1");

        window.Close();
    }

    [AvaloniaFact]
    public void Typing_in_the_inspector_renames_and_undo_reverts_the_whole_word()
    {
        var window = Open();
        window.Session.Selection = window.Session.Document.Find("ActionButton");
        Pump(40);

        var nameBox = window.FindControl<InspectorPanel>("Inspector")!.GetVisualDescendants().OfType<TextBox>()
            .First(t => t.Text == "ActionButton");

        foreach (var text in new[] { "G", "Go", "GoB", "GoButton" })
        {
            nameBox.Text = text;
            Pump(10);
        }

        Assert.NotNull(window.Session.Document.Find("GoButton"));

        window.Session.Undo();
        Assert.NotNull(window.Session.Document.Find("ActionButton"));

        window.Close();
    }

    [AvaloniaFact]
    public void Code_mode_applies_edited_json_back_to_the_design()
    {
        var window = Open();

        window.FindControl<RadioButton>("CodeModeButton")!.IsChecked = true;
        Pump(60);

        var code = window.FindControl<CodePanel>("CodeSurface")!;
        Assert.True(code.IsVisible);
        Screenshot(window, "04-code-mode");

        code.Editor.Text = code.Editor.Text.Replace("\"MainScreen\"", "\"ThermostatScreen\"", StringComparison.Ordinal);
        code.Apply();
        Pump(40);

        Assert.Equal("ThermostatScreen", window.Session.Document.Name);
        Assert.True(window.Session.CanUndo);

        window.FindControl<RadioButton>("DesignModeButton")!.IsChecked = true;
        Pump(40);
        Assert.False(code.IsVisible);

        window.Close();
    }

    [AvaloniaFact]
    public void The_styles_page_creates_a_style_and_widgets_can_use_it()
    {
        var window = Open();
        var inspector = window.FindControl<InspectorPanel>("Inspector")!;

        inspector.ShowStyles(window.Session.AddStyle());
        Pump(40);
        Screenshot(window, "05-styles");

        var style = window.Session.Document.Styles.Single();
        window.Session.ToggleStyle(window.Session.Document.Find("ActionButton")!, style.Name, applied: true);
        Pump(60);

        Assert.Empty(window.Session.Document.Validate());
        Assert.Equal([style.Name], window.Session.Document.Find("ActionButton")!.Styles);

        window.Close();
    }

    [AvaloniaFact]
    public void The_assistant_panel_opens_and_reports_its_configuration()
    {
        var window = Open();

        Key(window, Avalonia.Input.Key.J, RawInputModifiers.Control);
        var panel = window.FindControl<ChatPanel>("Assistant")!;
        Assert.True(panel.IsVisible);

        // No keys in the test configuration: the panel says what to set rather than failing later.
        var texts = panel.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains(texts, t => t?.Contains("No API key", StringComparison.Ordinal) == true || t?.StartsWith("Ready", StringComparison.Ordinal) == true);
        Screenshot(window, "06-assistant");

        Key(window, Avalonia.Input.Key.J, RawInputModifiers.Control);
        Assert.False(panel.IsVisible);

        window.Close();
    }

    [AvaloniaFact]
    public void An_assistant_layout_lands_on_the_canvas_and_undoes()
    {
        var window = Open();

        var layout = new UiDocument
        {
            Name = "FromJack",
            Children = [new UiNode { Type = UiWidgetType.Led, Name = "Status", X = 40, Y = 40, AccentColor = "#22C55E" }],
        };

        // What ChatPanel raises when Jack's create_layout tool produces a document.
        window.FindControl<ChatPanel>("Assistant")!.GetType()
            .GetEvent(nameof(ChatPanel.LayoutProduced))!
            .GetRaiseMethod(nonPublic: true);
        typeof(MainWindow).GetMethod("OnAssistantLayout", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(window, [null, layout]);
        Pump(60);

        Assert.Equal("FromJack", window.Session.Document.Name);

        window.Session.Undo();
        Assert.Equal("MainScreen", window.Session.Document.Name);

        window.Close();
    }

    [AvaloniaFact]
    public void A_preset_resizes_the_screen()
    {
        var window = Open();
        var presets = window.FindControl<ComboBox>("PresetBox")!;

        presets.SelectedItem = presets.ItemsSource!.Cast<object>().OfType<Core.ScreenPreset>().First(p => p.Width == 480 && p.Height == 320);
        Pump(80);

        Assert.Equal((480, 320), (window.Session.Document.Width, window.Session.Document.Height));
        if (window.Preview.IsAvailable)
        {
            PumpUntil(() => window.Preview.Bitmap?.PixelSize.Width == 480);
            Assert.Equal(480, window.Preview.Bitmap!.PixelSize.Width);
        }
        Screenshot(window, "07-small-panel");

        window.Close();
    }
}
