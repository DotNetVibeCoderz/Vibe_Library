using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Lvgl.Interop;

/// <summary>
/// Raw P/Invoke surface for the <c>lvglnet</c> shared library (upstream LVGL v9 plus the
/// <c>lvn_*</c> shim from <c>native/shim</c>).
/// </summary>
/// <remarks>
/// <para>
/// Rules that keep this binding safe, and which any new entry point must follow:
/// </para>
/// <list type="bullet">
///   <item><description>
///     Never declare an import that passes or returns <c>lv_color_t</c> by value. It is a
///     three-byte struct, which the Windows x64 ABI passes indirectly and the SysV/AAPCS ABIs
///     pass in a register. Use the <c>lvn_*</c> colour wrappers, which take a packed
///     <c>0xRRGGBB</c> <see cref="uint"/> instead.
///   </description></item>
///   <item><description>
///     LVGL v9 coordinates and enums are 32-bit; enums are declared here as <see cref="int"/>
///     and the strongly typed managed enums are cast at the call site.
///   </description></item>
///   <item><description>
///     Callbacks are unmanaged function pointers, not delegates, so no marshalling stub runs on
///     the render path. The managed targets are <c>[UnmanagedCallersOnly]</c> statics that resolve
///     their instance from LVGL's user data or from a registry.
///   </description></item>
/// </list>
/// </remarks>
internal static unsafe partial class LvglNative
{
    internal const string Library = "lvglnet";

    static LvglNative() => LvglNativeLibrary.EnsureResolverInstalled();

    #region Shim - build information

    [LibraryImport(Library)]
    internal static partial uint lvn_abi_version();

    [LibraryImport(Library)]
    internal static partial uint lvn_lvgl_version();

    [LibraryImport(Library)]
    internal static partial uint lvn_color_depth();

    [LibraryImport(Library)]
    internal static partial int lvn_size_content();

    [LibraryImport(Library)]
    internal static partial int lvn_coord_max();

    /// <summary>LVGL's own <c>LV_PCT</c>, used to verify the managed reimplementation.</summary>
    [LibraryImport(Library)]
    internal static partial int lv_pct(int value);

    #endregion

    #region Core lifecycle

    [LibraryImport(Library)]
    internal static partial void lv_init();

    [LibraryImport(Library)]
    internal static partial void lv_deinit();

