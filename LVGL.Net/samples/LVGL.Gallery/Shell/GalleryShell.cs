using Lvgl.Drawing;
using Lvgl.Widgets;

namespace Lvgl.Gallery.Shell;

/// <summary>
/// The gallery frame. A rail of category chips on the left; to the right a field in the selected
/// category's colour, which the selected chip runs into like a folder tab. The field carries the
/// category's demo tabs and the Demo/Source switch, and holds a sheet the demo is built on.
/// </summary>
/// <remarks>
/// Geometry is computed in pixels from the display size: LVGL percentages cannot take part in
/// arithmetic (see LvCoord), and the frame needs "screen width minus the rail".
/// </remarks>
internal sealed class GalleryShell
{
    private const int RailWidth = 216;
    private const int ChipTop = 104;
    private const int ChipHeight = 60;
    private const int ChipGap = 8;
    private const int HeaderHeight = 100;
    private const int FooterHeight = 40;

    private readonly LvglApplication _app;
    private readonly DemoContext _context;
    private readonly IReadOnlyList<Demo> _demos;
    private readonly Typeface _fonts;

    private readonly List<(LvButton Chip, LvPanel Swatch, LvLabel Name, LvLabel Count)> _chips = [];
    private LvPanel _field = null!;
    private LvLabel _eyebrow = null!;
    private LvObject _tabs = null!;
    private LvButton _demoToggle = null!;
    private LvButton _sourceToggle = null!;
    private LvPanel _sheet = null!;
    private LvPanel _stage = null!;
    private LvLabel _summary = null!;
    private LvLabel _builtWith = null!;
    private int _sheetWidth;

    private Demo? _current;
    private Demo? _live;
    private bool _showingSource;

    public GalleryShell(LvglApplication app, DemoContext context, IReadOnlyList<Demo> demos)
    {
        _app = app;
        _context = context;
        _demos = demos;
        _fonts = context.Fonts;
    }

    /// <summary>The running instance of the demo on screen, or null in Source view.</summary>
    public Demo? Live => _live;

    public void Build()
    {
        var screen = _app.Screen;
        var width = _app.Display.Width;
        var height = _app.Display.Height;

        screen.SetBackgroundColor(Palette.Paper);
        screen.SetPadding(0);
        screen.IsScrollable = false;

        // The field first, so the selected chip - created after it - draws over its left edge.
        _field = Plain(new LvPanel(screen, width - RailWidth, height));
        _field.SetPosition(RailWidth, 0);
        _field.SetBackgroundOpacity(255);

        BuildRail(screen, height);
        BuildHeader(width - RailWidth);

        // Inset on every side, so a band of the field shows between the rail and the sheet.
        var sheetWidth = width - RailWidth - 32;
        var sheetHeight = height - HeaderHeight - 16;

        _sheetWidth = sheetWidth;
        _sheet = Plain(new LvPanel(_field, sheetWidth, sheetHeight));
        _sheet.SetPosition(16, HeaderHeight);
        _sheet.SetBackgroundOpacity(255);
        _sheet.SetBackgroundColor(Palette.Sheet);
        _sheet.SetRadius(18);

        _stage = Plain(new LvPanel(_sheet, sheetWidth - 32, sheetHeight - 16 - FooterHeight));
        _stage.SetPosition(16, 16);

        _summary = Label(_sheet, string.Empty, Face.Body, 14, Palette.InkMuted);
        _summary.SetPosition(18, sheetHeight - FooterHeight + 10);
        _summary.SetSize(sheetWidth - 330, 20);
        _summary.LongMode = LvLabelLongMode.Dot;

        _builtWith = Label(_sheet, string.Empty, Face.Mono, 11, Palette.InkFaint);
        _builtWith.Align(LvAlign.BottomRight, -18, -14);
    }

