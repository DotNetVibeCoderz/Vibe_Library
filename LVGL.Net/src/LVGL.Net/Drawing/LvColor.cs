using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Lvgl.Drawing;

/// <summary>
/// A 24-bit RGB colour.
/// </summary>
/// <remarks>
/// Deliberately *not* a mirror of the native <c>lv_color_t</c>: that type is a three-byte struct
/// whose by-value calling convention differs between Windows x64 and System V, so LVGL.Net never
/// passes it across the boundary. Colours travel as a packed <c>0xRRGGBB</c> <see cref="uint"/>
/// and the native shim converts them with <c>lv_color_hex</c>.
/// </remarks>
public readonly struct LvColor : IEquatable<LvColor>
{
    private readonly uint _rgb;

    /// <summary>Creates a colour from packed <c>0xRRGGBB</c>. The upper byte is ignored.</summary>
    public LvColor(uint rgb) => _rgb = rgb & 0x00FFFFFFu;

    /// <summary>Creates a colour from 8-bit components.</summary>
    public LvColor(byte r, byte g, byte b) => _rgb = ((uint)r << 16) | ((uint)g << 8) | b;

    /// <summary>Packed <c>0xRRGGBB</c> value handed to the native side.</summary>
    public uint Rgb => _rgb;

    public byte R => (byte)(_rgb >> 16);
    public byte G => (byte)(_rgb >> 8);
    public byte B => (byte)_rgb;

    /// <summary>Creates a colour from packed <c>0xRRGGBB</c>.</summary>
    public static LvColor FromRgb(uint rgb) => new(rgb);

    /// <summary>Creates a colour from 8-bit components.</summary>
    public static LvColor FromRgb(byte r, byte g, byte b) => new(r, g, b);

    /// <summary>
    /// Parses <c>#RGB</c>, <c>#RRGGBB</c> or bare hex. Returns <see langword="false"/> instead of
    /// throwing so it can back designer text boxes directly.
    /// </summary>
    public static bool TryParse(string? text, out LvColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var span = text.AsSpan().Trim();
        if (span.Length > 0 && span[0] == '#') span = span[1..];
        if (span.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) span = span[2..];

        if (span.Length == 3)
        {
            // #abc -> #aabbcc
            Span<char> expanded = stackalloc char[6];
            for (var i = 0; i < 3; i++)
            {
                expanded[i * 2] = span[i];
                expanded[i * 2 + 1] = span[i];
            }
            span = expanded.ToString();
        }

        if (span.Length != 6) return false;
        if (!uint.TryParse(span, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value)) return false;

        color = new LvColor(value);
        return true;
    }

    /// <summary>Parses a hex colour, throwing on malformed input.</summary>
    public static LvColor Parse(string text) =>
        TryParse(text, out var color) ? color : throw new FormatException($"'{text}' is not a valid colour.");

    /// <summary>
    /// Blends towards <paramref name="other"/>. <paramref name="amount"/> is clamped to 0..1.
    /// </summary>
    public LvColor Lerp(LvColor other, float amount)
    {
        var t = Math.Clamp(amount, 0f, 1f);
        return new LvColor(
            (byte)(R + (other.R - R) * t),
            (byte)(G + (other.G - G) * t),
            (byte)(B + (other.B - B) * t));
    }

    /// <summary>Returns a darker shade; <paramref name="amount"/> 0 = unchanged, 1 = black.</summary>
    public LvColor Darken(float amount) => Lerp(Black, amount);

    /// <summary>Returns a lighter tint; <paramref name="amount"/> 0 = unchanged, 1 = white.</summary>
    public LvColor Lighten(float amount) => Lerp(White, amount);

    /// <summary>
    /// Relative luminance per WCAG 2.x, used to pick readable foreground text over this colour.
    /// </summary>
    public float Luminance()
    {
        static float Channel(byte c)
        {
            var v = c / 255f;
            return v <= 0.03928f ? v / 12.92f : MathF.Pow((v + 0.055f) / 1.055f, 2.4f);
        }
        return 0.2126f * Channel(R) + 0.7152f * Channel(G) + 0.0722f * Channel(B);
    }

    /// <summary>Black or white, whichever reads better on this colour.</summary>
    public LvColor ContrastingText() => Luminance() > 0.179f ? Black : White;

    /// <summary>The XRGB8888 word this colour occupies in a 32-bit LVGL frame buffer.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint ToXrgb8888() => 0xFF000000u | _rgb;

    public bool Equals(LvColor other) => _rgb == other._rgb;

    public override bool Equals([NotNullWhen(true)] object? obj) => obj is LvColor other && Equals(other);

    public override int GetHashCode() => (int)_rgb;

    public override string ToString() => "#" + _rgb.ToString("X6", CultureInfo.InvariantCulture);

    public static bool operator ==(LvColor left, LvColor right) => left.Equals(right);

    public static bool operator !=(LvColor left, LvColor right) => !left.Equals(right);

    public static implicit operator LvColor(uint rgb) => new(rgb);

    // A small palette so samples and the designer share one vocabulary.
    public static LvColor Black => new(0x000000u);
    public static LvColor White => new(0xFFFFFFu);
    public static LvColor Red => new(0xE53935u);
    public static LvColor Green => new(0x43A047u);
    public static LvColor Blue => new(0x1E88E5u);
    public static LvColor Yellow => new(0xFDD835u);
    public static LvColor Orange => new(0xFB8C00u);
    public static LvColor Purple => new(0x8E24AAu);
    public static LvColor Cyan => new(0x00ACC1u);
    public static LvColor Grey => new(0x9E9E9Eu);
    public static LvColor DarkGrey => new(0x424242u);
}