    [LibraryImport(Library)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool lv_is_initialized();

    [LibraryImport(Library)]
    internal static partial void lv_tick_inc(uint ms);

    /// <summary>Runs due timers and redraws; returns the suggested idle time in milliseconds.</summary>
    [LibraryImport(Library)]
    internal static partial uint lv_timer_handler();

    #endregion

    #region Display

    [LibraryImport(Library)]
    internal static partial nint lv_display_create(int horizontalRes, int verticalRes);

    [LibraryImport(Library)]
    internal static partial void lv_display_delete(nint display);

    [LibraryImport(Library)]
    internal static partial void lv_display_set_buffers(nint display, void* buf1, void* buf2, uint bufSizeBytes, int renderMode);

    [LibraryImport(Library)]
    internal static partial void lv_display_set_flush_cb(nint display, delegate* unmanaged[Cdecl]<nint, nint, byte*, void> flushCb);

    [LibraryImport(Library)]
    internal static partial void lv_display_flush_ready(nint display);

    [LibraryImport(Library)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool lv_display_flush_is_last(nint display);

    [LibraryImport(Library)]
    internal static partial void lv_display_set_user_data(nint display, void* userData);

    [LibraryImport(Library)]
    internal static partial void* lv_display_get_user_data(nint display);

    [LibraryImport(Library)]
    internal static partial nint lv_display_get_default();

    [LibraryImport(Library)]
    internal static partial int lv_display_get_horizontal_resolution(nint display);

    [LibraryImport(Library)]
    internal static partial int lv_display_get_vertical_resolution(nint display);

    [LibraryImport(Library)]
    internal static partial void lv_display_set_color_format(nint display, int colorFormat);

    [LibraryImport(Library)]
    internal static partial nint lv_screen_active();

    [LibraryImport(Library)]
    internal static partial void lv_screen_load(nint screen);

    [LibraryImport(Library)]
    internal static partial void lvn_area_get(nint area, int* x1, int* y1, int* x2, int* y2);

    #endregion

    #region Input devices

    [LibraryImport(Library)]
    internal static partial nint lv_indev_create();

    [LibraryImport(Library)]
    internal static partial void lv_indev_delete(nint indev);

    [LibraryImport(Library)]
    internal static partial void lv_indev_set_type(nint indev, int type);

    [LibraryImport(Library)]
    internal static partial void lv_indev_set_read_cb(nint indev, delegate* unmanaged[Cdecl]<nint, nint, void> readCb);

    [LibraryImport(Library)]
    internal static partial void lv_indev_set_display(nint indev, nint display);

    [LibraryImport(Library)]
    internal static partial void lvn_indev_data_set_pointer(nint data, int x, int y, byte pressed);

    [LibraryImport(Library)]
    internal static partial void lvn_indev_data_set_key(nint data, uint key, byte pressed);

    [LibraryImport(Library)]
    internal static partial void lvn_indev_data_set_encoder(nint data, int diff, byte pressed);

    /// <summary>
    /// Binds a keypad or encoder device to a focus group. Without this a key device has nowhere
    /// to deliver its input and typing silently does nothing.
    /// </summary>
    [LibraryImport(Library)]
    internal static partial void lv_indev_set_group(nint indev, nint group);

    #endregion

    #region Focus groups

    [LibraryImport(Library)]
    internal static partial nint lv_group_create();

    [LibraryImport(Library)]
    internal static partial void lv_group_delete(nint group);

    /// <summary>
    /// Sets the group new focusable widgets join automatically as they are created - see
    /// <c>lv_obj_class_init_obj</c>, which calls <c>lv_group_add_obj</c> for every widget whose
    /// class is group-default.
    /// </summary>
    [LibraryImport(Library)]
    internal static partial void lv_group_set_default(nint group);

    [LibraryImport(Library)]
    internal static partial nint lv_group_get_default();

    [LibraryImport(Library)]
    internal static partial void lv_group_add_obj(nint group, nint obj);

    [LibraryImport(Library)]
    internal static partial void lv_group_remove_obj(nint obj);

    [LibraryImport(Library)]
    internal static partial void lv_group_focus_obj(nint obj);

    #endregion

    #region Object tree

    [LibraryImport(Library)]
    internal static partial nint lv_obj_create(nint parent);

    [LibraryImport(Library)]
    internal static partial void lv_obj_delete(nint obj);

    [LibraryImport(Library)]
    internal static partial void lv_obj_clean(nint obj);

    [LibraryImport(Library)]
    internal static partial void lv_obj_set_parent(nint obj, nint parent);

    [LibraryImport(Library)]
    internal static partial nint lv_obj_get_parent(nint obj);

    [LibraryImport(Library)]
    internal static partial nint lv_obj_get_child(nint obj, int index);

    [LibraryImport(Library)]
    internal static partial uint lv_obj_get_child_count(nint obj);

    [LibraryImport(Library)]
    internal static partial void lv_obj_set_user_data(nint obj, void* userData);

    [LibraryImport(Library)]
    internal static partial void* lv_obj_get_user_data(nint obj);

    [LibraryImport(Library)]
    internal static partial void lv_obj_invalidate(nint obj);

    [LibraryImport(Library)]
    internal static partial void lv_obj_update_layout(nint obj);

    #endregion

    #region Geometry

    [LibraryImport(Library)]
    internal static partial void lv_obj_set_pos(nint obj, int x, int y);

    [LibraryImport(Library)]
    internal static partial void lv_obj_set_x(nint obj, int x);

    [LibraryImport(Library)]
    internal static partial void lv_obj_set_y(nint obj, int y);

    [LibraryImport(Library)]
    internal static partial void lv_obj_set_size(nint obj, int w, int h);

    [LibraryImport(Library)]
    internal static partial void lv_obj_set_width(nint obj, int w);

    [LibraryImport(Library)]
    internal static partial void lv_obj_set_height(nint obj, int h);

    [LibraryImport(Library)]
    internal static partial void lv_obj_align(nint obj, int align, int xOfs, int yOfs);

    [LibraryImport(Library)]
    internal static partial void lv_obj_align_to(nint obj, nint reference, int align, int xOfs, int yOfs);

    [LibraryImport(Library)]
    internal static partial void lv_obj_center(nint obj);

    [LibraryImport(Library)]
    internal static partial int lv_obj_get_x(nint obj);

    [LibraryImport(Library)]
    internal static partial int lv_obj_get_y(nint obj);

    [LibraryImport(Library)]
    internal static partial int lv_obj_get_width(nint obj);

    [LibraryImport(Library)]
    internal static partial int lv_obj_get_height(nint obj);

    #endregion

    #region Flags, states, layout

    [LibraryImport(Library)]
    internal static partial void lv_obj_add_flag(nint obj, uint flag);

    [LibraryImport(Library)]
    internal static partial void lv_obj_remove_flag(nint obj, uint flag);

    [LibraryImport(Library)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool lv_obj_has_flag(nint obj, uint flag);

    [LibraryImport(Library)]
    internal static partial void lv_obj_add_state(nint obj, ushort state);

    [LibraryImport(Library)]
    internal static partial void lv_obj_remove_state(nint obj, ushort state);

    [LibraryImport(Library)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool lv_obj_has_state(nint obj, ushort state);

    [LibraryImport(Library)]
    internal static partial void lv_obj_set_flex_flow(nint obj, int flow);

    [LibraryImport(Library)]
    internal static partial void lv_obj_set_flex_align(nint obj, int main, int cross, int track);

    [LibraryImport(Library)]
    internal static partial void lv_obj_set_scrollbar_mode(nint obj, int mode);

    #endregion

    #region Styles - local properties

    [LibraryImport(Library)]
    internal static partial void lv_obj_set_style_bg_opa(nint obj, byte opa, uint selector);

    [LibraryImport(Library)]
    internal static partial void lv_obj_set_style_opa(nint obj, byte opa, uint selector);

    [LibraryImport(Library)]
    internal static partial void lv_obj_set_style_radius(nint obj, int radius, uint selector);

    [LibraryImport(Library)]
    internal static partial void lv_obj_set_style_border_width(nint obj, int width, uint selector);

    // pad_all is static inline in LVGL's headers, so there is no symbol to import - it goes
    // through the shim like the other non-exported helpers.
    [LibraryImport(Library)]
    internal static partial void lvn_obj_set_style_pad_all(nint obj, int pad, uint selector);

    [LibraryImport(Library)]
    internal static partial void lv_obj_set_style_pad_row(nint obj, int pad, uint selector);

    [LibraryImport(Library)]
    internal static partial void lv_obj_set_style_pad_column(nint obj, int pad, uint selector);

    [LibraryImport(Library)]
    internal static partial void lv_obj_set_style_text_align(nint obj, int align, uint selector);

    [LibraryImport(Library)]
    internal static partial void lv_obj_set_style_line_width(nint obj, int width, uint selector);

    [LibraryImport(Library)]
    internal static partial void lv_obj_set_style_arc_width(nint obj, int width, uint selector);

    [LibraryImport(Library)]
    internal static partial void lv_obj_add_style(nint obj, nint style, uint selector);

    [LibraryImport(Library)]
    internal static partial void lv_obj_remove_style(nint obj, nint style, uint selector);

    // Colour and font setters live in the shim - see the class remarks.
    [LibraryImport(Library)]
    internal static partial void lvn_obj_set_style_bg_color(nint obj, uint rgb, uint selector);

    [LibraryImport(Library)]
    internal static partial void lvn_obj_set_style_bg_grad_color(nint obj, uint rgb, uint selector);

    [LibraryImport(Library)]
    internal static partial void lvn_obj_set_style_text_color(nint obj, uint rgb, uint selector);

    [LibraryImport(Library)]
    internal static partial void lvn_obj_set_style_border_color(nint obj, uint rgb, uint selector);

    [LibraryImport(Library)]
    internal static partial void lvn_obj_set_style_outline_color(nint obj, uint rgb, uint selector);

    [LibraryImport(Library)]
    internal static partial void lvn_obj_set_style_shadow_color(nint obj, uint rgb, uint selector);

    [LibraryImport(Library)]
    internal static partial void lvn_obj_set_style_line_color(nint obj, uint rgb, uint selector);

    [LibraryImport(Library)]
    internal static partial void lvn_obj_set_style_arc_color(nint obj, uint rgb, uint selector);

    [LibraryImport(Library)]
    internal static partial void lvn_obj_set_style_text_font(nint obj, nint font, uint selector);

    [LibraryImport(Library)]
    internal static partial nint lvn_font_montserrat(uint size);

    #endregion

    #region Styles - reusable style objects

    [LibraryImport(Library)]
    internal static partial nint lvn_style_create();

    [LibraryImport(Library)]
    internal static partial void lvn_style_delete(nint style);

    [LibraryImport(Library)]
    internal static partial void lvn_style_set_bg_color(nint style, uint rgb);

    [LibraryImport(Library)]
    internal static partial void lvn_style_set_bg_grad_color(nint style, uint rgb);

    [LibraryImport(Library)]
    internal static partial void lvn_style_set_text_color(nint style, uint rgb);

    [LibraryImport(Library)]
    internal static partial void lvn_style_set_border_color(nint style, uint rgb);

    [LibraryImport(Library)]
    internal static partial void lvn_style_set_line_color(nint style, uint rgb);

    [LibraryImport(Library)]
    internal static partial void lvn_style_set_arc_color(nint style, uint rgb);

    [LibraryImport(Library)]
    internal static partial void lvn_style_set_text_font(nint style, nint font);

    [LibraryImport(Library)]
    internal static partial void lv_style_set_radius(nint style, int radius);

    [LibraryImport(Library)]
    internal static partial void lv_style_set_border_width(nint style, int width);

    [LibraryImport(Library)]
    internal static partial void lvn_style_set_pad_all(nint style, int pad);

    [LibraryImport(Library)]
    internal static partial void lv_style_set_bg_opa(nint style, byte opa);

    [LibraryImport(Library)]
    internal static partial void lv_style_set_width(nint style, int width);

    [LibraryImport(Library)]
    internal static partial void lv_style_set_height(nint style, int height);

    #endregion

    #region Events

    [LibraryImport(Library)]
    internal static partial nint lv_obj_add_event_cb(nint obj, delegate* unmanaged[Cdecl]<nint, void> eventCb, int filter, void* userData);

    /// <summary>Translates a stable LVGL.Net event id into this build's <c>lv_event_code_t</c>.</summary>
    [LibraryImport(Library)]
    internal static partial int lvn_event_code(int stableId);

    [LibraryImport(Library)]
    internal static partial int lv_event_get_code(nint e);

    [LibraryImport(Library)]
    internal static partial nint lv_event_get_target(nint e);

    [LibraryImport(Library)]
    internal static partial nint lv_event_get_current_target(nint e);

    [LibraryImport(Library)]
    internal static partial void* lv_event_get_user_data(nint e);

    #endregion

    #region Widgets

    [LibraryImport(Library)]
    internal static partial nint lv_label_create(nint parent);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void lv_label_set_text(nint obj, string text);

    [LibraryImport(Library)]
    internal static partial nint lv_label_get_text(nint obj);

    [LibraryImport(Library)]
    internal static partial void lv_label_set_long_mode(nint obj, int mode);

    [LibraryImport(Library)]
    internal static partial nint lv_button_create(nint parent);

    [LibraryImport(Library)]
    internal static partial nint lv_slider_create(nint parent);

    [LibraryImport(Library)]
    internal static partial void lv_slider_set_value(nint obj, int value, int animEnable);

    [LibraryImport(Library)]
    internal static partial int lv_slider_get_value(nint obj);

    [LibraryImport(Library)]
    internal static partial void lv_slider_set_range(nint obj, int min, int max);

    [LibraryImport(Library)]
    internal static partial nint lv_bar_create(nint parent);

    [LibraryImport(Library)]
    internal static partial void lv_bar_set_value(nint obj, int value, int animEnable);

    [LibraryImport(Library)]
    internal static partial int lv_bar_get_value(nint obj);

    [LibraryImport(Library)]
    internal static partial void lv_bar_set_range(nint obj, int min, int max);

    [LibraryImport(Library)]
    internal static partial nint lv_arc_create(nint parent);

    [LibraryImport(Library)]
    internal static partial void lv_arc_set_value(nint obj, int value);

    [LibraryImport(Library)]
    internal static partial int lv_arc_get_value(nint obj);

    [LibraryImport(Library)]
    internal static partial void lv_arc_set_range(nint obj, int min, int max);

    [LibraryImport(Library)]
    internal static partial void lv_arc_set_bg_angles(nint obj, int start, int end);

    [LibraryImport(Library)]
    internal static partial void lv_arc_set_rotation(nint obj, int rotation);

    [LibraryImport(Library)]
    internal static partial nint lv_switch_create(nint parent);

    [LibraryImport(Library)]
    internal static partial nint lv_checkbox_create(nint parent);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void lv_checkbox_set_text(nint obj, string text);

    [LibraryImport(Library)]
    internal static partial nint lv_dropdown_create(nint parent);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void lv_dropdown_set_options(nint obj, string options);

    [LibraryImport(Library)]
    internal static partial uint lv_dropdown_get_selected(nint obj);

    [LibraryImport(Library)]
    internal static partial void lv_dropdown_set_selected(nint obj, uint index);

    [LibraryImport(Library)]
    internal static partial nint lv_roller_create(nint parent);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void lv_roller_set_options(nint obj, string options, int mode);

    [LibraryImport(Library)]
    internal static partial uint lv_roller_get_selected(nint obj);

    [LibraryImport(Library)]
    internal static partial nint lv_textarea_create(nint parent);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void lv_textarea_set_text(nint obj, string text);

    [LibraryImport(Library)]
    internal static partial nint lv_textarea_get_text(nint obj);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void lv_textarea_set_placeholder_text(nint obj, string text);

    [LibraryImport(Library)]
    internal static partial void lv_textarea_set_one_line(nint obj, [MarshalAs(UnmanagedType.U1)] bool oneLine);

    [LibraryImport(Library)]
    internal static partial nint lv_chart_create(nint parent);

    [LibraryImport(Library)]
    internal static partial void lv_chart_set_type(nint obj, int type);

    [LibraryImport(Library)]
    internal static partial void lv_chart_set_point_count(nint obj, uint count);

    [LibraryImport(Library)]
    internal static partial void lv_chart_set_range(nint obj, int axis, int min, int max);

    [LibraryImport(Library)]
    internal static partial void lv_chart_set_update_mode(nint obj, int mode);

    [LibraryImport(Library)]
    internal static partial void lv_chart_set_div_line_count(nint obj, byte hDiv, byte vDiv);

    [LibraryImport(Library)]
    internal static partial void lv_chart_set_next_value(nint obj, nint series, int value);

    [LibraryImport(Library)]
    internal static partial void lv_chart_set_all_value(nint obj, nint series, int value);

    [LibraryImport(Library)]
    internal static partial void lv_chart_refresh(nint obj);

    [LibraryImport(Library)]
    internal static partial void lv_chart_remove_series(nint obj, nint series);

    [LibraryImport(Library)]
    internal static partial nint lvn_chart_add_series(nint chart, uint rgb, int axis);

    #endregion

    #region Bundled demos

    [LibraryImport(Library)]
    internal static partial int lvn_demo_widgets();

    [LibraryImport(Library)]
    internal static partial int lvn_demo_benchmark();

    #endregion

    /// <summary>Reads a NUL-terminated UTF-8 string returned by LVGL without copying twice.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static string ReadUtf8(nint pointer) =>
        pointer == 0 ? string.Empty : Marshal.PtrToStringUTF8(pointer) ?? string.Empty;
}
