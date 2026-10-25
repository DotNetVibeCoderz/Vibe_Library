using Lvgl.Drawing;
using Lvgl.Gallery.Shell;
using Lvgl.Widgets;

namespace Lvgl.Gallery.Home;

/// <summary>
/// A wall panel for a home: choose a room, set its thermostat on the dial, switch and dim its
/// lights, or apply a scene to the whole house. The kind of screen a Pi with a 7" panel is for.
/// </summary>
internal sealed class SmartHome : Demo
{
    private sealed class Light(string name, bool on, int level)
    {
        public string Name { get; } = name;
        public bool On { get; set; } = on;
        public int Level { get; set; } = level;
    }

    private sealed class Room(string name, double current, int target, params Light[] lights)
    {
        public string Name { get; } = name;
        public double Current { get; } = current;
        public int Target { get; set; } = target;
        public Light[] Lights { get; } = lights;
    }

    private readonly Room[] _rooms =
    [
        new("Living room", 21.4, 22, new("Ceiling", true, 80), new("Floor lamp", true, 45), new("TV backlight", false, 30)),
        new("Kitchen", 22.8, 21, new("Ceiling", true, 100), new("Counter strip", false, 70)),
        new("Bedroom", 19.6, 19, new("Ceiling", false, 60), new("Bedside", true, 20)),
        new("Office", 20.9, 21, new("Desk lamp", true, 90), new("Ceiling", false, 70), new("Monitor bias", true, 40)),
    ];

    private readonly List<LvButton> _roomButtons = [];
    private LvArc _dial = null!;
    private LvLabel _target = null!, _current = null!, _mode = null!, _summary = null!;
    private LvObject _lights = null!;
    private int _room;
    private int _lightsWidth;

    public override Category Category => Category.Home;
    public override string Title => "Smart home";
    public override string Summary => "Rooms, a thermostat dial, dimmable lights and house-wide scenes - a wall panel for a Pi touch display.";
    public override string BuiltWith => "LvArc  LvSwitch  LvSlider  LvLed";

    protected override void Build(LvObject stage, int width, int height)
    {
        // Rooms across the top.
        var rooms = Bare(new LvPanel(stage, width, 44));
        rooms.SetFlexFlow(LvFlexFlow.Row);
        rooms.SetGap(0, 8);
        for (var i = 0; i < _rooms.Length; i++)
        {
            var button = Button(rooms, _rooms[i].Name, 124, 42, Palette.Card, Palette.Ink, 15);
            button.SetBorderColor(Palette.Rule);
            var index = i;
            button.Clicked += (_, _) => ShowRoom(index);
            _roomButtons.Add(button);
        }

        _summary = Text(stage, "", 13, Face.Mono, Palette.InkMuted);
        _summary.Align(LvAlign.TopRight, 0, 14);

        // Thermostat.
        var climate = Card(stage, 0, 58, 300, height - 58);
        Caption(climate, "Climate").SetPosition(18, 14);

        _dial = new LvArc(climate);
        _dial.SetSize(230, 230);
        _dial.Align(LvAlign.TopMid, 0, 42);
        _dial.SetRange(16, 28);
        _dial.Rotation = 135;
        _dial.SetBackgroundAngles(0, 270);
        _dial.SetArcWidth(16);
        _dial.SetArcWidth(16, LvPart.Indicator);
        _dial.SetArcColor(Palette.Sunken);
        _dial.SetArcColor(Accent, LvPart.Indicator);
        _dial.SetBackgroundColor(Accent, LvPart.Knob);
        _dial.SetPadding(4, LvPart.Knob);
        _dial.ValueChanged += (_, _) =>
        {
            _rooms[_room].Target = _dial.Value;
            ShowClimate();
        };

        _target = Text(_dial, "", 64, Face.Display, Palette.Ink);
        _target.Align(LvAlign.Center, 0, -6);
        _mode = Text(_dial, "", 13, Face.Mono, Accent);
        _mode.Align(LvAlign.Center, 0, 40);

        _current = Text(climate, "", 15, Face.Body, Palette.InkMuted);
        _current.Align(LvAlign.BottomMid, 0, -18);

        // Lights.
        var lightsX = 316;
        _lightsWidth = width - lightsX;
        var lights = Card(stage, lightsX, 58, _lightsWidth, height - 58 - 70);
        Caption(lights, "Lights").SetPosition(18, 14);
        _lights = Bare(new LvPanel(lights, _lightsWidth - 36, height - 58 - 70 - 48));
        _lights.SetPosition(18, 40);
        _lights.SetFlexFlow(LvFlexFlow.Column);
        _lights.SetGap(10, 0);

        // Scenes for the whole house.
        var scenes = Bare(new LvPanel(stage, _lightsWidth, 58));
        scenes.SetPosition(lightsX, height - 58);
        scenes.SetFlexFlow(LvFlexFlow.Row);
        scenes.SetGap(0, 8);
        var sceneWidth = (_lightsWidth - 24) / 4;
        Scene(scenes, "Morning", sceneWidth, l => (true, 100));
        Scene(scenes, "Evening", sceneWidth, l => (l.Name != "Ceiling", 40));
        Scene(scenes, "Movie", sceneWidth, l => (l.Name.Contains("TV") || l.Name.Contains("bias"), 15));
        Scene(scenes, "All off", sceneWidth, l => (false, l.Level));

        ShowRoom(0);
    }

