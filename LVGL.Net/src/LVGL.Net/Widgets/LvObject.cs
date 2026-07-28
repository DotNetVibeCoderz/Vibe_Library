using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Lvgl.Drawing;
using Lvgl.Events;
using Lvgl.Interop;
using Lvgl.Styling;

namespace Lvgl.Widgets;

/// <summary>
/// Managed wrapper around an <c>lv_obj_t</c> - the base of every LVGL widget.
/// </summary>
/// <remarks>
/// <para>
/// <b>Lifetime.</b> LVGL owns the object tree: deleting a parent deletes its children. This class
/// therefore does not implement <see cref="IDisposable"/>, because "disposing" a child that its
/// parent already freed would be a double free. Call <see cref="Delete"/> when you genuinely want
/// the widget gone; otherwise let the screen or parent take it with them.
/// </para>
/// <para>
/// <b>Event dispatch.</b> Managed instances are found from a native pointer through a static
/// registry rather than a <see cref="GCHandle"/> stored in the object's user data. LVGL can fire
/// callbacks while it is tearing an object down, and a freed <see cref="GCHandle"/> read during
/// that window is undefined behaviour, whereas a missed dictionary lookup is merely a no-op.
/// Entries are removed when LVGL reports the delete event.
/// </para>
/// </remarks>
public unsafe class LvObject
{
    private static readonly ConcurrentDictionary<nint, LvObject> Registry = new();

    private Dictionary<LvEventCode, EventHandler<LvEventArgs>>? _handlers;
    private HashSet<LvEventCode>? _nativeSubscriptions;
    private nint _handle;

    /// <summary>Wraps an existing native object.</summary>
    protected internal LvObject(nint handle)
    {
        if (handle == 0) throw new LvglException($"{GetType().Name} could not be created (LVGL returned NULL).");

        _handle = handle;
        Registry[handle] = this;

        // Always know when LVGL frees the object so the registry does not accumulate dead entries
        // and so Handle stops handing out a dangling pointer.
        SubscribeNative(LvEventCode.Delete);
    }

    /// <summary>Creates a plain container object.</summary>
    /// <param name="parent">Parent widget; <see langword="null"/> parents to the active screen.</param>
    public LvObject(LvObject? parent) : this(LvglNative.lv_obj_create(ResolveParent(parent))) { }

    /// <summary>
    /// Native <c>lv_obj_t*</c>.
    /// </summary>
    /// <remarks>
    /// Throws once LVGL has deleted the object rather than returning zero. Derived widgets pass
    /// this straight to LVGL, and a null <c>lv_obj_t*</c> is dereferenced without a check on the C
    /// side - so handing out zero here turns a stale reference into an access violation instead of
    /// an exception. Test <see cref="IsAlive"/> when a reference may outlive its widget.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The widget has been deleted.</exception>
    public nint Handle => _handle != 0
        ? _handle
        : throw new ObjectDisposedException(GetType().Name, "The LVGL object has been deleted.");

    /// <summary>False once LVGL has freed the underlying object.</summary>
    public bool IsAlive => _handle != 0;

    /// <summary>Optional identifier used by the designer and code generator. Not sent to LVGL.</summary>
    public string? Name { get; set; }

    /// <summary>Returns the managed wrapper for a native handle, if one exists.</summary>
    public static LvObject? FromHandle(nint handle) =>
        handle != 0 && Registry.TryGetValue(handle, out var obj) ? obj : null;

    internal static nint ResolveParent(LvObject? parent) =>
        parent?.Handle ?? LvglNative.lv_screen_active();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private nint RequireHandle() => Handle;

    #region Geometry

    /// <summary>X position relative to the parent's content area.</summary>
    public int X
    {
        get => LvglNative.lv_obj_get_x(RequireHandle());
        set => LvglNative.lv_obj_set_x(RequireHandle(), value);
    }

    /// <summary>Y position relative to the parent's content area.</summary>
    public int Y
    {
        get => LvglNative.lv_obj_get_y(RequireHandle());
        set => LvglNative.lv_obj_set_y(RequireHandle(), value);
    }

