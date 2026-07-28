namespace Lvgl;

/// <summary>
/// Icon glyphs from LVGL's built-in symbol font, for use inside label and button text.
/// </summary>
/// <remarks>
/// <para>
/// LVGL's <c>LV_SYMBOL_*</c> macros are Private Use Area code points that its fonts map to
/// FontAwesome glyphs. They are built here from their numeric code points rather than pasted in as
/// literals, so the source file stays plain ASCII and cannot be corrupted by an editor that
/// re-encodes it. The UTF-8 marshaller emits exactly the three-byte sequences LVGL expects.
/// </para>
/// <para>
/// Concatenate with normal text, e.g. <c>$"{LvSymbols.Warning} Sensor offline"</c>.
/// </para>
/// </remarks>
public static class LvSymbols
{
    private static string Glyph(int codePoint) => char.ConvertFromUtf32(codePoint);

    public static string Audio { get; } = Glyph(0xF001);
    public static string Video { get; } = Glyph(0xF008);
    public static string List { get; } = Glyph(0xF00B);
    public static string Ok { get; } = Glyph(0xF00C);
    public static string Close { get; } = Glyph(0xF00D);
    public static string Power { get; } = Glyph(0xF011);
    public static string Settings { get; } = Glyph(0xF013);
    public static string Home { get; } = Glyph(0xF015);
    public static string Download { get; } = Glyph(0xF019);
    public static string Drive { get; } = Glyph(0xF01C);
    public static string Refresh { get; } = Glyph(0xF021);
    public static string Mute { get; } = Glyph(0xF026);
    public static string VolumeMid { get; } = Glyph(0xF027);
    public static string VolumeMax { get; } = Glyph(0xF028);
    public static string Image { get; } = Glyph(0xF03E);
    public static string Previous { get; } = Glyph(0xF048);
    public static string Play { get; } = Glyph(0xF04B);
    public static string Pause { get; } = Glyph(0xF04C);
    public static string Stop { get; } = Glyph(0xF04D);
    public static string Next { get; } = Glyph(0xF051);
    public static string Eject { get; } = Glyph(0xF052);
    public static string Left { get; } = Glyph(0xF053);
    public static string Right { get; } = Glyph(0xF054);
    public static string Plus { get; } = Glyph(0xF067);
    public static string Minus { get; } = Glyph(0xF068);
    public static string Warning { get; } = Glyph(0xF071);
    public static string Up { get; } = Glyph(0xF077);
    public static string Down { get; } = Glyph(0xF078);
    public static string Loop { get; } = Glyph(0xF079);
    public static string Directory { get; } = Glyph(0xF07B);
    public static string Upload { get; } = Glyph(0xF093);
    public static string Call { get; } = Glyph(0xF095);
    public static string Cut { get; } = Glyph(0xF0C4);
    public static string Copy { get; } = Glyph(0xF0C5);
    public static string Save { get; } = Glyph(0xF0C7);
    public static string Bars { get; } = Glyph(0xF0C9);
    public static string Envelope { get; } = Glyph(0xF0E0);
    public static string Charge { get; } = Glyph(0xF0E7);
    public static string Bell { get; } = Glyph(0xF0F3);
    public static string Keyboard { get; } = Glyph(0xF11C);
    public static string Gps { get; } = Glyph(0xF124);
    public static string File { get; } = Glyph(0xF158);
    public static string Wifi { get; } = Glyph(0xF1EB);
    public static string BatteryFull { get; } = Glyph(0xF240);
    public static string BatteryThreeQuarters { get; } = Glyph(0xF241);
    public static string BatteryHalf { get; } = Glyph(0xF242);
    public static string BatteryQuarter { get; } = Glyph(0xF243);
    public static string BatteryEmpty { get; } = Glyph(0xF244);
    public static string Usb { get; } = Glyph(0xF287);
    public static string Bluetooth { get; } = Glyph(0xF293);
    public static string Trash { get; } = Glyph(0xF2ED);
    public static string Edit { get; } = Glyph(0xF304);
    public static string Backspace { get; } = Glyph(0xF55A);
    public static string SdCard { get; } = Glyph(0xF7C2);
}
