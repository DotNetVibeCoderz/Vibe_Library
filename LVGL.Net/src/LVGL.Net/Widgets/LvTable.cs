using Lvgl.Interop;

namespace Lvgl.Widgets;

/// <summary>A grid of text cells.</summary>
/// <remarks>
/// LVGL draws the whole table as one widget rather than a widget per cell, so it stays cheap at
/// hundreds of cells. Writing a cell beyond the current size grows the table.
/// </remarks>
public sealed class LvTable : LvObject
{
    /// <summary>Creates a table on <paramref name="parent"/>.</summary>
    public LvTable(LvObject? parent) : base(LvglNative.lv_table_create(ResolveParent(parent))) { }

    /// <summary>Number of rows.</summary>
    public int RowCount
    {
        get => (int)LvglNative.lv_table_get_row_count(Handle);
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            LvglNative.lv_table_set_row_count(Handle, (uint)value);
        }
    }

    /// <summary>Number of columns.</summary>
    public int ColumnCount
    {
        get => (int)LvglNative.lv_table_get_column_count(Handle);
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            LvglNative.lv_table_set_column_count(Handle, (uint)value);
        }
    }

    /// <summary>Gets or sets the text of a cell.</summary>
    public string this[int row, int column]
    {
        get
        {
            CheckCell(row, column, mustExist: true);
            return LvglNative.ReadUtf8(LvglNative.lv_table_get_cell_value(Handle, (uint)row, (uint)column));
        }
        set
        {
            CheckCell(row, column, mustExist: false);
            LvglNative.lv_table_set_cell_value(Handle, (uint)row, (uint)column, value ?? string.Empty);
        }
    }

    /// <summary>Sets a column's width in pixels.</summary>
    public void SetColumnWidth(int column, int width)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(column);
        LvglNative.lv_table_set_column_width(Handle, (uint)column, width);
    }

    /// <summary>A column's width in pixels.</summary>
    public int GetColumnWidth(int column)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(column);
        return LvglNative.lv_table_get_column_width(Handle, (uint)column);
    }

    /// <summary>
    /// Replaces the contents with <paramref name="rows"/>, sizing the table to fit the widest row.
    /// </summary>
    public void SetRows(IReadOnlyList<IReadOnlyList<string>> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var columns = 0;
        foreach (var row in rows) columns = Math.Max(columns, row.Count);

        RowCount = rows.Count;
        ColumnCount = Math.Max(columns, 1);

        for (var r = 0; r < rows.Count; r++)
        {
            for (var c = 0; c < rows[r].Count; c++) this[r, c] = rows[r][c];
        }
    }

    private void CheckCell(int row, int column, bool mustExist)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(row);
        ArgumentOutOfRangeException.ThrowIfNegative(column);

        // Reading outside the table makes LVGL log and return its last cell; refuse instead.
        if (mustExist && (row >= RowCount || column >= ColumnCount))
        {
            throw new ArgumentOutOfRangeException(nameof(row), $"Cell ({row},{column}) is outside the {RowCount}x{ColumnCount} table.");
        }
    }
}