    /// <summary>
    /// Width in pixels. Accepts <see cref="LvCoord.Percent"/> and <see cref="LvCoord.SizeContent"/>.
    /// </summary>
    public int Width
    {
        get => LvglNative.lv_obj_get_width(RequireHandle());
        set => LvglNative.lv_obj_set_width(RequireHandle(), value);
    }

    /// <summary>
    /// Height in pixels. Accepts <see cref="LvCoord.Percent"/> and <see cref="LvCoord.SizeContent"/>.
    /// </summary>
    public int Height
    {
        get => LvglNative.lv_obj_get_height(RequireHandle());
        set => LvglNative.lv_obj_set_height(RequireHandle(), value);
    }

    /// <summary>Sets position in one call.</summary>
    public LvObject SetPosition(int x, int y)
    {
        LvglNative.lv_obj_set_pos(RequireHandle(), x, y);
        return this;
    }

    /// <summary>Sets size in one call.</summary>
    public LvObject SetSize(int width, int height)
    {
        LvglNative.lv_obj_set_size(RequireHandle(), width, height);
        return this;
    }

    /// <summary>Aligns inside the parent, with an optional offset.</summary>
    public LvObject Align(LvAlign align, int xOffset = 0, int yOffset = 0)
    {
        LvglNative.lv_obj_align(RequireHandle(), (int)align, xOffset, yOffset);
        return this;
    }

    /// <summary>Aligns relative to another object.</summary>
    public LvObject AlignTo(LvObject reference, LvAlign align, int xOffset = 0, int yOffset = 0)
    {
        ArgumentNullException.ThrowIfNull(reference);
        LvglNative.lv_obj_align_to(RequireHandle(), reference.RequireHandle(), (int)align, xOffset, yOffset);
        return this;
    }

    /// <summary>Centres inside the parent.</summary>
    public LvObject Center()
    {
        LvglNative.lv_obj_center(RequireHandle());
        return this;
    }

    /// <summary>Marks the widget for redraw.</summary>
    public void Invalidate() => LvglNative.lv_obj_invalidate(RequireHandle());

    /// <summary>
    /// Resolves pending layout immediately.
    /// </summary>
    /// <remarks>
    /// LVGL normally computes layout during the next refresh, so <see cref="Width"/> and
    /// <see cref="Height"/> still report the previous values right after a size or flex change -
    /// and report 0 for a widget that has never been laid out. Call this when the computed size is
    /// needed before the next frame, which is typically the case when sizes were given as
    /// percentages or <see cref="LvCoord.SizeContent"/>.
    /// </remarks>
    public void UpdateLayout() => LvglNative.lv_obj_update_layout(RequireHandle());

    #endregion

    #region Flags and states

    /// <summary>Hides the widget without removing it from the tree.</summary>
    public bool IsHidden
    {
        get => HasFlag(LvObjFlag.Hidden);
        set => SetFlag(LvObjFlag.Hidden, value);
    }

    /// <summary>Whether the widget reacts to pointer input.</summary>
    public bool IsClickable
    {
        get => HasFlag(LvObjFlag.Clickable);
        set => SetFlag(LvObjFlag.Clickable, value);
    }

    /// <summary>Whether the widget scrolls its content.</summary>
    public bool IsScrollable
    {
        get => HasFlag(LvObjFlag.Scrollable);
        set => SetFlag(LvObjFlag.Scrollable, value);
    }

    /// <summary>Whether the widget is in the disabled state.</summary>
    public bool IsDisabled
    {
        get => HasState(LvState.Disabled);
        set => SetState(LvState.Disabled, value);
    }

    /// <summary>Whether the widget is in the checked state (toggles, switches, checkboxes).</summary>
    public bool IsChecked
    {
        get => HasState(LvState.Checked);
        set => SetState(LvState.Checked, value);
    }

    public bool HasFlag(LvObjFlag flag) => LvglNative.lv_obj_has_flag(RequireHandle(), (uint)flag);

    public LvObject SetFlag(LvObjFlag flag, bool enabled)
    {
        var handle = RequireHandle();
        if (enabled) LvglNative.lv_obj_add_flag(handle, (uint)flag);
        else LvglNative.lv_obj_remove_flag(handle, (uint)flag);
        return this;
    }

    public bool HasState(LvState state) => LvglNative.lv_obj_has_state(RequireHandle(), (ushort)state);