    private void Scene(LvObject parent, string name, int width, Func<Light, (bool On, int Level)> apply)
    {
        var button = Button(parent, name, width, 56, Tint, Accent, 16);
        button.Clicked += (_, _) =>
        {
            foreach (var light in _rooms.SelectMany(r => r.Lights)) (light.On, light.Level) = apply(light);
            ShowRoom(_room);
        };
    }

    private void ShowRoom(int index)
    {
        _room = index;
        for (var i = 0; i < _roomButtons.Count; i++)
        {
            _roomButtons[i].SetBackgroundColor(i == index ? Accent : Palette.Card);
            _roomButtons[i].SetTextColor(i == index ? Palette.White : Palette.Ink);
            _roomButtons[i].SetBorderWidth(i == index ? 0 : 1);
        }

        _dial.Value = _rooms[index].Target;
        ShowClimate();

        _lights.Clear();
        foreach (var light in _rooms[index].Lights) LightRow(light);
        UpdateSummary();
    }

    private void ShowClimate()
    {
        var room = _rooms[_room];
        _target.Text = $"{room.Target}°";
        var heating = room.Target > room.Current + 0.3;
        var cooling = room.Target < room.Current - 0.3;
        _mode.Text = heating ? "HEATING" : cooling ? "COOLING" : "HOLDING";
        _mode.SetTextColor(heating ? Palette.Warn : cooling ? LvColor.FromRgb(0x2F62E8u) : Accent);
        _current.Text = $"Now {room.Current:0.0}°C in the {room.Name.ToLowerInvariant()}";
    }

    private void LightRow(Light light)
    {
        var row = Bare(new LvPanel(_lights, _lightsWidth - 36, 62));
        row.SetBackgroundOpacity(255);
        row.SetBackgroundColor(Palette.Sheet);
        row.SetRadius(10);

        var led = new LvLed(row) { Color = LvColor.FromRgb(0xF2B53Au) };
        led.SetSize(14, 14);
        led.Align(LvAlign.LeftMid, 16, 0);

        Text(row, light.Name, 16, Face.Body, Palette.Ink).Align(LvAlign.TopLeft, 44, 10);
        var level = Text(row, "", 12, Face.Mono, Palette.InkMuted);
        level.Align(LvAlign.BottomLeft, 44, -10);

        var dimmer = new LvSlider(row);
        dimmer.SetSize(_lightsWidth - 36 - 280, 6);
        dimmer.Align(LvAlign.RightMid, -86, 0);
        dimmer.SetRange(1, 100);
        dimmer.SetValue(light.Level, animate: false);
        dimmer.SetBackgroundColor(Palette.Sunken);
        dimmer.SetBackgroundColor(Accent, LvPart.Indicator);
        dimmer.SetBackgroundColor(Accent, LvPart.Knob);

        var power = new LvSwitch(row) { IsOn = light.On };
        power.Align(LvAlign.RightMid, -16, 0);
        power.SetBackgroundColor(Accent, LvPart.Indicator, LvState.Checked);

        void Show()
        {
            led.Brightness = light.On ? (byte)(80 + light.Level * 175 / 100) : (byte)0;
            level.Text = light.On ? $"{light.Level}%" : "Off";
            dimmer.IsDisabled = !light.On;
            UpdateSummary();
        }

        dimmer.ValueChanged += (_, _) => { light.Level = dimmer.Value; Show(); };
        power.ValueChanged += (_, _) => { light.On = power.IsOn; Show(); };
        Show();
    }

    private void UpdateSummary()
    {
        var on = _rooms.SelectMany(r => r.Lights).Count(l => l.On);
        var total = _rooms.Sum(r => r.Lights.Length);
        _summary.Text = $"{on} of {total} lights on";
    }
}
