using Lvgl.Drawing;

namespace Lvgl.Samples.Desktop;

/// <summary>Colours shared by every demo page, kept in one place so the sample looks coherent.</summary>
internal static class Theme
{
    public static readonly LvColor Background = LvColor.FromRgb(0x0F1720u);
    public static readonly LvColor Surface = LvColor.FromRgb(0x1A2430u);
    public static readonly LvColor SurfaceAlt = LvColor.FromRgb(0x243244u);
    public static readonly LvColor Accent = LvColor.FromRgb(0x38BDF8u);
    public static readonly LvColor AccentWarm = LvColor.FromRgb(0xFB923Cu);
    public static readonly LvColor Success = LvColor.FromRgb(0x4ADE80u);
    public static readonly LvColor Danger = LvColor.FromRgb(0xF87171u);
    public static readonly LvColor Text = LvColor.FromRgb(0xE6EDF3u);
    public static readonly LvColor TextMuted = LvColor.FromRgb(0x8B9AAAu);
}
