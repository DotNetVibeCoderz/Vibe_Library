using Lvgl.Interop;

namespace Lvgl.Widgets;

/// <summary>
/// Full-size pages laid out on a grid that the user swipes between - a phone-style home screen.
/// </summary>
/// <remarks>
/// Each tile states which directions it may be left in, so a grid with gaps or one-way paths can
/// be built. <see cref="LvObject.ValueChanged"/> fires after a swipe settles on a new tile.
/// </remarks>
public sealed class LvTileView : LvObject
{
    private readonly List<LvTile> _tiles = [];

    /// <summary>Creates a tile view on <paramref name="parent"/>.</summary>
    public LvTileView(LvObject? parent) : base(LvglNative.lv_tileview_create(ResolveParent(parent))) { }

    /// <summary>The tiles in the order they were added.</summary>
    public IReadOnlyList<LvTile> Tiles => _tiles;

    /// <summary>
    /// Adds a tile at a grid position.
    /// </summary>
    /// <param name="column">Column, from 0.</param>
    /// <param name="row">Row, from 0.</param>
    /// <param name="allowedDirections">Directions a swipe may leave this tile in.</param>
    public LvTile AddTile(int column, int row, LvDir allowedDirections = LvDir.All)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(column);
        ArgumentOutOfRangeException.ThrowIfNegative(row);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(column, byte.MaxValue);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(row, byte.MaxValue);

        var tile = new LvTile(LvglNative.lv_tileview_add_tile(Handle, (byte)column, (byte)row, (int)allowedDirections), column, row);
        _tiles.Add(tile);
        return tile;
    }

    /// <summary>Scrolls to the tile at a grid position.</summary>
    public void ShowTile(int column, int row, bool animate = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(column);
        ArgumentOutOfRangeException.ThrowIfNegative(row);
        LvglNative.lv_tileview_set_tile_by_index(Handle, (uint)column, (uint)row, animate);
    }

    /// <summary>
    /// The tile currently in view. Null until LVGL has settled on one - after
    /// <see cref="ShowTile"/> or the first swipe - because LVGL does not mark the first tile
    /// active when it is added.
    /// </summary>
    public LvTile? ActiveTile => FromHandle(LvglNative.lv_tileview_get_tile_active(Handle)) as LvTile;

    protected override void OnDeleted() => _tiles.Clear();
}

/// <summary>One page of an <see cref="LvTileView"/>; an ordinary container for its widgets.</summary>
public sealed class LvTile : LvObject
{
    internal LvTile(nint handle, int column, int row) : base(handle)
    {
        Column = column;
        Row = row;
    }

    /// <summary>Grid column.</summary>
    public int Column { get; }

    /// <summary>Grid row.</summary>
    public int Row { get; }
}
