using Lvgl.Drawing;

namespace Lvgl.Tests;

public class LvColorTests
{
    [Theory]
    [InlineData("#FF0000", 0xFF0000u)]
    [InlineData("00FF00", 0x00FF00u)]
    [InlineData("0x0000FF", 0x0000FFu)]
    [InlineData("#abc", 0xAABBCCu)]
    [InlineData("  #38BDF8  ", 0x38BDF8u)]
    public void TryParse_accepts_the_common_hex_forms(string text, uint expected)
    {
        Assert.True(LvColor.TryParse(text, out var color));
        Assert.Equal(expected, color.Rgb);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("#12345")]
    [InlineData("#GGGGGG")]
    [InlineData("rgb(1,2,3)")]
    [InlineData(null)]
    public void TryParse_rejects_malformed_input_without_throwing(string? text)
    {
        Assert.False(LvColor.TryParse(text, out _));
    }

    [Fact]
    public void Components_round_trip()
    {
        var color = new LvColor(0x12, 0x34, 0x56);

        Assert.Equal(0x12, color.R);
        Assert.Equal(0x34, color.G);
        Assert.Equal(0x56, color.B);
        Assert.Equal(0x123456u, color.Rgb);
        Assert.Equal("#123456", color.ToString());
    }

    [Fact]
    public void ToString_round_trips_through_Parse()
    {
        var original = LvColor.FromRgb(0x38BDF8u);
        Assert.Equal(original, LvColor.Parse(original.ToString()));
    }

    [Fact]
    public void Upper_byte_of_the_packed_value_is_discarded()
    {
        // Callers pass 0xAARRGGBB by mistake often enough that the constructor masks it off;
        // leaving it in place would corrupt the value handed to lv_color_hex.
        Assert.Equal(0x38BDF8u, new LvColor(0xFF38BDF8u).Rgb);
    }

    [Fact]
    public void Lerp_is_clamped_at_both_ends()
    {
        var from = LvColor.Black;
        var to = LvColor.White;

        Assert.Equal(from, from.Lerp(to, -5f));
        Assert.Equal(to, from.Lerp(to, 5f));
    }

    [Fact]
    public void ContrastingText_picks_the_readable_foreground()
    {
        Assert.Equal(LvColor.Black, LvColor.White.ContrastingText());
        Assert.Equal(LvColor.White, LvColor.Black.ContrastingText());
        Assert.Equal(LvColor.White, LvColor.FromRgb(0x1E3A8Au).ContrastingText());
    }

    [Fact]
    public void ToXrgb8888_sets_the_padding_byte_opaque()
    {
        // The framebuffer and SDL paths treat the top byte as ignored padding, but WPF's Bgr32
        // and some devices read it as alpha; leaving it zero would render nothing.
        Assert.Equal(0xFF38BDF8u, LvColor.FromRgb(0x38BDF8u).ToXrgb8888());
    }
}