    public LvObject SetState(LvState state, bool enabled)
    {
        var handle = RequireHandle();
        if (enabled) LvglNative.lv_obj_add_state(handle, (ushort)state);
        else LvglNative.lv_obj_remove_state(handle, (ushort)state);
        return this;
    }

    #endregion

    #region Layout

    /// <summary>Turns the widget into a flex container.</summary>
    public LvObject SetFlexFlow(LvFlexFlow flow)
    {
        LvglNative.lv_obj_set_flex_flow(RequireHandle(), (int)flow);
        return this;
    }

    /// <summary>Sets how flex children are distributed.</summary>
    public LvObject SetFlexAlign(LvFlexAlign main, LvFlexAlign cross, LvFlexAlign track)
    {
        LvglNative.lv_obj_set_flex_align(RequireHandle(), (int)main, (int)cross, (int)track);
        return this;
    }

    /// <summary>Sets scrollbar visibility.</summary>
    public LvObject SetScrollbarMode(LvScrollbarMode mode)
    {
        LvglNative.lv_obj_set_scrollbar_mode(RequireHandle(), (int)mode);
        return this;
    }

    #endregion

    #region Styling

    /// <summary>Combines a part and a state into the selector LVGL's style setters take.</summary>
    public static uint Selector(LvPart part = LvPart.Main, LvState state = LvState.Default) =>
        (uint)part | (uint)state;

    public LvObject SetBackgroundColor(LvColor color, LvPart part = LvPart.Main, LvState state = LvState.Default)
    {
        LvglNative.lvn_obj_set_style_bg_color(RequireHandle(), color.Rgb, Selector(part, state));
        return this;
    }

    /// <summary>Sets a vertical gradient's end colour. The start colour is the background colour.</summary>
    public LvObject SetBackgroundGradientColor(LvColor color, LvPart part = LvPart.Main, LvState state = LvState.Default)
    {
        LvglNative.lvn_obj_set_style_bg_grad_color(RequireHandle(), color.Rgb, Selector(part, state));
        return this;
    }

    public LvObject SetTextColor(LvColor color, LvPart part = LvPart.Main, LvState state = LvState.Default)
    {
        LvglNative.lvn_obj_set_style_text_color(RequireHandle(), color.Rgb, Selector(part, state));
        return this;
    }

    public LvObject SetBorderColor(LvColor color, LvPart part = LvPart.Main, LvState state = LvState.Default)
    {
        LvglNative.lvn_obj_set_style_border_color(RequireHandle(), color.Rgb, Selector(part, state));
        return this;
    }

    public LvObject SetLineColor(LvColor color, LvPart part = LvPart.Main, LvState state = LvState.Default)
    {
        LvglNative.lvn_obj_set_style_line_color(RequireHandle(), color.Rgb, Selector(part, state));
        return this;
    }

    public LvObject SetArcColor(LvColor color, LvPart part = LvPart.Main, LvState state = LvState.Default)
    {
        LvglNative.lvn_obj_set_style_arc_color(RequireHandle(), color.Rgb, Selector(part, state));
        return this;
    }

    public LvObject SetShadowColor(LvColor color, LvPart part = LvPart.Main, LvState state = LvState.Default)
    {
        LvglNative.lvn_obj_set_style_shadow_color(RequireHandle(), color.Rgb, Selector(part, state));
        return this;
    }

    /// <summary>Background opacity, 0 (transparent) to 255 (opaque).</summary>
    public LvObject SetBackgroundOpacity(byte opacity, LvPart part = LvPart.Main, LvState state = LvState.Default)
    {
        LvglNative.lv_obj_set_style_bg_opa(RequireHandle(), opacity, Selector(part, state));
        return this;
    }

    /// <summary>Opacity of the whole widget, 0 to 255.</summary>
    public LvObject SetOpacity(byte opacity, LvPart part = LvPart.Main, LvState state = LvState.Default)
    {
        LvglNative.lv_obj_set_style_opa(RequireHandle(), opacity, Selector(part, state));
        return this;
    }

    public LvObject SetRadius(int radius, LvPart part = LvPart.Main, LvState state = LvState.Default)
    {
        LvglNative.lv_obj_set_style_radius(RequireHandle(), radius, Selector(part, state));
        return this;
    }

