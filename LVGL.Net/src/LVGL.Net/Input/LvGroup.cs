using Lvgl.Interop;
using Lvgl.Widgets;

namespace Lvgl.Input;

/// <summary>
/// A focus group: the set of widgets a keyboard or encoder can move between.
/// </summary>
/// <remarks>
/// <para>
/// LVGL routes key input through a group rather than to widgets directly. A keypad device with no
/// group has nowhere to deliver its input, so typing does nothing at all - no error, no warning.
/// <see cref="LvglApplication"/> therefore creates one automatically whenever
/// <see cref="LvglOptions.EnableKeyboard"/> is set.
/// </para>
/// <para>
/// Making a group the default is what gets widgets into it: LVGL adds every focusable widget to
/// the default group as it is constructed. That has to happen <b>before</b> the widgets are
/// created, which is why the application sets it up during construction rather than on demand.
/// </para>
/// </remarks>
public sealed class LvGroup : IDisposable
{
    private nint _handle;

    private LvGroup(nint handle)
    {
        if (handle == 0) throw new LvglException("lv_group_create failed.");
        _handle = handle;
    }

    /// <summary>Native <c>lv_group_t*</c>.</summary>
    public nint Handle => _handle != 0
        ? _handle
        : throw new ObjectDisposedException(nameof(LvGroup));

    /// <summary>Creates an empty group.</summary>
    public static LvGroup Create()
    {
        LvglRuntime.EnsureUiThread();
        return new LvGroup(LvglNative.lv_group_create());
    }

    /// <summary>
    /// Creates a group and makes it the default, so widgets created afterwards join it
    /// automatically.
    /// </summary>
    public static LvGroup CreateDefault()
    {
        var group = Create();
        group.MakeDefault();
        return group;
    }

    /// <summary>
    /// Makes this the group new focusable widgets join on creation. Widgets that already exist
    /// are unaffected - add them with <see cref="Add"/>.
    /// </summary>
    public void MakeDefault()
    {
        LvglRuntime.EnsureUiThread();
        LvglNative.lv_group_set_default(Handle);
    }

    /// <summary>Adds an existing widget to the group.</summary>
    public void Add(LvObject widget)
    {
        ArgumentNullException.ThrowIfNull(widget);

        LvglRuntime.EnsureUiThread();
        LvglNative.lv_group_add_obj(Handle, widget.Handle);
    }

    /// <summary>
    /// Removes a widget from whatever group it belongs to. LVGL does this itself when a widget is
    /// deleted, so this is only needed to take a live widget out of the focus order.
    /// </summary>
    public static void Remove(LvObject widget)
    {
        ArgumentNullException.ThrowIfNull(widget);

        LvglRuntime.EnsureUiThread();
        LvglNative.lv_group_remove_obj(widget.Handle);
    }

    /// <summary>Moves focus to a widget, which must already be in a group.</summary>
    public static void Focus(LvObject widget)
    {
        ArgumentNullException.ThrowIfNull(widget);

        LvglRuntime.EnsureUiThread();
        LvglNative.lv_group_focus_obj(widget.Handle);
    }

    public void Dispose()
    {
        if (_handle == 0) return;

        // Clear the default first: LVGL nulls it when the default group is deleted, but doing it
        // here keeps the managed and native views of "what is default" in step.
        if (LvglNative.lv_group_get_default() == _handle) LvglNative.lv_group_set_default(0);

        LvglNative.lv_group_delete(_handle);
        _handle = 0;
    }
}
