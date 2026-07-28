using Lvgl.Drawing;
using Lvgl.Interop;

namespace Lvgl.Widgets;

/// <summary>
/// A line, bar or scatter chart.
/// </summary>
/// <remarks>
/// The realtime pattern this wrapper is built for is: create the chart once, set
/// <see cref="UpdateMode"/> to <see cref="LvChartUpdateMode.Shift"/>, then call
/// <see cref="LvChartSeries.AddPoint"/> as samples arrive. LVGL keeps the ring buffer internally,
/// so the managed side never allocates per sample.
/// </remarks>
public sealed class LvChart : LvObject
{
    private readonly List<LvChartSeries> _series = [];

    /// <summary>Creates a chart on <paramref name="parent"/>.</summary>
    public LvChart(LvObject? parent) : base(LvglNative.lv_chart_create(ResolveParent(parent))) { }

    /// <summary>Plot style.</summary>
    public LvChartType Type
    {
        set => LvglNative.lv_chart_set_type(Handle, (int)value);
    }

    /// <summary>Number of samples kept per series. Defaults to 10.</summary>
    public int PointCount
    {
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            LvglNative.lv_chart_set_point_count(Handle, (uint)value);
        }
    }

    /// <summary>How new points are inserted.</summary>
    public LvChartUpdateMode UpdateMode
    {
        set => LvglNative.lv_chart_set_update_mode(Handle, (int)value);
    }

    /// <summary>Series added to this chart.</summary>
    public IReadOnlyList<LvChartSeries> Series => _series;

    /// <summary>Sets the value range of one axis.</summary>
    public void SetRange(LvChartAxis axis, int minimum, int maximum)
    {
        if (minimum > maximum) throw new ArgumentException("minimum must not exceed maximum.", nameof(minimum));
        LvglNative.lv_chart_set_range(Handle, (int)axis, minimum, maximum);
    }

    /// <summary>Sets how many division lines the grid draws.</summary>
    public void SetDivisionLines(int horizontal, int vertical) =>
        LvglNative.lv_chart_set_div_line_count(Handle, (byte)horizontal, (byte)vertical);

    /// <summary>Adds a data series.</summary>
    public LvChartSeries AddSeries(LvColor color, LvChartAxis axis = LvChartAxis.PrimaryY)
    {
        var handle = LvglNative.lvn_chart_add_series(Handle, color.Rgb, (int)axis);
        if (handle == 0) throw new LvglException("lv_chart_add_series failed.");

        var series = new LvChartSeries(this, handle, color);
        _series.Add(series);
        return series;
    }

    /// <summary>Removes a series and its data.</summary>
    public void RemoveSeries(LvChartSeries series)
    {
        ArgumentNullException.ThrowIfNull(series);
        if (!_series.Remove(series)) return;

        LvglNative.lv_chart_remove_series(Handle, series.Handle);
    }

    /// <summary>Forces a redraw after bulk changes.</summary>
    public void Refresh() => LvglNative.lv_chart_refresh(Handle);

    protected override void OnDeleted() => _series.Clear();
}

/// <summary>One data series inside an <see cref="LvChart"/>.</summary>
public sealed class LvChartSeries(LvChart chart, nint handle, LvColor color)
{
    /// <summary>The chart this series belongs to.</summary>
    public LvChart Chart { get; } = chart;

    /// <summary>Native <c>lv_chart_series_t*</c>.</summary>
    public nint Handle { get; } = handle;

    /// <summary>Colour the series is drawn in.</summary>
    public LvColor Color { get; } = color;

    /// <summary>
    /// Appends a sample. With <see cref="LvChartUpdateMode.Shift"/> the oldest point drops off,
    /// which is the behaviour wanted for a live telemetry trace.
    /// </summary>
    public void AddPoint(int value) => LvglNative.lv_chart_set_next_value(Chart.Handle, Handle, value);

    /// <summary>Sets every point of the series to one value - useful to prime a fresh chart.</summary>
    public void Fill(int value) => LvglNative.lv_chart_set_all_value(Chart.Handle, Handle, value);
}