    public LvObject SetBorderWidth(int width, LvPart part = LvPart.Main, LvState state = LvState.Default)
    {
        LvglNative.lv_obj_set_style_border_width(RequireHandle(), width, Selector(part, state));
        return this;
    }

    public LvObject SetPadding(int padding, LvPart part = LvPart.Main, LvState state = LvState.Default)
    {
        LvglNative.lvn_obj_set_style_pad_all(RequireHandle(), padding, Selector(part, state));
        return this;
    }

    /// <summary>Gap between rows and columns of a flex or grid layout.</summary>
    public LvObject SetGap(int rowGap, int columnGap, LvPart part = LvPart.Main, LvState state = LvState.Default)
    {
        var handle = RequireHandle();
        var selector = Selector(part, state);
        LvglNative.lv_obj_set_style_pad_row(handle, rowGap, selector);
        LvglNative.lv_obj_set_style_pad_column(handle, columnGap, selector);
        return this;
    }

    public LvObject SetTextAlign(LvTextAlign align, LvPart part = LvPart.Main, LvState state = LvState.Default)
    {
        LvglNative.lv_obj_set_style_text_align(RequireHandle(), (int)align, Selector(part, state));
        return this;
    }

    public LvObject SetLineWidth(int width, LvPart part = LvPart.Main, LvState state = LvState.Default)
    {
        LvglNative.lv_obj_set_style_line_width(RequireHandle(), width, Selector(part, state));
        return this;
    }

    public LvObject SetArcWidth(int width, LvPart part = LvPart.Main, LvState state = LvState.Default)
    {
        LvglNative.lv_obj_set_style_arc_width(RequireHandle(), width, Selector(part, state));
        return this;
    }

    /// <summary>
    /// Selects one of the built-in Montserrat fonts. Sizes not compiled into the native library
    /// fall back to the default font rather than failing.
    /// </summary>
    public LvObject SetFontSize(int size, LvPart part = LvPart.Main, LvState state = LvState.Default)
    {
        var font = LvglNative.lvn_font_montserrat((uint)size);
        if (font != 0) LvglNative.lvn_obj_set_style_text_font(RequireHandle(), font, Selector(part, state));
        return this;
    }

    /// <summary>Applies a shared <see cref="LvStyle"/>.</summary>
    public LvObject AddStyle(LvStyle style, LvPart part = LvPart.Main, LvState state = LvState.Default)
    {
        ArgumentNullException.ThrowIfNull(style);
        LvglNative.lv_obj_add_style(RequireHandle(), style.Handle, Selector(part, state));
        return this;
    }

    /// <summary>Removes a previously applied shared style.</summary>
    public LvObject RemoveStyle(LvStyle style, LvPart part = LvPart.Main, LvState state = LvState.Default)
    {
        ArgumentNullException.ThrowIfNull(style);
        LvglNative.lv_obj_remove_style(RequireHandle(), style.Handle, Selector(part, state));
        return this;
    }

    #endregion

    #region Tree

    /// <summary>Parent widget, or <see langword="null"/> for a screen.</summary>
    public LvObject? Parent
    {
        get => FromHandle(LvglNative.lv_obj_get_parent(RequireHandle()));
        set => LvglNative.lv_obj_set_parent(RequireHandle(), ResolveParent(value));
    }

    /// <summary>Number of direct children.</summary>
    public int ChildCount => (int)LvglNative.lv_obj_get_child_count(RequireHandle());

    /// <summary>
    /// Child at <paramref name="index"/>. Returns <see langword="null"/> when the child was created
    /// by native code and has no managed wrapper (for example widgets built by an LVGL demo).
    /// </summary>
    public LvObject? GetChild(int index) => FromHandle(LvglNative.lv_obj_get_child(RequireHandle(), index));

    /// <summary>Enumerates children that have a managed wrapper.</summary>
    public IEnumerable<LvObject> Children()
    {
        var count = ChildCount;
        for (var i = 0; i < count; i++)
        {
            if (GetChild(i) is { } child) yield return child;
        }
    }

    /// <summary>Deletes all children, keeping this object.</summary>
    public void Clear() => LvglNative.lv_obj_clean(RequireHandle());

