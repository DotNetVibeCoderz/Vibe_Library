using Lvgl.Widgets;

namespace Lvgl.Samples.Desktop.Demos;

/// <summary>Shows the interactive widgets the wrapper exposes, each wired to a live readout.</summary>
internal sealed class WidgetGalleryDemo : DemoPage
{
    private LvLabel? _readout;

    public override string Title => "Widgets";

    public override string Description => "Buttons, sliders, switches, pickers - and their events";

    public override void Build(LvObject container, LvglApplication application)
    {
        container.SetFlexFlow(LvFlexFlow.RowWrap);
        container.SetFlexAlign(LvFlexAlign.Start, LvFlexAlign.Start, LvFlexAlign.Start);
        container.SetGap(12, 12);

        BuildButtonCard(container);
        BuildRangeCard(container);
        BuildToggleCard(container);
        BuildPickerCard(container);
        BuildReadoutCard(container);
    }

    private LvPanel Card(LvObject parent, string title, int width, int height)
    {
        var card = new LvPanel(parent, width, height);
        card.SetBackgroundColor(Theme.Surface);
        card.SetBorderWidth(0);
        card.SetRadius(12);
        card.SetPadding(14);

        var heading = new LvLabel(card, title);
        heading.Align(LvAlign.TopLeft);
        heading.SetTextColor(Theme.TextMuted);
        heading.SetFontSize(12);

        return card;
    }

    private void BuildButtonCard(LvObject parent)
    {
        var card = Card(parent, "Buttons", 260, 150);

        var primary = new LvButton(card, "Primary");
        primary.SetSize(110, 40);
        primary.Align(LvAlign.TopLeft, 0, 28);
        primary.SetRadius(8);
        primary.SetBackgroundColor(Theme.Accent);
        primary.SetTextColor(Theme.Background);
        primary.Clicked += (_, _) => Report("Primary clicked");

        var toggle = new LvButton(card, "Toggle");
        toggle.SetSize(110, 40);
        toggle.Align(LvAlign.TopRight, 0, 28);
        toggle.SetRadius(8);
        toggle.SetBackgroundColor(Theme.SurfaceAlt);
        toggle.SetBackgroundColor(Theme.Success, LvPart.Main, LvState.Checked);
        toggle.IsToggle = true;
        toggle.Clicked += (_, _) => Report($"Toggle is {(toggle.IsChecked ? "on" : "off")}");

        var disabled = new LvButton(card, "Disabled");
        disabled.SetSize(110, 40);
        disabled.Align(LvAlign.BottomLeft);
        disabled.SetRadius(8);
        disabled.IsDisabled = true;

        var icon = new LvButton(card, $"{LvSymbols.Save} Save");
        icon.SetSize(110, 40);
        icon.Align(LvAlign.BottomRight);
        icon.SetRadius(8);
        icon.SetBackgroundColor(Theme.SurfaceAlt);
        icon.Clicked += (_, _) => Report("Saved");
    }

    private void BuildRangeCard(LvObject parent)
    {
        var card = Card(parent, "Ranges", 260, 150);

        var slider = new LvSlider(card);
        slider.SetSize(210, 8);
        slider.Align(LvAlign.TopLeft, 0, 40);
        slider.SetRange(0, 100);
        slider.SetValue(42, animate: false);
        slider.SetBackgroundColor(Theme.Accent, LvPart.Indicator);
        slider.SetBackgroundColor(Theme.Accent, LvPart.Knob);
        slider.ValueChanged += (_, _) => Report($"Slider {slider.Value}");

        var bar = new LvBar(card);
        bar.SetSize(210, 8);
        bar.Align(LvAlign.TopLeft, 0, 78);
        bar.SetValue(68, animate: false);
        bar.SetBackgroundColor(Theme.Success, LvPart.Indicator);

        var arc = new LvArc(card);
        arc.SetSize(56, 56);
        arc.Align(LvAlign.BottomRight);
        arc.SetRange(0, 100);
        arc.Value = 65;
        arc.SetArcColor(Theme.AccentWarm, LvPart.Indicator);
        arc.ValueChanged += (_, _) => Report($"Arc {arc.Value}");

        var caption = new LvLabel(card, "slider / bar / arc");
        caption.Align(LvAlign.BottomLeft);
        caption.SetTextColor(Theme.TextMuted);
        caption.SetFontSize(12);
    }

    private void BuildToggleCard(LvObject parent)
    {
        var card = Card(parent, "Toggles", 220, 150);

        var toggle = new LvSwitch(card);
        toggle.Align(LvAlign.TopLeft, 0, 34);
        toggle.IsOn = true;
        toggle.SetBackgroundColor(Theme.Success, LvPart.Indicator, LvState.Checked);
        toggle.ValueChanged += (_, _) => Report($"Switch {(toggle.IsOn ? "on" : "off")}");

        var switchLabel = new LvLabel(card, "Backlight");
        switchLabel.AlignTo(toggle, LvAlign.OutRightMid, 10, 0);
        switchLabel.SetTextColor(Theme.Text);

        var checkbox = new LvCheckbox(card, "Auto refresh");
        checkbox.Align(LvAlign.TopLeft, 0, 80);
        checkbox.SetTextColor(Theme.Text);
        checkbox.IsChecked = true;
        checkbox.ValueChanged += (_, _) => Report($"Auto refresh {(checkbox.IsChecked ? "on" : "off")}");

        var second = new LvCheckbox(card, "Verbose log");
        second.Align(LvAlign.TopLeft, 0, 110);
        second.SetTextColor(Theme.Text);
        second.ValueChanged += (_, _) => Report($"Verbose log {(second.IsChecked ? "on" : "off")}");
    }

    private void BuildPickerCard(LvObject parent)
    {
        var card = Card(parent, "Pickers", 260, 190);

        var dropdown = new LvDropdown(card, "Celsius", "Fahrenheit", "Kelvin");
        dropdown.SetSize(210, 38);
        dropdown.Align(LvAlign.TopLeft, 0, 30);
        dropdown.ValueChanged += (_, _) => Report($"Unit: {dropdown.SelectedOption}");

        var roller = new LvRoller(card, "1 s", "5 s", "10 s", "30 s", "60 s");
        roller.SetSize(100, 90);
        roller.Align(LvAlign.BottomLeft);
        roller.ValueChanged += (_, _) => Report($"Interval: {roller.SelectedOption}");

        var input = new LvTextArea(card);
        input.SetSize(100, 40);
        input.Align(LvAlign.BottomRight);
        input.IsSingleLine = true;
        input.PlaceholderText = "name";
    }

    private void BuildReadoutCard(LvObject parent)
    {
        var card = Card(parent, "Last event", 500, 90);

        _readout = new LvLabel(card, "interact with a widget above");
        _readout.Align(LvAlign.LeftMid, 0, 8);
        _readout.SetTextColor(Theme.Accent);
        _readout.SetFontSize(16);
    }

    private void Report(string message)
    {
        if (_readout is { IsAlive: true }) _readout.Text = message;
    }

    public override void Teardown() => _readout = null;
}
