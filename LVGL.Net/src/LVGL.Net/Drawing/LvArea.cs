using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Lvgl.Drawing;

/// <summary>
/// An inclusive rectangle, matching LVGL's <c>lv_area_t</c> semantics: <see cref="X2"/> and
/// <see cref="Y2"/> are the last pixel *inside* the area, so a 1x1 area has <c>X1 == X2</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct LvArea(int x1, int y1, int x2, int y2)
{
    public int X1 { get; } = x1;
    public int Y1 { get; } = y1;
    public int X2 { get; } = x2;
    public int Y2 { get; } = y2;

    /// <summary>Width in pixels (inclusive bounds).</summary>
    public int Width => X2 - X1 + 1;

    /// <summary>Height in pixels (inclusive bounds).</summary>
    public int Height => Y2 - Y1 + 1;

    /// <summary>True when the area covers no pixels.</summary>
    public bool IsEmpty => X2 < X1 || Y2 < Y1;

    /// <summary>Creates an area from a position and size.</summary>
    public static LvArea FromSize(int x, int y, int width, int height) =>
        new(x, y, x + width - 1, y + height - 1);

    /// <summary>Clips this area to <paramref name="bounds"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public LvArea Clip(in LvArea bounds) => new(
        Math.Max(X1, bounds.X1),
        Math.Max(Y1, bounds.Y1),
        Math.Min(X2, bounds.X2),
        Math.Min(Y2, bounds.Y2));

    public override string ToString() => $"({X1},{Y1})-({X2},{Y2}) {Width}x{Height}";
}