    private void BuildRail(LvObject screen, int height)
    {
        var mark = Label(screen, "LVGL.Net", Face.Display, 34, Palette.Ink);
        mark.SetPosition(22, 22);

        var subtitle = Label(screen, "Gallery of use cases", Face.Body, 14, Palette.InkMuted);
        subtitle.SetPosition(23, 62);

        for (var i = 0; i < Categories.All.Length; i++)
        {
            var category = Categories.All[i];
            var count = _demos.Count(d => d.Category == category);

            var chip = new LvButton(screen);
            chip.SetPosition(12, ChipTop + i * (ChipHeight + ChipGap));
            chip.SetRadius(14);
            chip.SetPadding(0);
            chip.SetShadowColor(Palette.Paper);
            chip.SetBorderWidth(0);

            var swatch = Plain(new LvPanel(chip, 10, 10));
            swatch.SetBackgroundOpacity(255);
            swatch.SetBackgroundColor(category.Colour());
            swatch.SetRadius(5);
            swatch.Align(LvAlign.LeftMid, 16, 0);
            swatch.IsClickable = false;

            var name = Label(chip, category.Name(), Face.Display, 22, Palette.Ink);
            name.Align(LvAlign.LeftMid, 36, -1);

            var number = Label(chip, count.ToString(), Face.Mono, 12, Palette.InkMuted);
            number.Align(LvAlign.RightMid, -18, 0);

            chip.Clicked += (_, _) => ShowCategory(category);
            _chips.Add((chip, swatch, name, number));
        }

        var credit = Label(screen, $"Ported by {LvglAbout.PortedBy}\nof {LvglAbout.Organization}  ·  LVGL {LvglRuntime.LvglVersion}", Face.Body, 12, Palette.InkFaint);
        credit.SetPosition(22, height - 48);
    }

    private void BuildHeader(int fieldWidth)
    {
        _eyebrow = Label(_field, string.Empty, Face.Mono, 12, Palette.White);
        _eyebrow.SetPosition(22, 18);
        _eyebrow.SetOpacity(210);

        _tabs = Plain(new LvPanel(_field, fieldWidth - 260, 52));
        _tabs.SetPosition(10, 40);
        _tabs.SetFlexFlow(LvFlexFlow.Row);
        _tabs.SetFlexAlign(LvFlexAlign.Start, LvFlexAlign.Center, LvFlexAlign.Center);
        _tabs.SetGap(0, 4);

        // Demo | Source: one control, two halves.
        var toggle = Plain(new LvPanel(_field, 184, 40));
        toggle.Align(LvAlign.TopRight, -16, 44);
        toggle.SetBackgroundOpacity(255);
        toggle.SetBackgroundColor(Palette.Ink);
        toggle.SetBackgroundOpacity(40);
        toggle.SetRadius(20);
        toggle.SetPadding(4);

        _demoToggle = ToggleHalf(toggle, "Demo", LvAlign.LeftMid);
        _sourceToggle = ToggleHalf(toggle, "Source", LvAlign.RightMid);
        _demoToggle.Clicked += (_, _) => ShowSource(false);
        _sourceToggle.Clicked += (_, _) => ShowSource(true);
    }

    private LvButton ToggleHalf(LvObject parent, string text, LvAlign align)
    {
        var half = new LvButton(parent, text);
        half.SetSize(88, 32);
        half.Align(align);
        half.SetRadius(16);
        half.SetShadowColor(Palette.Ink);
        half.SetBackgroundOpacity(0);
        half.SetTextColor(Palette.White);
        half.SetFont(_fonts[Face.Strong, 14]);
        return half;
    }

    /// <summary>Opens the first demo of a category.</summary>
    public void ShowCategory(Category category)
    {
        if (_current?.Category == category) return;
        Show(_demos.First(d => d.Category == category));
    }

