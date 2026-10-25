using Lvgl;
using Lvgl.Drawing;
using Lvgl.Widgets;
#if (UseJson)
using Lvgl.Ui;
#else
using Lvgl.Styling;
#endif

namespace Company.LvglTemplateApp;

#if (UseJson)
/// <summary>
/// The first screen. Its layout lives in <c>Screens/MainScreen.lvgl.json</c> - open that file in
/// the LVGL.Net designer to change it - and this class wires behaviour to the named widgets.
/// </summary>
internal sealed class MainScreen
{
    public const int DesignWidth = DESIGN_WIDTH;
    public const int DesignHeight = DESIGN_HEIGHT;

    private UiBuilder? _builder;
    private LvTimer? _clock;

    public void Build(LvObject root)
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "Screens");
        var document = UiJson.Load(Path.Combine(folder, "MainScreen.lvgl.json"));

        // The builder owns the layout's shared styles and fonts; it is disposed in Destroy.
        _builder = new UiBuilder { AssetDirectory = folder };
        _builder.Build(document, root);
        foreach (var problem in _builder.Diagnostics) Console.Error.WriteLine(problem);

        var level = _builder.Find<LvSlider>("LevelSlider")!;
        var bar = _builder.Find<LvBar>("LevelBar")!;
        var value = _builder.Find<LvLabel>("LevelValue")!;
        level.ValueChanged += (_, _) =>
        {
            bar.SetValue(level.Value, animate: true);
            value.Text = $"{level.Value}%";
        };

        var power = _builder.Find<LvSwitch>("PowerSwitch")!;
        var led = _builder.Find<LvLed>("PowerLed")!;
        power.ValueChanged += (_, _) => led.Brightness = power.IsOn ? (byte)255 : (byte)40;

        // A timer runs on the LVGL thread inside the loop, so it may touch widgets directly.
        var time = _builder.Find<LvLabel>("ClockLabel")!;
        _clock = new LvTimer(1000, _ => time.Text = DateTime.Now.ToString("HH:mm:ss"));
        _clock.FireSoon();
    }

    /// <summary>Frees the timer and the layout's styles. Call after the widgets are gone.</summary>
    public void Destroy()
    {
        _clock?.Dispose();
        _builder?.Dispose();
    }
}
#else
/// <summary>The first screen, built in C#.</summary>
internal sealed class MainScreen
{
    public const int DesignWidth = DESIGN_WIDTH;
    public const int DesignHeight = DESIGN_HEIGHT;

    private static readonly LvColor Background = LvColor.FromRgb(0x0F1720u);
    private static readonly LvColor Surface = LvColor.FromRgb(0x1A2430u);
    private static readonly LvColor Text = LvColor.FromRgb(0xE6EDF3u);
    private static readonly LvColor Muted = LvColor.FromRgb(0x8B9AAAu);
    private static readonly LvColor Accent = LvColor.FromRgb(0x38BDF8u);

    // A shared style: one property table for every card. It must outlive the widgets using it.
    private LvStyle? _card;
    private LvTimer? _clock;

    public void Build(LvObject root)
    {
        root.SetBackgroundColor(Background);

        _card = new LvStyle().BackgroundColor(Surface).Radius(12).BorderWidth(0).Padding(16);

        var title = new LvLabel(root, "Company.LvglTemplateApp");
        title.Align(LvAlign.TopLeft, 24, 20);
        title.SetTextColor(Text);
        title.SetFontSize(24);

        var time = new LvLabel(root, "--:--:--");
        time.Align(LvAlign.TopRight, -24, 26);
        time.SetTextColor(Muted);

        // Level card: a slider driving a bar and a readout.
        var levelCard = new LvPanel(root, 340, 150);
        levelCard.AddStyle(_card);
        levelCard.Align(LvAlign.LeftMid, 24, 10);

        var levelTitle = new LvLabel(levelCard, "Level");
        levelTitle.SetTextColor(Muted);

        var value = new LvLabel(levelCard, "40%");
        value.Align(LvAlign.TopRight);
        value.SetTextColor(Text);
        value.SetFontSize(20);

        var bar = new LvBar(levelCard);
        bar.SetSize(300, 12);
        bar.Align(LvAlign.TopLeft, 0, 44);
        bar.SetBackgroundColor(Accent, LvPart.Indicator);
        bar.SetValue(40, animate: false);

        var slider = new LvSlider(levelCard);
        slider.SetSize(300, 10);
        slider.Align(LvAlign.BottomLeft, 0, -10);
        slider.SetBackgroundColor(Accent, LvPart.Indicator);
        slider.SetBackgroundColor(Accent, LvPart.Knob);
        slider.SetValue(40, animate: false);
        slider.ValueChanged += (_, _) =>
        {
            bar.SetValue(slider.Value, animate: true);
            value.Text = $"{slider.Value}%";
        };

        // Power card: a switch and a status light.
        var powerCard = new LvPanel(root, 260, 150);
        powerCard.AddStyle(_card);
        powerCard.Align(LvAlign.RightMid, -24, 10);

        var powerTitle = new LvLabel(powerCard, "Power");
        powerTitle.SetTextColor(Muted);

        var led = new LvLed(powerCard) { Color = LvColor.FromRgb(0x4ADE80u), Brightness = 255 };
        led.SetSize(22, 22);
        led.Align(LvAlign.LeftMid, 4, 14);

        var power = new LvSwitch(powerCard) { IsOn = true };
        power.Align(LvAlign.RightMid, 0, 14);
        power.ValueChanged += (_, _) => led.Brightness = power.IsOn ? (byte)255 : (byte)40;

        // Slide the cards in. The animations stop by themselves if the cards are deleted.
        levelCard.AnimateOpacity(0, 255, durationMs: 500);
        powerCard.AnimateOpacity(0, 255, durationMs: 700);

        // A timer runs on the LVGL thread inside the loop, so it may touch widgets directly.
        _clock = new LvTimer(1000, _ => time.Text = DateTime.Now.ToString("HH:mm:ss"));
        _clock.FireSoon();
    }

    /// <summary>Frees the timer and the shared style. Call after the widgets are gone.</summary>
    public void Destroy()
    {
        _clock?.Dispose();
        _card?.Dispose();
    }
}
#endif
