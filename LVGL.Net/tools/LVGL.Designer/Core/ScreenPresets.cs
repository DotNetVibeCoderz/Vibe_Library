namespace Lvgl.Designer.Core;

/// <summary>A display a layout is commonly designed for.</summary>
public sealed record ScreenPreset(string Name, int Width, int Height)
{
    public override string ToString() => $"{Name}  {Width}×{Height}";
}

/// <summary>The panels LVGL.Net is usually deployed on, for the screen-size picker.</summary>
public static class ScreenPresets
{
    public static IReadOnlyList<ScreenPreset> All { get; } =
    [
        new("Pi Touch Display", 800, 480),
        new("Pi Touch Display 2, portrait", 720, 1280),
        new("Pi Touch Display 2, landscape", 1280, 720),
        new("3.5\" SPI panel", 480, 320),
        new("2.8\" SPI panel", 320, 240),
        new("7\" HDMI panel", 1024, 600),
        new("10\" HDMI panel", 1280, 800),
        new("Round 2.1\" panel", 480, 480),
    ];

    /// <summary>The preset matching a size, or null for a custom size.</summary>
    public static ScreenPreset? Match(int width, int height) =>
        All.FirstOrDefault(preset => preset.Width == width && preset.Height == height);
}
