using Lvgl.Drawing;

namespace Lvgl.Gallery.Shell;

/// <summary>
/// The gallery's colours. The neutrals are a cool, light grey-blue - the paper every demo sits on -
/// and each use-case category owns one saturated colour that fills the field behind its demos.
/// </summary>
internal static class Palette
{
    public static readonly LvColor Paper = LvColor.FromRgb(0xE9EDF2u);
    public static readonly LvColor Sheet = LvColor.FromRgb(0xF7F9FBu);
    public static readonly LvColor Card = LvColor.FromRgb(0xFFFFFFu);
    public static readonly LvColor Sunken = LvColor.FromRgb(0xEEF1F5u);
    public static readonly LvColor Rule = LvColor.FromRgb(0xD5DCE5u);
    public static readonly LvColor Ink = LvColor.FromRgb(0x18212Bu);
    public static readonly LvColor InkMuted = LvColor.FromRgb(0x5B6878u);
    public static readonly LvColor InkFaint = LvColor.FromRgb(0x98A3B1u);
    public static readonly LvColor White = LvColor.FromRgb(0xFFFFFFu);

    // States, shared by every demo so "good" and "bad" read the same everywhere.
    public static readonly LvColor Good = LvColor.FromRgb(0x14A06Bu);
    public static readonly LvColor Warn = LvColor.FromRgb(0xE09A1Au);
    public static readonly LvColor Bad = LvColor.FromRgb(0xE0464Eu);
}

/// <summary>The use cases the gallery is organised by.</summary>
internal enum Category
{
    Scientific,
    Work,
    Multimedia,
    Games,
    Tools,
    Home,
}

internal static class Categories
{
    public static readonly Category[] All = Enum.GetValues<Category>();

    public static string Name(this Category category) => category switch
    {
        Category.Scientific => "Scientific",
        Category.Work => "Work & data",
        Category.Multimedia => "Multimedia",
        Category.Games => "Games",
        Category.Tools => "Tools",
        Category.Home => "Home & IoT",
        _ => category.ToString(),
    };

    /// <summary>The category's field colour. White text sits on all of them.</summary>
    public static LvColor Colour(this Category category) => LvColor.FromRgb(category switch
    {
        Category.Scientific => 0x2F62E8u,   // cobalt: plotting paper ink
        Category.Work => 0x0B8F63u,         // ledger green
        Category.Multimedia => 0xD8336Bu,   // magenta
        Category.Games => 0xC9690Eu,        // arcade amber
        Category.Tools => 0x6646D6u,        // violet
        Category.Home => 0x0A8C98u,         // teal
        _ => 0x5B6878u,
    });

    /// <summary>A pale tint of the category colour for fills inside a demo.</summary>
    public static LvColor Tint(this Category category) => Mix(category.Colour(), Palette.White, 0.86);

    public static LvColor Mix(LvColor a, LvColor b, double towardB)
    {
        static int Channel(uint rgb, int shift) => (int)((rgb >> shift) & 0xFF);
        int Lerp(int shift) => (int)Math.Round(Channel(a.Rgb, shift) + (Channel(b.Rgb, shift) - Channel(a.Rgb, shift)) * towardB);
        return LvColor.FromRgb((byte)Lerp(16), (byte)Lerp(8), (byte)Lerp(0));
    }
}
