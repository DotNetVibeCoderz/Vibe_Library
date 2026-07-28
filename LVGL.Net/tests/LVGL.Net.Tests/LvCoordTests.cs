using Lvgl.Drawing;

namespace Lvgl.Tests;

/// <summary>
/// Guards the reimplementation of LVGL's coordinate macros.
/// </summary>
/// <remarks>
/// <c>LV_PCT</c> and <c>LV_SIZE_CONTENT</c> are C macros, so they cannot be called through
/// P/Invoke and had to be reproduced in managed code. These tests pin the encoding without needing
/// the native library; <c>LvglIntegrationTests</c> additionally cross-checks the same values
/// against LVGL's own <c>lv_pct</c>. Both matter: a wrong encoding does not fail loudly, it just
/// lays widgets out somewhere unexpected.
/// </remarks>
public class LvCoordTests
{
    private const int TypeSpec = 1 << 29;
    private const int CoordMax = (1 << 29) - 1;
    private const int PctPosMax = (CoordMax - 1) / 2;

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(50)]
    [InlineData(100)]
    [InlineData(1000)]
    public void Percent_matches_the_LV_PCT_macro_for_positive_values(int value)
    {
        Assert.Equal(value | TypeSpec, LvCoord.Percent(value));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-50)]
    [InlineData(-100)]
    public void Percent_matches_the_LV_PCT_macro_for_negative_values(int value)
    {
        // LVGL v9 stores a negative percentage as LV_PCT_POS_MAX - value, not as a sign bit and
        // not as v8's 1000 - value.
        Assert.Equal((PctPosMax - value) | TypeSpec, LvCoord.Percent(value));
    }

    [Fact]
    public void Percent_clamps_to_the_representable_range()
    {
        Assert.Equal(PctPosMax | TypeSpec, LvCoord.Percent(int.MaxValue));
        Assert.Equal((PctPosMax + PctPosMax) | TypeSpec, LvCoord.Percent(int.MinValue));
    }

    [Fact]
    public void SizeContent_matches_the_macro()
    {
        // v9: LV_SIZE_CONTENT = LV_COORD_SET_SPEC(LV_COORD_MAX). v8 used a fixed 2001.
        Assert.Equal(CoordMax | TypeSpec, LvCoord.SizeContent);
    }

    [Fact]
    public void Special_coordinates_are_distinguishable_from_pixels()
    {
        Assert.True(LvCoord.IsSpecial(LvCoord.Percent(50)));
        Assert.True(LvCoord.IsSpecial(LvCoord.SizeContent));

        Assert.False(LvCoord.IsSpecial(0));
        Assert.False(LvCoord.IsSpecial(480));
        Assert.False(LvCoord.IsSpecial(-20));
    }

    [Fact]
    public void SizeContent_is_special_but_not_a_percentage()
    {
        // The two share the SPEC type bit and are told apart by magnitude, so this is worth
        // pinning: confusing them would make a content-sized widget fill its parent instead.
        Assert.True(LvCoord.IsSpecial(LvCoord.SizeContent));
        Assert.False(LvCoord.IsPercent(LvCoord.SizeContent));
        Assert.True(LvCoord.IsPercent(LvCoord.Percent(50)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(25)]
    [InlineData(100)]
    [InlineData(-30)]
    public void GetPercent_recovers_what_Percent_encoded(int value)
    {
        Assert.Equal(value, LvCoord.GetPercent(LvCoord.Percent(value)));
    }

    [Fact]
    public void Plain_recovers_the_encoded_number()
    {
        Assert.Equal(75, LvCoord.Plain(LvCoord.Percent(75)));
        Assert.Equal(CoordMax, LvCoord.Plain(LvCoord.SizeContent));
    }

    [Fact]
    public void Arithmetic_on_a_percentage_does_not_produce_a_percentage()
    {
        // Documents the trap that broke the first version of the desktop sample: subtracting a
        // pixel count from LvCoord.Percent(100) silently yields a plain pixel value.
        var broken = LvCoord.Percent(100) - 190;

        Assert.False(LvCoord.IsSpecial(broken));
        Assert.NotEqual(LvCoord.Percent(100), broken);
    }
}
