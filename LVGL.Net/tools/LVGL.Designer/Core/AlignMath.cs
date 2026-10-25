using Lvgl.Ui;

namespace Lvgl.Designer.Core;

/// <summary>
/// Geometry for direct manipulation: what to write into a node so that its widget lands where
/// the user dragged it.
/// </summary>
/// <remarks>
/// <para>
/// Moving is simple because X and Y are always an offset - from the parent's corner for an
/// absolute widget, from the alignment anchor for an aligned one - so a drag of (dx, dy) adds
/// (dx, dy) either way.
/// </para>
/// <para>
/// Resizing is not: an aligned widget's anchor moves with its own size. A centred widget that
/// grows by 20 px shifts 10 px left on its own, so the offset has to absorb that shift or the
/// edge the user is not dragging would move too. <see cref="AnchorShift"/> is that correction,
/// derived from LVGL's <c>lv_obj_align</c> rules, and needs no knowledge of the parent's size.
/// </para>
/// </remarks>
public static class AlignMath
{
    /// <summary>
    /// How far LVGL moves a widget's left/top edge, for a given alignment, when the widget grows by
    /// (<paramref name="growX"/>, <paramref name="growY"/>) with its offset unchanged.
    /// </summary>
    public static (double X, double Y) AnchorShift(LvAlign? align, double growX, double growY)
    {
        var horizontal = Horizontal(align ?? LvAlign.Default);
        var vertical = Vertical(align ?? LvAlign.Default);

        return (Shift(horizontal, growX), Shift(vertical, growY));

        static double Shift(Anchor anchor, double grow) => anchor switch
        {
            Anchor.Start => 0,
            Anchor.Middle => -grow / 2,
            Anchor.End => -grow,
            _ => 0,
        };
    }

    /// <summary>
    /// The node values that put the widget at <paramref name="target"/> after a resize from
    /// <paramref name="start"/>, given the node's offsets at the start of the gesture.
    /// </summary>
    public static (int X, int Y, int Width, int Height) Resize(
        LvAlign? align, int startX, int startY, Rect start, Rect target)
    {
        var growX = target.Width - start.Width;
        var growY = target.Height - start.Height;
        var (shiftX, shiftY) = AnchorShift(align, growX, growY);

        // The left/top edge moved by (target - start); the anchor already accounts for shiftX/Y of
        // that, so the offset covers the rest.
        var x = startX + (target.X - start.X) - shiftX;
        var y = startY + (target.Y - start.Y) - shiftY;

        return ((int)Math.Round(x), (int)Math.Round(y), (int)Math.Round(target.Width), (int)Math.Round(target.Height));
    }

    private enum Anchor
    {
        Start,
        Middle,
        End,
    }

    // The "out" alignments put the widget outside the parent: OutLeft* hangs off the left edge by
    // its own width (so it behaves like End), OutRight* starts at the parent's right edge (Start).
    private static Anchor Horizontal(LvAlign align) => align switch
    {
        LvAlign.TopMid or LvAlign.BottomMid or LvAlign.Center or LvAlign.OutTopMid or LvAlign.OutBottomMid => Anchor.Middle,
        LvAlign.TopRight or LvAlign.BottomRight or LvAlign.RightMid or LvAlign.OutTopRight or LvAlign.OutBottomRight
            or LvAlign.OutLeftTop or LvAlign.OutLeftMid or LvAlign.OutLeftBottom => Anchor.End,
        _ => Anchor.Start,
    };

    private static Anchor Vertical(LvAlign align) => align switch
    {
        LvAlign.LeftMid or LvAlign.RightMid or LvAlign.Center or LvAlign.OutLeftMid or LvAlign.OutRightMid => Anchor.Middle,
        LvAlign.BottomLeft or LvAlign.BottomMid or LvAlign.BottomRight or LvAlign.OutLeftBottom or LvAlign.OutRightBottom
            or LvAlign.OutTopLeft or LvAlign.OutTopMid or LvAlign.OutTopRight => Anchor.End,
        _ => Anchor.Start,
    };
}

/// <summary>A rectangle in preview pixels. Kept free of any UI toolkit so the core stays testable.</summary>
public readonly record struct Rect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;

    public bool Contains(double x, double y) => x >= X && x < Right && y >= Y && y < Bottom;

    public override string ToString() => $"{X:0},{Y:0} {Width:0}x{Height:0}";
}

/// <summary>Which part of the selection footprint the pointer grabbed.</summary>
[Flags]
public enum ResizeEdge
{
    None = 0,
    Left = 1,
    Top = 2,
    Right = 4,
    Bottom = 8,
    TopLeft = Top | Left,
    TopRight = Top | Right,
    BottomLeft = Bottom | Left,
    BottomRight = Bottom | Right,
}

/// <summary>Resizing a rectangle by dragging one of its edges or corners.</summary>
public static class ResizeMath
{
    /// <summary>The smallest size a drag can shrink a widget to.</summary>
    public const double MinimumSize = 4;

    /// <summary>
    /// Applies a drag of (<paramref name="dx"/>, <paramref name="dy"/>) on <paramref name="edge"/>.
    /// The opposite edge stays put; the size never drops below <see cref="MinimumSize"/>.
    /// </summary>
    public static Rect Apply(Rect start, ResizeEdge edge, double dx, double dy)
    {
        var left = start.X;
        var top = start.Y;
        var right = start.Right;
        var bottom = start.Bottom;

        if (edge.HasFlag(ResizeEdge.Left)) left = Math.Min(left + dx, right - MinimumSize);
        if (edge.HasFlag(ResizeEdge.Right)) right = Math.Max(right + dx, left + MinimumSize);
        if (edge.HasFlag(ResizeEdge.Top)) top = Math.Min(top + dy, bottom - MinimumSize);
        if (edge.HasFlag(ResizeEdge.Bottom)) bottom = Math.Max(bottom + dy, top + MinimumSize);

        return new Rect(left, top, right - left, bottom - top);
    }

    /// <summary>Rounds to the nearest multiple of <paramref name="grid"/>; a grid of 1 or less is off.</summary>
    public static double Snap(double value, double grid) => grid <= 1 ? Math.Round(value) : Math.Round(value / grid) * grid;
}
