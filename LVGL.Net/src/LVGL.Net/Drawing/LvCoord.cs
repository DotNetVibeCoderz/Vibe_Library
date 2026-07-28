namespace Lvgl.Drawing;

/// <summary>
/// Helpers for LVGL's "special" coordinate values.
/// </summary>
/// <remarks>
/// <para>
/// LVGL encodes percentages and content-sizing in the high bits of a normal <c>int32_t</c>
/// coordinate rather than using a separate type, via the <c>LV_PCT</c> / <c>LV_SIZE_CONTENT</c>
/// macros. Macros do not survive into a shared library, so the encoding is reproduced here in
/// managed code - it is on the layout path and a P/Invoke per coordinate would not pay for itself.
/// </para>
/// <para>
/// The constants mirror <c>lv_area.h</c> of LVGL v9 exactly. Note that v9 changed both macros from
/// v8: <c>LV_SIZE_CONTENT</c> became <c>LV_COORD_MAX</c> rather than a fixed 2001, and negative
/// percentages are stored relative to <c>LV_PCT_POS_MAX</c> rather than 1000. An integration test
/// cross-checks every value produced here against LVGL's own <c>lv_pct</c>, so a future change
/// upstream fails the build rather than silently misplacing widgets.
/// </para>
/// </remarks>
public static class LvCoord
{
    private const int TypeShift = 29;
    private const int TypeSpec = 1 << TypeShift;
    private const int TypeMask = 3 << TypeShift;

    /// <summary>Largest plain pixel coordinate LVGL can represent (<c>LV_COORD_MAX</c>).</summary>
    public const int CoordMax = (1 << TypeShift) - 1;

    /// <summary>Size the widget to fit its content (<c>LV_SIZE_CONTENT</c>).</summary>
    public const int SizeContent = CoordMax | TypeSpec;

    private const int PctStoredMax = CoordMax - 1;

    /// <summary>Largest percentage LVGL can store (<c>LV_PCT_POS_MAX</c>).</summary>
    public const int PercentMax = PctStoredMax / 2;

    /// <summary>
    /// A percentage of the parent's corresponding dimension (<c>LV_PCT</c>).
    /// </summary>
    /// <remarks>
    /// Negative values are allowed and mean "the parent's size minus that percentage", as in LVGL.
    /// Values beyond <see cref="PercentMax"/> are clamped, matching the macro.
    /// </remarks>
    public static int Percent(int value) => value < 0
        ? (PercentMax - Math.Max(value, -PercentMax)) | TypeSpec
        : Math.Min(value, PercentMax) | TypeSpec;

    /// <summary>True when <paramref name="coord"/> is a percentage or <see cref="SizeContent"/>.</summary>
    public static bool IsSpecial(int coord) => (coord & TypeMask) == TypeSpec;

    /// <summary>True when <paramref name="coord"/> is specifically a percentage.</summary>
    public static bool IsPercent(int coord) => IsSpecial(coord) && Plain(coord) <= PctStoredMax;

    /// <summary>Strips the encoding bits, yielding the raw number a special coordinate carries.</summary>
    public static int Plain(int coord) => coord & ~TypeMask;

    /// <summary>Recovers the percentage from a value produced by <see cref="Percent"/>.</summary>
    public static int GetPercent(int coord)
    {
        var plain = Plain(coord);
        return plain > PercentMax ? PercentMax - plain : plain;
    }
}
