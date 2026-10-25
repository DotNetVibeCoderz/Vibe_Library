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

    /// <summary>
    /// Value of a named LVGL enum constant (e.g. <c>"LV_STATE_CHECKED"</c>), or
    /// <see cref="int.MinValue"/> when unknown. Used by the tests to verify the managed mirrors.
    /// </summary>
    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int lvn_constant(string name);

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

    /// <summary>Renders every pending invalid area now, without waiting for the refresh timer.</summary>
    [LibraryImport(Library)]
    internal static partial void lv_refr_now(nint display);

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

    [LibraryImport(Library)]
    internal static partial void lvn_obj_get_coords(nint obj, int* x1, int* y1, int* x2, int* y2);

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

    [LibraryImport(Library)]
    internal static partial int lv_obj_get_index(nint obj);

    [LibraryImport(Library)]
    internal static partial void lv_obj_move_to_index(nint obj, int index);

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
    internal static partial void lv_obj_add_state(nint obj, uint state);

    [LibraryImport(Library)]
    internal static partial void lv_obj_remove_state(nint obj, uint state);

    [LibraryImport(Library)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool lv_obj_has_state(nint obj, uint state);

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
    internal static partial void lv_obj_set_style_width(nint obj, int width, uint selector);

    [LibraryImport(Library)]
    internal static partial void lv_obj_set_style_height(nint obj, int height, uint selector);

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
    internal static partial void lv_slider_set_value(nint obj, int value, [MarshalAs(UnmanagedType.U1)] bool animate);

    [LibraryImport(Library)]
    internal static partial int lv_slider_get_value(nint obj);

    [LibraryImport(Library)]
    internal static partial void lv_slider_set_range(nint obj, int min, int max);

    [LibraryImport(Library)]
    internal static partial nint lv_bar_create(nint parent);

    [LibraryImport(Library)]
    internal static partial void lv_bar_set_value(nint obj, int value, [MarshalAs(UnmanagedType.U1)] bool animate);

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
    internal static partial void lv_chart_set_axis_range(nint obj, int axis, int min, int max);

    [LibraryImport(Library)]
    internal static partial void lv_chart_set_update_mode(nint obj, int mode);

    [LibraryImport(Library)]
    internal static partial void lv_chart_set_div_line_count(nint obj, uint hDiv, uint vDiv);

    [LibraryImport(Library)]
    internal static partial void lv_chart_set_next_value(nint obj, nint series, int value);

    [LibraryImport(Library)]
    internal static partial void lv_chart_set_all_values(nint obj, nint series, int value);

    [LibraryImport(Library)]
    internal static partial void lv_chart_refresh(nint obj);

    [LibraryImport(Library)]
    internal static partial void lv_chart_remove_series(nint obj, nint series);

    [LibraryImport(Library)]
    internal static partial nint lvn_chart_add_series(nint chart, uint rgb, int axis);

    #endregion

    #region Widgets - containers and navigation

    [LibraryImport(Library)]
    internal static partial nint lv_tabview_create(nint parent);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint lv_tabview_add_tab(nint obj, string name);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void lv_tabview_set_tab_text(nint obj, uint index, string name);

    [LibraryImport(Library)]
    internal static partial void lv_tabview_set_active(nint obj, uint index, [MarshalAs(UnmanagedType.U1)] bool animate);

    [LibraryImport(Library)]
    internal static partial uint lv_tabview_get_tab_active(nint obj);

    [LibraryImport(Library)]
    internal static partial uint lv_tabview_get_tab_count(nint obj);

    [LibraryImport(Library)]
    internal static partial void lv_tabview_set_tab_bar_position(nint obj, int direction);

    [LibraryImport(Library)]
    internal static partial void lv_tabview_set_tab_bar_size(nint obj, int size);

    [LibraryImport(Library)]
    internal static partial nint lv_tabview_get_content(nint obj);

    [LibraryImport(Library)]
    internal static partial nint lv_tabview_get_tab_bar(nint obj);

    [LibraryImport(Library)]
    internal static partial nint lv_list_create(nint parent);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint lv_list_add_text(nint list, string text);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint lv_list_add_button(nint list, string? icon, string text);

    [LibraryImport(Library)]
    internal static partial nint lv_list_get_button_text(nint list, nint button);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void lv_list_set_button_text(nint list, nint button, string text);

    [LibraryImport(Library)]
    internal static partial nint lv_msgbox_create(nint parent);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint lv_msgbox_add_title(nint obj, string title);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint lv_msgbox_add_text(nint obj, string text);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint lv_msgbox_add_footer_button(nint obj, string text);

    [LibraryImport(Library)]
    internal static partial nint lv_msgbox_add_close_button(nint obj);

    [LibraryImport(Library)]
    internal static partial nint lv_msgbox_get_content(nint obj);

    [LibraryImport(Library)]
    internal static partial void lv_msgbox_close(nint obj);

    [LibraryImport(Library)]
    internal static partial nint lv_tileview_create(nint parent);

    [LibraryImport(Library)]
    internal static partial nint lv_tileview_add_tile(nint obj, byte column, byte row, int direction);

    [LibraryImport(Library)]
    internal static partial void lv_tileview_set_tile_by_index(nint obj, uint column, uint row, [MarshalAs(UnmanagedType.U1)] bool animate);

    [LibraryImport(Library)]
    internal static partial nint lv_tileview_get_tile_active(nint obj);

    [LibraryImport(Library)]
    internal static partial nint lv_keyboard_create(nint parent);

    [LibraryImport(Library)]
    internal static partial void lv_keyboard_set_textarea(nint obj, nint textArea);

    [LibraryImport(Library)]
    internal static partial nint lv_keyboard_get_textarea(nint obj);

    [LibraryImport(Library)]
    internal static partial void lv_keyboard_set_mode(nint obj, int mode);

    [LibraryImport(Library)]
    internal static partial int lv_keyboard_get_mode(nint obj);

    [LibraryImport(Library)]
    internal static partial void lv_keyboard_set_popovers(nint obj, [MarshalAs(UnmanagedType.U1)] bool enabled);

    #endregion

    #region Widgets - indicators

    [LibraryImport(Library)]
    internal static partial nint lv_spinner_create(nint parent);

    [LibraryImport(Library)]
    internal static partial void lv_spinner_set_anim_params(nint obj, uint durationMs, uint arcSweep);

    [LibraryImport(Library)]
    internal static partial uint lv_spinner_get_anim_duration(nint obj);

    [LibraryImport(Library)]
    internal static partial uint lv_spinner_get_arc_sweep(nint obj);

    [LibraryImport(Library)]
    internal static partial nint lv_led_create(nint parent);

    [LibraryImport(Library)]
    internal static partial void lvn_led_set_color(nint obj, uint rgb);

    [LibraryImport(Library)]
    internal static partial void lv_led_set_brightness(nint obj, byte brightness);

    [LibraryImport(Library)]
    internal static partial byte lv_led_get_brightness(nint obj);

    [LibraryImport(Library)]
    internal static partial void lv_led_on(nint obj);

    [LibraryImport(Library)]
    internal static partial void lv_led_off(nint obj);

    [LibraryImport(Library)]
    internal static partial void lv_led_toggle(nint obj);

    [LibraryImport(Library)]
    internal static partial nint lv_scale_create(nint parent);

    [LibraryImport(Library)]
    internal static partial void lv_scale_set_mode(nint obj, int mode);

    [LibraryImport(Library)]
    internal static partial void lv_scale_set_range(nint obj, int min, int max);

    [LibraryImport(Library)]
    internal static partial void lv_scale_set_total_tick_count(nint obj, uint count);

    [LibraryImport(Library)]
    internal static partial void lv_scale_set_major_tick_every(nint obj, uint every);

    [LibraryImport(Library)]
    internal static partial void lv_scale_set_label_show(nint obj, [MarshalAs(UnmanagedType.U1)] bool show);

    [LibraryImport(Library)]
    internal static partial void lv_scale_set_angle_range(nint obj, uint degrees);

    [LibraryImport(Library)]
    internal static partial void lv_scale_set_rotation(nint obj, int degrees);

    [LibraryImport(Library)]
    internal static partial int lv_scale_get_mode(nint obj);

    [LibraryImport(Library)]
    internal static partial int lv_scale_get_total_tick_count(nint obj);

    [LibraryImport(Library)]
    internal static partial int lv_scale_get_major_tick_every(nint obj);

    [LibraryImport(Library)]
    internal static partial int lv_scale_get_range_min_value(nint obj);

    [LibraryImport(Library)]
    internal static partial int lv_scale_get_range_max_value(nint obj);

    [LibraryImport(Library)]
    internal static partial nint lv_table_create(nint parent);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void lv_table_set_cell_value(nint obj, uint row, uint column, string text);

    [LibraryImport(Library)]
    internal static partial nint lv_table_get_cell_value(nint obj, uint row, uint column);

    [LibraryImport(Library)]
    internal static partial void lv_table_set_row_count(nint obj, uint rows);

    [LibraryImport(Library)]
    internal static partial void lv_table_set_column_count(nint obj, uint columns);

    [LibraryImport(Library)]
    internal static partial uint lv_table_get_row_count(nint obj);

    [LibraryImport(Library)]
    internal static partial uint lv_table_get_column_count(nint obj);

    [LibraryImport(Library)]
    internal static partial void lv_table_set_column_width(nint obj, uint column, int width);

    [LibraryImport(Library)]
    internal static partial int lv_table_get_column_width(nint obj, uint column);

    [LibraryImport(Library)]
    internal static partial nint lv_spinbox_create(nint parent);

    [LibraryImport(Library)]
    internal static partial void lv_spinbox_set_value(nint obj, int value);

    [LibraryImport(Library)]
    internal static partial int lv_spinbox_get_value(nint obj);

    [LibraryImport(Library)]
    internal static partial void lv_spinbox_set_range(nint obj, int min, int max);

    [LibraryImport(Library)]
    internal static partial void lv_spinbox_set_digit_format(nint obj, uint digitCount, uint separatorPosition);

    [LibraryImport(Library)]
    internal static partial void lv_spinbox_set_step(nint obj, uint step);

    [LibraryImport(Library)]
    internal static partial int lv_spinbox_get_step(nint obj);

    [LibraryImport(Library)]
    internal static partial void lv_spinbox_set_rollover(nint obj, [MarshalAs(UnmanagedType.U1)] bool rollover);

    [LibraryImport(Library)]
    internal static partial void lv_spinbox_increment(nint obj);

    [LibraryImport(Library)]
    internal static partial void lv_spinbox_decrement(nint obj);

    #endregion

    #region Images

    [LibraryImport(Library)]
    internal static partial nint lv_image_create(nint parent);

    /// <summary>Sets a descriptor (<c>lv_image_dsc_t*</c>) as the source.</summary>
    [LibraryImport(Library)]
    internal static partial void lv_image_set_src(nint obj, nint source);

    /// <summary>
    /// Sets a file path (<c>"A:..."</c>) or a symbol string as the source. LVGL tells the two
    /// apart by the first byte and copies the string, so it need not outlive the call.
    /// </summary>
    [LibraryImport(Library, EntryPoint = "lv_image_set_src", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void lv_image_set_src_text(nint obj, string source);

    [LibraryImport(Library)]
    internal static partial void lv_image_set_rotation(nint obj, int angleTenths);

    [LibraryImport(Library)]
    internal static partial int lv_image_get_rotation(nint obj);

    [LibraryImport(Library)]
    internal static partial void lv_image_set_scale(nint obj, uint scale);

    [LibraryImport(Library)]
    internal static partial int lv_image_get_scale(nint obj);

    [LibraryImport(Library)]
    internal static partial void lv_image_set_pivot(nint obj, int x, int y);

    [LibraryImport(Library)]
    internal static partial void lv_image_set_inner_align(nint obj, int align);

    [LibraryImport(Library)]
    internal static partial void lv_image_set_antialias(nint obj, [MarshalAs(UnmanagedType.U1)] bool antialias);

    [LibraryImport(Library)]
    internal static partial int lv_image_get_src_width(nint obj);

    [LibraryImport(Library)]
    internal static partial int lv_image_get_src_height(nint obj);

    [LibraryImport(Library)]
    internal static partial nint lvn_image_dsc_create(uint width, uint height, uint colorFormat, uint stride, void* data, uint dataSize);

    [LibraryImport(Library)]
    internal static partial void lvn_image_dsc_delete(nint descriptor);

    [LibraryImport(Library)]
    internal static partial void lvn_obj_set_style_image_recolor(nint obj, uint rgb, uint selector);

    [LibraryImport(Library)]
    internal static partial void lv_obj_set_style_image_recolor_opa(nint obj, byte opa, uint selector);

    #endregion

    #region Fonts

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint lv_tiny_ttf_create_file(string path, int fontSize);

    [LibraryImport(Library)]
    internal static partial nint lv_tiny_ttf_create_data(void* data, nuint dataSize, int fontSize);

    [LibraryImport(Library)]
    internal static partial void lv_tiny_ttf_set_size(nint font, int fontSize);

    [LibraryImport(Library)]
    internal static partial void lv_tiny_ttf_destroy(nint font);

    #endregion

    #region Animations and timers

    [LibraryImport(Library)]
    internal static partial void lvn_anim_set_callbacks(
        delegate* unmanaged[Cdecl]<nint, int, void> exec,
        delegate* unmanaged[Cdecl]<nint, void> completed,
        delegate* unmanaged[Cdecl]<nint, void> deleted);

    [LibraryImport(Library)]
    internal static partial int lvn_anim_start(
        nint id, int start, int end, uint duration, uint delay, uint reverseDuration, uint reverseDelay,
        uint repeatCount, uint repeatDelay, int path, byte earlyApply);

    [LibraryImport(Library)]
    internal static partial int lvn_anim_stop(nint id);

    [LibraryImport(Library)]
    internal static partial nint lv_timer_create(delegate* unmanaged[Cdecl]<nint, void> callback, uint periodMs, void* userData);

    [LibraryImport(Library)]
    internal static partial void lv_timer_delete(nint timer);

    [LibraryImport(Library)]
    internal static partial void* lv_timer_get_user_data(nint timer);

    [LibraryImport(Library)]
    internal static partial void lv_timer_pause(nint timer);

    [LibraryImport(Library)]
    internal static partial void lv_timer_resume(nint timer);

    [LibraryImport(Library)]
    internal static partial void lv_timer_ready(nint timer);

    [LibraryImport(Library)]
    internal static partial void lv_timer_reset(nint timer);

    [LibraryImport(Library)]
    internal static partial void lv_timer_set_period(nint timer, uint periodMs);

    [LibraryImport(Library)]
    internal static partial void lv_timer_set_repeat_count(nint timer, int count);

    /// <summary>
    /// Off for every managed timer: an auto-deleted timer would leave the wrapper holding a freed
    /// pointer. With it off, a timer whose repeat count runs out is paused instead.
    /// </summary>
    [LibraryImport(Library)]
    internal static partial void lv_timer_set_auto_delete(nint timer, [MarshalAs(UnmanagedType.U1)] bool autoDelete);

    [LibraryImport(Library)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool lv_timer_get_paused(nint timer);

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
