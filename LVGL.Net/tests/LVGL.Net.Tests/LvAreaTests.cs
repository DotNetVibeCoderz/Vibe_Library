using Lvgl.Drawing;

namespace Lvgl.Tests;

public class LvAreaTests
{
    [Fact]
    public void Bounds_are_inclusive_like_lv_area_t()
    {
        // A 1x1 area has x1 == x2 in LVGL; getting this wrong shifts every blit by a pixel.
        var single = new LvArea(5, 5, 5, 5);

        Assert.Equal(1, single.Width);
        Assert.Equal(1, single.Height);
        Assert.False(single.IsEmpty);
    }

    [Fact]
    public void FromSize_produces_inclusive_bounds()
    {
        var area = LvArea.FromSize(10, 20, 100, 50);

        Assert.Equal(10, area.X1);
        Assert.Equal(20, area.Y1);
        Assert.Equal(109, area.X2);
        Assert.Equal(69, area.Y2);
        Assert.Equal(100, area.Width);
        Assert.Equal(50, area.Height);
    }

    [Fact]
    public void Clip_restricts_to_the_bounds()
    {
        var screen = new LvArea(0, 0, 799, 479);
        var clipped = new LvArea(-20, -10, 900, 600).Clip(screen);

        Assert.Equal(new LvArea(0, 0, 799, 479).ToString(), clipped.ToString());
    }

    [Fact]
    public void Clip_of_a_disjoint_area_is_empty()
    {
        var screen = new LvArea(0, 0, 799, 479);
        var clipped = new LvArea(900, 500, 1000, 600).Clip(screen);

        Assert.True(clipped.IsEmpty);
    }

    [Fact]
    public void Inverted_bounds_are_empty()
    {
        Assert.True(new LvArea(10, 10, 9, 20).IsEmpty);
        Assert.True(new LvArea(10, 10, 20, 9).IsEmpty);
    }
}
