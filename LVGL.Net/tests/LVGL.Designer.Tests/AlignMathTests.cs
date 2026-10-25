using Lvgl.Designer.Core;

namespace Lvgl.Designer.Tests;

/// <summary>
/// Checks the resize write-back against LVGL's own positioning rules for every alignment.
/// </summary>
/// <remarks>
/// <see cref="Place"/> transcribes <c>lv_obj_align</c>'s switch from <c>lv_obj_pos.c</c>
/// independently of <see cref="AlignMath"/>, so the two are not the same code checking itself.
/// For each alignment, a widget is resized from every edge; the values AlignMath writes back must
/// put the widget exactly where the user dragged it when LVGL places it again.
/// </remarks>
public class AlignMathTests
{
    private const int ParentWidth = 800;
    private const int ParentHeight = 480;

    /// <summary>Where LVGL puts a widget of size (w, h) with this alignment and offset.</summary>
    private static (double X, double Y) Place(LvAlign? align, double w, double h, double ox, double oy)
    {
        const double pw = ParentWidth;
        const double ph = ParentHeight;

        var (x, y) = (align ?? LvAlign.Default) switch
        {
            LvAlign.Default or LvAlign.TopLeft => (0d, 0d),
            LvAlign.TopMid => (pw / 2 - w / 2, 0),
            LvAlign.TopRight => (pw - w, 0),
            LvAlign.BottomLeft => (0, ph - h),
            LvAlign.BottomMid => (pw / 2 - w / 2, ph - h),
            LvAlign.BottomRight => (pw - w, ph - h),
            LvAlign.LeftMid => (0, ph / 2 - h / 2),
            LvAlign.RightMid => (pw - w, ph / 2 - h / 2),
            LvAlign.Center => (pw / 2 - w / 2, ph / 2 - h / 2),
            LvAlign.OutTopLeft => (0, -h),
            LvAlign.OutTopMid => (pw / 2 - w / 2, -h),
            LvAlign.OutTopRight => (pw - w, -h),
            LvAlign.OutBottomLeft => (0, ph),
            LvAlign.OutBottomMid => (pw / 2 - w / 2, ph),
            LvAlign.OutBottomRight => (pw - w, ph),
            LvAlign.OutLeftTop => (-w, 0),
            LvAlign.OutLeftMid => (-w, ph / 2 - h / 2),
            LvAlign.OutLeftBottom => (-w, ph - h),
            LvAlign.OutRightTop => (pw, 0),
            LvAlign.OutRightMid => (pw, ph / 2 - h / 2),
            LvAlign.OutRightBottom => (pw, ph - h),
            _ => throw new ArgumentOutOfRangeException(nameof(align)),
        };

        return (x + ox, y + oy);
    }

    public static TheoryData<LvAlign?, ResizeEdge> Cases()
    {
        var data = new TheoryData<LvAlign?, ResizeEdge>();
        LvAlign?[] aligns = [null, .. Enum.GetValues<LvAlign>().Cast<LvAlign?>()];
        ResizeEdge[] edges = [ResizeEdge.Left, ResizeEdge.Right, ResizeEdge.Top, ResizeEdge.Bottom, ResizeEdge.TopLeft, ResizeEdge.BottomRight, ResizeEdge.TopRight, ResizeEdge.BottomLeft];

        foreach (var align in aligns)
        {
            foreach (var edge in edges) data.Add(align, edge);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void A_resize_lands_where_it_was_dragged(LvAlign? align, ResizeEdge edge)
    {
        // Even sizes and deltas: LVGL divides by two with integer maths, the transcription above
        // does not, and they only agree exactly on even numbers.
        const int startX = 24, startY = -16, width = 120, height = 60;

        var (left, top) = Place(align, width, height, startX, startY);
        var start = new Rect(left, top, width, height);

        var target = ResizeMath.Apply(start, edge, dx: 30, dy: -20);
        var (x, y, w, h) = AlignMath.Resize(align, startX, startY, start, target);

        var (placedX, placedY) = Place(align, w, h, x, y);

        Assert.Equal(target.X, placedX, 3);
        Assert.Equal(target.Y, placedY, 3);
        Assert.Equal(target.Width, w);
        Assert.Equal(target.Height, h);
    }

    [Fact]
    public void Resizing_never_inverts_or_collapses_a_rectangle()
    {
        var start = new Rect(10, 10, 40, 40);

        var squashed = ResizeMath.Apply(start, ResizeEdge.Left, dx: 500, dy: 0);

        Assert.Equal(ResizeMath.MinimumSize, squashed.Width);
        Assert.Equal(start.Right, squashed.Right);
    }

    [Theory]
    [InlineData(13, 8, 16)]
    [InlineData(11, 8, 8)]
    [InlineData(-5, 8, -8)]
    [InlineData(13.4, 1, 13)]
    public void Snapping_rounds_to_the_grid(double value, double grid, double expected) =>
        Assert.Equal(expected, ResizeMath.Snap(value, grid));
}
