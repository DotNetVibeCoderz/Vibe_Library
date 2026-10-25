using Lvgl.Drawing;

namespace Lvgl.Gallery.Shell;

/// <summary>The faces the gallery uses.</summary>
internal enum Face
{
    /// <summary>Barlow Condensed SemiBold: titles, big readouts, game tiles.</summary>
    Display,
    /// <summary>Barlow Medium: running text and controls.</summary>
    Body,
    /// <summary>Barlow SemiBold: emphasis inside running text.</summary>
    Strong,
    /// <summary>Martian Mono: source code, data, units.</summary>
    Mono,
}

/// <summary>
/// TrueType fonts rasterised by LVGL's TinyTTF, one <see cref="LvFont"/> per face and size, created
/// on first use and kept for the life of the app. Falls back to the built-in Montserrat when the
/// font files are missing, so the gallery still runs from an unusual working directory.
/// </summary>
internal sealed class Typeface : IDisposable
{
    private static readonly int[] MontserratSizes = [12, 14, 16, 20, 24, 26, 28, 36];

    private readonly Dictionary<(Face, int), LvFont> _fonts = [];
    private readonly string _folder;

    public Typeface(string folder) => _folder = folder;

    public LvFont this[Face face, int size] => Get(face, size);

    public LvFont Get(Face face, int size)
    {
        if (_fonts.TryGetValue((face, size), out var font)) return font;

        var file = Path.Combine(_folder, face switch
        {
            Face.Display => "BarlowCondensed-SemiBold.ttf",
            Face.Body => "Barlow-Medium.ttf",
            Face.Strong => "Barlow-SemiBold.ttf",
            _ => "MartianMono.ttf",
        });

        font = File.Exists(file) ? LvFont.FromFile(file, size) : LvFont.Montserrat(Nearest(size));
        _fonts[(face, size)] = font;
        return font;
    }

    private static int Nearest(int size) => MontserratSizes.MinBy(s => Math.Abs(s - size));

    /// <summary>Frees every font. Only after the widgets using them are gone.</summary>
    public void Dispose()
    {
        foreach (var font in _fonts.Values) font.Dispose();
        _fonts.Clear();
    }
}