    /// <summary>Opens a demo, in Demo view.</summary>
    public void Show(Demo demo, bool source = false)
    {
        var categoryChanged = _current?.Category != demo.Category;

        // Widgets first, then what the demo owned (timers, styles, images) - never the reverse.
        _stage.Clear();
        _live?.Unmount();
        _live = null;

        _current = demo;
        _showingSource = source;

        if (categoryChanged) PaintCategory(demo.Category);
        PaintTabs();
        PaintToggle();

        _summary.Text = demo.Summary;
        _builtWith.Text = demo.BuiltWith;

        // The summary takes whatever the type list leaves, and ends in an ellipsis past that.
        _builtWith.UpdateLayout();
        _summary.Width = _sheetWidth - 36 - _builtWith.Width - 24;

        _stage.UpdateLayout();
        if (source) SourceView.Build(_stage, _stage.Width, _stage.Height, demo.SourceFile, _fonts);
        else
        {
            // A fresh instance every time, so a demo always starts from its initial state.
            _live = (Demo)Activator.CreateInstance(demo.GetType())!;
            _live.Mount(_stage, _context);
        }

        _stage.AnimateOpacity(0, 255, 180);
    }

    public void ShowSource(bool source)
    {
        if (_current is null || source == _showingSource) return;
        Show(_current, source);
    }

    private void PaintCategory(Category category)
    {
        var colour = category.Colour();
        _field.SetBackgroundColor(colour);

        var count = _demos.Count(d => d.Category == category);
        _eyebrow.Text = $"{category.Name().ToUpperInvariant()}  ·  {count} {(count == 1 ? "DEMO" : "DEMOS")}";

        for (var i = 0; i < _chips.Count; i++)
        {
            var (chip, swatch, name, number) = _chips[i];
            var selected = Categories.All[i] == category;

            // The selected chip is wider than the rail: it runs into the band of field colour
            // beside the sheet, and since both are the same colour the two read as one shape.
            chip.SetSize(selected ? RailWidth + 4 : RailWidth - 24, ChipHeight);
            chip.SetBackgroundColor(selected ? colour : Palette.Card);
            chip.SetShadowColor(selected ? colour : Palette.Paper);
            chip.SetBackgroundColor(selected ? colour : Palette.Sunken, LvPart.Main, LvState.Pressed);
            swatch.SetBackgroundColor(selected ? Palette.White : Categories.All[i].Colour());
            name.SetTextColor(selected ? Palette.White : Palette.Ink);
            number.SetTextColor(selected ? Palette.White : Palette.InkMuted);
            number.Align(LvAlign.RightMid, selected ? -38 : -18, 0);
        }
    }

    private void PaintTabs()
    {
        _tabs.Clear();
        var colour = _current!.Category.Colour();

        foreach (var demo in _demos.Where(d => d.Category == _current.Category))
        {
            var selected = demo == _current;
            var tab = new LvButton(_tabs, demo.Title);
            tab.SetSize(LvCoord.SizeContent, 40);
            tab.SetPadding(14);
            tab.SetRadius(20);
            tab.SetShadowColor(colour);
            tab.SetFont(_fonts[Face.Display, 22]);
            tab.SetBackgroundColor(Palette.White);
            tab.SetBackgroundOpacity(selected ? (byte)255 : (byte)0);
            tab.SetTextColor(selected ? colour : Palette.White);

            var target = demo;
            tab.Clicked += (_, _) => { if (_current != target) Show(target); };
        }
    }

    private void PaintToggle()
    {
        var colour = _current!.Category.Colour();
        foreach (var (half, on) in new[] { (_demoToggle, !_showingSource), (_sourceToggle, _showingSource) })
        {
            half.SetBackgroundColor(Palette.White);
            half.SetBackgroundOpacity(on ? (byte)255 : (byte)0);
            half.SetTextColor(on ? colour : Palette.White);
        }
    }

    private LvLabel Label(LvObject parent, string text, Face face, int size, LvColor color)
    {
        var label = new LvLabel(parent, text);
        label.SetFont(_fonts[face, size]);
        label.SetTextColor(color);
        return label;
    }

    private static T Plain<T>(T widget) where T : LvObject
    {
        widget.SetBackgroundOpacity(0);
        widget.SetBorderWidth(0);
        widget.SetRadius(0);
        widget.SetPadding(0);
        widget.IsScrollable = false;
        return widget;
    }
}