    /// <summary>
    /// Deletes the object and its children. The instance becomes unusable; <see cref="IsAlive"/>
    /// turns false once LVGL confirms the deletion.
    /// </summary>
    public void Delete()
    {
        var handle = _handle;
        if (handle == 0) return;
        LvglNative.lv_obj_delete(handle);
    }

    #endregion

    #region Events

    /// <summary>Raised when the widget is clicked.</summary>
    public event EventHandler<LvEventArgs> Clicked
    {
        add => AddHandler(LvEventCode.Clicked, value);
        remove => RemoveHandler(LvEventCode.Clicked, value);
    }

    /// <summary>Raised when the widget's value changes (slider dragged, switch toggled, ...).</summary>
    public event EventHandler<LvEventArgs> ValueChanged
    {
        add => AddHandler(LvEventCode.ValueChanged, value);
        remove => RemoveHandler(LvEventCode.ValueChanged, value);
    }

    /// <summary>Raised when LVGL deletes the widget.</summary>
    public event EventHandler<LvEventArgs> Deleted
    {
        add => AddHandler(LvEventCode.Delete, value);
        remove => RemoveHandler(LvEventCode.Delete, value);
    }

    /// <summary>Subscribes to any LVGL event code.</summary>
    public void AddHandler(LvEventCode code, EventHandler<LvEventArgs> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        _handlers ??= [];
        _handlers[code] = _handlers.TryGetValue(code, out var existing)
            ? (EventHandler<LvEventArgs>)Delegate.Combine(existing, handler)
            : handler;

        SubscribeNative(code);
    }

    /// <summary>Unsubscribes a previously added handler.</summary>
    public void RemoveHandler(LvEventCode code, EventHandler<LvEventArgs> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (_handlers is null || !_handlers.TryGetValue(code, out var existing)) return;

        // The native subscription stays registered: LVGL has no "remove one callback" that is
        // cheap to target, and an unsubscribed code simply dispatches to nothing.
        var remaining = (EventHandler<LvEventArgs>?)Delegate.Remove(existing, handler);
        if (remaining is null) _handlers.Remove(code);
        else _handlers[code] = remaining;
    }

    private void SubscribeNative(LvEventCode code)
    {
        _nativeSubscriptions ??= [];
        if (!_nativeSubscriptions.Add(code)) return;

        var nativeCode = LvEventCodes.ToNative(code);
        if (nativeCode < 0)
        {
            _nativeSubscriptions.Remove(code);
            throw new LvglException($"The loaded LVGL build does not support the {code} event.");
        }

        LvglNative.lv_obj_add_event_cb(RequireHandle(), &EventThunk, nativeCode, null);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void EventThunk(nint e)
    {
        try
        {
            // current_target, not target: with LV_OBJ_FLAG_EVENT_BUBBLE a child's event is
            // delivered to the ancestor that subscribed, and the subscriber is what callers expect.
            var handle = LvglNative.lv_event_get_current_target(e);
            if (handle == 0 || !Registry.TryGetValue(handle, out var self)) return;

            if (!LvEventCodes.TryFromNative(LvglNative.lv_event_get_code(e), out var code)) return;

            self.Dispatch(code, new LvEventArgs(code, e, handle));
        }
        catch
        {
            // A managed exception must not unwind into LVGL's C stack. Handlers are expected to
            // deal with their own failures; see LvglApplication.Post for work that can throw.
        }
    }

    private void Dispatch(LvEventCode code, LvEventArgs args)
    {
        if (_handlers is not null && _handlers.TryGetValue(code, out var handler))
        {
            handler(this, args);
        }

        if (code != LvEventCode.Delete) return;

        // LVGL is about to free the object: drop the registry entry so a later callback for a
        // recycled address cannot reach this instance, and make Handle stop returning a stale
        // pointer.
        Registry.TryRemove(_handle, out _);
        _handle = 0;
        OnDeleted();
    }

    /// <summary>Called after LVGL has deleted the underlying object.</summary>
    protected virtual void OnDeleted() { }

    #endregion

    public override string ToString() =>
        Name is null ? $"{GetType().Name}(0x{_handle:X})" : $"{GetType().Name} '{Name}'";
}
