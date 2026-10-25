using Lvgl.Drawing;
using Lvgl.Widgets;

namespace Lvgl.Gallery.Shell;

/// <summary>What a demo gets from the gallery.</summary>
internal sealed class DemoContext(LvglApplication application, Typeface fonts, Func<Work.GalleryDatabase> database)
{
    private Work.GalleryDatabase? _database;

    public LvglApplication Application { get; } = application;
    public Typeface Fonts { get; } = fonts;

    /// <summary>The SQLite store behind the Work demos, opened on first use.</summary>
    public Work.GalleryDatabase Database => _database ??= database();

    /// <summary>True while rendering screenshots: demos pick a representative, repeatable state.</summary>
    public bool Screenshot { get; init; }
}

/// <summary>
/// One gallery demo. A demo builds into a stage it is handed, and everything it creates that LVGL
/// does not own - timers, styles, images - goes through <see cref="Track{T}"/> so the gallery can
/// free it after the widgets are deleted, in that order.
/// </summary>
internal abstract class Demo
{
    private readonly List<IDisposable> _owned = [];

    public abstract Category Category { get; }
    public abstract string Title { get; }

    /// <summary>One sentence: what the demo shows, in the user's terms.</summary>
    public abstract string Summary { get; }

    /// <summary>The LVGL.Net types doing the work, shown in the footer.</summary>
    public abstract string BuiltWith { get; }

    /// <summary>Name of the embedded source file shown by the Source view.</summary>
    public string SourceFile => GetType().Name + ".cs";

    protected DemoContext Context { get; private set; } = null!;
    protected LvglApplication App => Context.Application;
    protected Typeface Fonts => Context.Fonts;
    protected LvColor Accent => Category.Colour();
    protected LvColor Tint => Category.Tint();

    public void Mount(LvObject stage, DemoContext context)
    {
        Context = context;
        Build(stage, stage.Width, stage.Height);
    }

    /// <summary>Creates the demo's widgets on a stage of <paramref name="width"/> × <paramref name="height"/>.</summary>
    protected abstract void Build(LvObject stage, int width, int height);

    /// <summary>Frees what the demo owns. Called after its widgets have been deleted.</summary>
    public void Unmount()
    {
        for (var i = _owned.Count - 1; i >= 0; i--) _owned[i].Dispose();
        _owned.Clear();
        OnUnmounted();
    }

    protected virtual void OnUnmounted() { }

    protected T Track<T>(T owned) where T : IDisposable
    {
        _owned.Add(owned);
        return owned;
    }

    /// <summary>A timer that stops with the demo. It runs on the LVGL thread, so it may touch widgets.</summary>
    protected LvTimer Every(int periodMs, Action tick) => Track(new LvTimer(periodMs, _ => tick()));

    #region Building blocks

    /// <summary>A label in one of the gallery faces.</summary>
    protected LvLabel Text(LvObject parent, string text, int size = 15, Face face = Face.Body, LvColor? color = null)
    {
        var label = new LvLabel(parent, text);
        label.SetFont(Fonts[face, size]);
        label.SetTextColor(color ?? Palette.Ink);
        return label;
    }

    /// <summary>A container with no chrome of its own: no fill, border, padding or scrolling.</summary>
    protected static T Bare<T>(T widget) where T : LvObject
    {
        widget.SetBackgroundOpacity(0);
        widget.SetBorderWidth(0);
        widget.SetRadius(0);
        widget.SetPadding(0);
        widget.IsScrollable = false;
        return widget;
    }

    /// <summary>A plain rectangle - a fill, a rule, a swatch.</summary>
    protected static LvPanel Box(LvObject parent, int x, int y, int width, int height, LvColor fill, int radius = 0)
    {
        var box = Bare(new LvPanel(parent, width, height));
        box.SetPosition(x, y);
        box.SetBackgroundOpacity(255);
        box.SetBackgroundColor(fill);
        box.SetRadius(radius);
        box.IsClickable = false;
        return box;
    }

    /// <summary>A white card with a hairline edge.</summary>
    protected static LvPanel Card(LvObject parent, int x, int y, int width, int height)
    {
        var card = Box(parent, x, y, width, height, Palette.Card, 12);
        card.SetBorderWidth(1);
        card.SetBorderColor(Palette.Rule);
        return card;
    }

    /// <summary>A flat button. <paramref name="fill"/> defaults to the category colour.</summary>
    protected LvButton Button(LvObject parent, string text, int width, int height, LvColor? fill = null, LvColor? ink = null, int size = 16)
    {
        var colour = fill ?? Accent;
        var button = new LvButton(parent, text);
        button.SetSize(width, height);
        button.SetRadius(10);
        button.SetBackgroundColor(colour);
        button.SetBackgroundColor(Categories.Mix(colour, Palette.Ink, 0.18), LvPart.Main, LvState.Pressed);
        button.SetShadowColor(colour);
        button.SetTextColor(ink ?? Palette.White);
        button.SetFont(Fonts[Face.Strong, size]);
        return button;
    }

    /// <summary>
    /// An <see cref="LvSymbols"/> glyph. Symbols live in the built-in Montserrat fonts, not in the
    /// gallery's TrueType faces, so they get a Montserrat font of their own.
    /// </summary>
    protected static LvLabel Icon(LvObject parent, string symbol, int size = 20, LvColor? color = null)
    {
        var label = new LvLabel(parent, symbol);
        label.SetFont(LvFont.Montserrat(size));
        label.SetTextColor(color ?? Palette.Ink);
        return label;
    }

    /// <summary>A button showing an <see cref="LvSymbols"/> glyph.</summary>
    protected LvButton IconButton(LvObject parent, string symbol, int width, int height, LvColor? fill = null, LvColor? ink = null, int size = 20)
    {
        var button = Button(parent, symbol, width, height, fill, ink);
        button.SetFont(LvFont.Montserrat(size));
        return button;
    }

    /// <summary>A small caption above a value: "TEMPERATURE".</summary>
    protected LvLabel Caption(LvObject parent, string text) => Text(parent, text.ToUpperInvariant(), 12, Face.Mono, Palette.InkMuted);

    #endregion
}
