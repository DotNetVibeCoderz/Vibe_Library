using Avalonia.Media;

namespace ScrapyGallery.Controls;

/// <summary>
/// The gallery palette, taken from a garden spider's web: pale silk ground, ink for text and code,
/// thread grey for structure, and the Argiope's amber as the one accent. Status colours encode HTTP
/// outcomes wherever a page appears (web nodes, charts, ledger).
/// </summary>
public static class Palette
{
    public static readonly Color Silk = Color.Parse("#EEF1F4");
    public static readonly Color SilkDeep = Color.Parse("#E2E7ED");
    public static readonly Color Ink = Color.Parse("#18213A");
    public static readonly Color InkSoft = Color.Parse("#4A5573");
    public static readonly Color Thread = Color.Parse("#9AA7BD");
    public static readonly Color Argiope = Color.Parse("#E0A100");
    public static readonly Color Moss = Color.Parse("#2E8B6F");
    public static readonly Color Dusk = Color.Parse("#6C5BC4");
    public static readonly Color Rust = Color.Parse("#C4462B");

    public static IBrush Brush(Color c, double opacity = 1) => new SolidColorBrush(c, opacity);

    /// <summary>Colour for an HTTP status (0 = pending, -1 = failed).</summary>
    public static Color ForStatus(int status) => status switch
    {
        0 => Thread,
        < 0 => Rust,
        >= 200 and < 300 => Moss,
        >= 300 and < 400 => Dusk,
        _ => Rust,
    };
}
