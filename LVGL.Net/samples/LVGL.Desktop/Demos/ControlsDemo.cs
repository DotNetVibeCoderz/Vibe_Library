using Lvgl.Drawing;
using Lvgl.Widgets;

namespace Lvgl.Samples.Desktop.Demos;

/// <summary>
/// The 0.2 widget set in one place: tabs, a settings list, a gauge built from a scale and an arc,
/// status lights, a data table, a set-point spinbox, a keyboard - plus animations and a timer.
/// </summary>
internal sealed class ControlsDemo : DemoPage
{
    private LvTimer? _clock;
    private LvLabel? _time;
    private LvImageSource? _swatch;

    public override string Title => "Controls";

    public override string Description => "Tabs, lists, gauges, LEDs, tables, spinboxes, a keyboard - and animation";

    public override void Build(LvObject container, LvglApplication application)
    {
        var tabs = new LvTabView(container);
        tabs.SetSize(LvCoord.Percent(100), LvCoord.Percent(100));
        tabs.SetBackgroundColor(Theme.Background);
        tabs.TabBar.SetBackgroundColor(Theme.Surface);
        tabs.TabBar.SetTextColor(Theme.Text);

        BuildStatus(tabs.AddTab("Status"));
        BuildSettings(tabs.AddTab("Settings"));
        BuildData(tabs.AddTab("Data"));
        BuildInput(tabs.AddTab("Input"));

        // A once-a-second clock on LVGL's own timer: it runs in the loop, so it may touch widgets.
        _clock = new LvTimer(1000, _ =>
        {
            if (_time is { IsAlive: true }) _time.Text = DateTime.Now.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        });
        _clock.FireSoon();
    }

    private void BuildStatus(LvTabPage page)
    {
        page.SetBackgroundColor(Theme.Background);

        // Gauge: a round scale for the ticks, an arc on top for the value.
        var gauge = new LvScale(page) { Mode = LvScaleMode.RoundInner, TotalTickCount = 41, MajorTickEvery = 5, ShowLabels = true, AngleRange = 270, Rotation = 135 };
        gauge.SetRange(0, 100);
        gauge.SetSize(220, 220);
        gauge.SetPosition(10, 10);
        gauge.SetTextColor(Theme.TextMuted);
        gauge.SetLineColor(Theme.SurfaceAlt, LvPart.Items);

        var arc = new LvArc(page);
        arc.SetSize(170, 170);
        arc.AlignTo(gauge, LvAlign.Center);
        arc.SetRange(0, 100);
        arc.SetArcColor(Theme.AccentWarm, LvPart.Indicator);
        arc.SetArcWidth(10, LvPart.Indicator);
        arc.SetArcWidth(10);
        arc.SetBackgroundOpacity(0, LvPart.Knob);

        var reading = new LvLabel(page, "0");
        reading.AlignTo(gauge, LvAlign.Center);
        reading.SetFontSize(28);
        reading.SetTextColor(Theme.Text);

        // Sweep the needle forever; the animation stops by itself when the page is torn down.
        new LvAnimation(arc, value =>
        {
            arc.Value = value;
            reading.Text = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }, 15, 85)
        {
            Duration = 2400,
            ReverseDuration = 2400,
            RepeatCount = LvAnimation.Infinite,
            Path = LvAnimPath.EaseInOut,
        }.Start();

        string[] rooms = ["Living room", "Kitchen", "Bedroom"];
        LvColor[] colors = [Theme.Success, Theme.AccentWarm, Theme.Accent];

        for (var i = 0; i < rooms.Length; i++)
        {
            var row = new LvPanel(page, 300, 56);
            row.SetPosition(260, 14 + i * 68);
            row.SetBackgroundColor(Theme.Surface);
            row.SetBorderWidth(0);
            row.SetRadius(10);
            row.IsScrollable = false;

            var led = new LvLed(row) { Color = colors[i], Brightness = i == 2 ? (byte)60 : (byte)255 };
            led.SetSize(18, 18);
            led.Align(LvAlign.LeftMid, 6);

            var name = new LvLabel(row, rooms[i]);
            name.Align(LvAlign.LeftMid, 40);
            name.SetTextColor(Theme.Text);

            var power = new LvSwitch(row) { IsOn = i != 2 };
            power.Align(LvAlign.RightMid);
            power.ValueChanged += (_, _) => led.Brightness = power.IsOn ? (byte)255 : (byte)60;
        }

        var busy = new LvSpinner(page);
        busy.SetSize(40, 40);
        busy.SetPosition(260, 230);
        busy.SetArcColor(Theme.Accent, LvPart.Indicator);

        _time = new LvLabel(page, "--:--:--");
        _time.SetPosition(312, 240);
        _time.SetTextColor(Theme.TextMuted);
    }

