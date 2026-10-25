using Lvgl.Interop;

namespace Lvgl.Widgets;

/// <summary>
/// Pages behind a row of tabs. Each <see cref="AddTab"/> returns the page to put widgets on.
/// </summary>
/// <remarks>
/// <see cref="LvObject.ValueChanged"/> fires when the user switches tab; read
/// <see cref="ActiveTab"/> in the handler.
/// </remarks>
public sealed class LvTabView : LvObject
{
    private readonly List<LvTabPage> _tabs = [];

    /// <summary>Creates a tab view on <paramref name="parent"/>.</summary>
    public LvTabView(LvObject? parent) : base(LvglNative.lv_tabview_create(ResolveParent(parent))) { }

    /// <summary>The pages, in tab order.</summary>
    public IReadOnlyList<LvTabPage> Tabs => _tabs;

    /// <summary>Number of tabs.</summary>
    public int TabCount => (int)LvglNative.lv_tabview_get_tab_count(Handle);

    /// <summary>Adds a tab and returns its page.</summary>
    public LvTabPage AddTab(string title)
    {
        var page = new LvTabPage(this, LvglNative.lv_tabview_add_tab(Handle, title ?? string.Empty), _tabs.Count);
        _tabs.Add(page);
        return page;
    }

    /// <summary>Index of the visible tab. Assigning switches without animation.</summary>
    public int ActiveTab
    {
        get => (int)LvglNative.lv_tabview_get_tab_active(Handle);
        set => SetActiveTab(value, animate: false);
    }

    /// <summary>Switches tab, optionally sliding the content across.</summary>
    public void SetActiveTab(int index, bool animate)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, TabCount);
        LvglNative.lv_tabview_set_active(Handle, (uint)index, animate);
    }

    /// <summary>Renames a tab.</summary>
    public void SetTabTitle(int index, string title)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, TabCount);
        LvglNative.lv_tabview_set_tab_text(Handle, (uint)index, title ?? string.Empty);
    }

    /// <summary>Which edge the tab bar sits on: <see cref="LvDir.Top"/> (default), Bottom, Left or Right.</summary>
    public LvDir TabBarPosition
    {
        set
        {
            if (value is not (LvDir.Top or LvDir.Bottom or LvDir.Left or LvDir.Right))
            {
                throw new ArgumentOutOfRangeException(nameof(value), "The tab bar goes on one edge: Top, Bottom, Left or Right.");
            }

            LvglNative.lv_tabview_set_tab_bar_position(Handle, (int)value);
        }
    }

    /// <summary>Height of a horizontal tab bar, or width of a vertical one, in pixels.</summary>
    public int TabBarSize
    {
        set => LvglNative.lv_tabview_set_tab_bar_size(Handle, value);
    }

    /// <summary>The bar holding the tab buttons, for styling.</summary>
    public LvObject TabBar => Wrap(LvglNative.lv_tabview_get_tab_bar(Handle));

    protected override void OnDeleted() => _tabs.Clear();
}

/// <summary>One page of an <see cref="LvTabView"/>; an ordinary container for the tab's widgets.</summary>
public sealed class LvTabPage : LvObject
{
    internal LvTabPage(LvTabView owner, nint handle, int index) : base(handle)
    {
        Owner = owner;
        Index = index;
    }

    /// <summary>The tab view this page belongs to.</summary>
    public LvTabView Owner { get; }

    /// <summary>Position of the tab.</summary>
    public int Index { get; }

    /// <summary>Brings this page to the front.</summary>
    public void Activate(bool animate = false) => Owner.SetActiveTab(Index, animate);
}