    private static void BuildSettings(LvTabPage page)
    {
        page.SetBackgroundColor(Theme.Background);

        var list = new LvList(page);
        list.SetSize(320, LvCoord.Percent(100));
        list.SetBackgroundColor(Theme.Surface);
        list.SetBorderWidth(0);

        list.AddText("Connectivity");
        list.AddButton("Wi-Fi", LvSymbols.Wifi);
        list.AddButton("Bluetooth", LvSymbols.Bluetooth);
        list.AddText("Device");
        list.AddButton("Display", LvSymbols.Image);
        list.AddButton("Sound", LvSymbols.VolumeMax);
        list.AddButton("Power", LvSymbols.Power);

        var about = new LvButton(page, "About this device");
        about.SetPosition(340, 0);
        about.SetSize(200, 44);
        about.Clicked += (_, _) =>
        {
            var box = LvMessageBox.Show("LVGL.Net", $"{LvglAbout.Banner}\nLVGL {LvglRuntime.LvglVersion}", "Close");
            box.Buttons[0].Clicked += (_, _) => box.Close();
        };
    }

    private void BuildData(LvTabPage page)
    {
        page.SetBackgroundColor(Theme.Background);

        var table = new LvTable(page);
        table.SetRows(
        [
            ["Sensor", "Value", "Status"],
            ["CPU", "48 C", "OK"],
            ["GPU", "51 C", "OK"],
            ["Board", "39 C", "OK"],
            ["Fan", "2100 rpm", "Low"],
        ]);
        table.SetColumnWidth(0, 120);
        table.SetColumnWidth(1, 110);
        table.SetColumnWidth(2, 90);

        // A generated image: pixels made at run time, shown by LVGL without any file.
        _swatch = Gradient(160, 100);
        var image = new LvImage(page, _swatch);
        image.SetPosition(350, 0);
        image.SetRadius(10);

        var caption = new LvLabel(page, "Pixels generated in C#");
        caption.SetPosition(350, 110);
        caption.SetTextColor(Theme.TextMuted);
    }

    private static LvImageSource Gradient(int width, int height)
    {
        var pixels = new uint[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var color = Theme.Accent.Lerp(Theme.AccentWarm, x / (float)(width - 1)).Darken(y / (float)height * 0.5f);
                pixels[y * width + x] = 0xFF000000u | color.Rgb;
            }
        }

        return LvImageSource.FromArgb(width, height, pixels);
    }

    private static void BuildInput(LvTabPage page)
    {
        page.SetBackgroundColor(Theme.Background);

        var field = new LvTextArea(page) { IsSingleLine = true, PlaceholderText = "Device name" };
        field.SetSize(260, 44);
        field.SetPosition(0, 0);

        var setPoint = new LvSpinbox(page);
        setPoint.SetDigitFormat(3, decimalPlaces: 1);
        setPoint.SetRange(100, 300);
        setPoint.Value = 215;
        setPoint.SetSize(110, 44);
        setPoint.SetPosition(280, 0);

        var minus = new LvButton(page, "-");
        minus.SetSize(44, 44);
        minus.SetPosition(400, 0);
        minus.Clicked += (_, _) => setPoint.Decrement();

        var plus = new LvButton(page, "+");
        plus.SetSize(44, 44);
        plus.SetPosition(452, 0);
        plus.Clicked += (_, _) => setPoint.Increment();

        var keyboard = new LvKeyboard(page) { TextArea = field };
        keyboard.SetSize(LvCoord.Percent(100), 200);
        keyboard.Align(LvAlign.BottomMid);
    }

    public override void Teardown()
    {
        // Widgets are already gone; the timer and the image pixels they used can follow.
        _clock?.Dispose();
        _clock = null;
        _swatch?.Dispose();
        _swatch = null;
        _time = null;
    }
}
