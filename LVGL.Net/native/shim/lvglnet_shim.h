/*
 * lvglnet_shim.h - ABI-safe entry points for the LVGL.Net managed wrapper.
 *
 * Why this file exists
 * --------------------
 * Most of LVGL's C API is directly P/Invoke-able: the arguments are pointers,
 * ints and enums, which marshal identically on every platform we target.
 * A small part of it is NOT safe to call directly from .NET:
 *
 *   1. lv_color_t is a 3-byte struct passed BY VALUE. A 3-byte struct is
 *      passed in a register on the System V AMD64 / AAPCS ABIs but *by hidden
 *      pointer* on Windows x64. Getting that wrong silently corrupts colours.
 *   2. lv_style_t must be allocated by the caller, but its size is a
 *      compile-time detail of lv_conf.h that managed code cannot know.
 *   3. lv_indev_data_t and lv_area_t are written/read field-by-field, so
 *      managed code would have to hard-code struct offsets.
 *   4. The built-in fonts (lv_font_montserrat_*) are exported *variables*,
 *      not functions, which P/Invoke cannot bind to portably.
 *   5. Some convenience setters (lv_obj_set_style_pad_all, lv_style_set_pad_all)
 *      are static inline in LVGL's headers, so they exist in no shared library
 *      and there is no symbol to import. They are re-exported here.
 *
 * Everything here is a thin forwarder: no state, no allocation on the hot
 * path, no copying of pixel data. The render loop still calls lv_* directly.
 */
#ifndef LVGLNET_SHIM_H
#define LVGLNET_SHIM_H

#include "lvgl.h"

#ifdef __cplusplus
extern "C" {
#endif

#if defined(_WIN32)
#  define LVN_API __declspec(dllexport)
#else
#  define LVN_API __attribute__((visibility("default")))
#endif

/* Bumped whenever an existing lvn_* signature changes, or a new entry point the
 * managed side requires is added. The managed side checks this on startup and
 * refuses to run against a mismatched build.
 * Keep in step with LvglRuntime.ExpectedShimAbi. */
#define LVN_ABI_VERSION 3

/* ---- build information ------------------------------------------------ */
LVN_API uint32_t lvn_abi_version(void);
LVN_API uint32_t lvn_lvgl_version(void);   /* (major << 16) | (minor << 8) | patch */
LVN_API uint32_t lvn_color_depth(void);    /* bits per pixel of the render buffer */
LVN_API uint32_t lvn_style_size(void);     /* sizeof(lv_style_t), for diagnostics */

/* LV_SIZE_CONTENT is a macro, so managed code reproduces its value. This exposes
 * the real one so the reproduction can be verified rather than trusted. */
LVN_API int32_t lvn_size_content(void);
LVN_API int32_t lvn_coord_max(void);

/* ---- geometry --------------------------------------------------------- */
LVN_API void lvn_area_get(const lv_area_t *area, int32_t *x1, int32_t *y1, int32_t *x2, int32_t *y2);

/* ---- event codes ------------------------------------------------------ */
/*
 * lv_event_code_t is an enum whose numeric values have shifted between LVGL
 * point releases (LV_EVENT_ROTARY was inserted in the middle in 9.1). Managed
 * code therefore uses its own stable identifiers, listed below, and translates
 * them here at run time instead of baking in LVGL's ordinals.
 * Returns -1 for an identifier this LVGL build does not have.
 */
enum {
    LVN_EVENT_ALL = 0,
    LVN_EVENT_PRESSED = 1,
    LVN_EVENT_PRESSING = 2,
    LVN_EVENT_PRESS_LOST = 3,
    LVN_EVENT_SHORT_CLICKED = 4,
    LVN_EVENT_LONG_PRESSED = 5,
    LVN_EVENT_LONG_PRESSED_REPEAT = 6,
    LVN_EVENT_CLICKED = 7,
    LVN_EVENT_RELEASED = 8,
    LVN_EVENT_SCROLL_BEGIN = 9,
    LVN_EVENT_SCROLL_END = 10,
    LVN_EVENT_SCROLL = 11,
    LVN_EVENT_GESTURE = 12,
    LVN_EVENT_KEY = 13,
    LVN_EVENT_FOCUSED = 14,
    LVN_EVENT_DEFOCUSED = 15,
    LVN_EVENT_LEAVE = 16,
    LVN_EVENT_VALUE_CHANGED = 17,
    LVN_EVENT_INSERT = 18,
    LVN_EVENT_REFRESH = 19,
    LVN_EVENT_READY = 20,
    LVN_EVENT_CANCEL = 21,
    LVN_EVENT_DELETE = 22,
    LVN_EVENT_CHILD_CHANGED = 23,
    LVN_EVENT_SIZE_CHANGED = 24,
    LVN_EVENT_STYLE_CHANGED = 25,
    LVN_EVENT_SCREEN_LOADED = 26,
    LVN_EVENT_SCREEN_UNLOADED = 27
};

LVN_API int32_t lvn_event_code(int32_t stable_id);

/* ---- input devices ---------------------------------------------------- */
LVN_API void lvn_indev_data_set_pointer(lv_indev_data_t *data, int32_t x, int32_t y, uint8_t pressed);
LVN_API void lvn_indev_data_set_key(lv_indev_data_t *data, uint32_t key, uint8_t pressed);
LVN_API void lvn_indev_data_set_encoder(lv_indev_data_t *data, int32_t diff, uint8_t pressed);

/* ---- fonts ------------------------------------------------------------ */
/* Returns the closest built-in Montserrat font, or NULL when none is compiled in. */
LVN_API const lv_font_t *lvn_font_montserrat(uint32_t size);
LVN_API void lvn_obj_set_style_text_font(lv_obj_t *obj, const lv_font_t *font, uint32_t selector);

/* ---- colours (uint32_t 0xRRGGBB instead of lv_color_t by value) -------- */
LVN_API void lvn_obj_set_style_bg_color(lv_obj_t *obj, uint32_t rgb, uint32_t selector);
LVN_API void lvn_obj_set_style_bg_grad_color(lv_obj_t *obj, uint32_t rgb, uint32_t selector);
LVN_API void lvn_obj_set_style_text_color(lv_obj_t *obj, uint32_t rgb, uint32_t selector);
LVN_API void lvn_obj_set_style_border_color(lv_obj_t *obj, uint32_t rgb, uint32_t selector);
LVN_API void lvn_obj_set_style_outline_color(lv_obj_t *obj, uint32_t rgb, uint32_t selector);
LVN_API void lvn_obj_set_style_shadow_color(lv_obj_t *obj, uint32_t rgb, uint32_t selector);
LVN_API void lvn_obj_set_style_line_color(lv_obj_t *obj, uint32_t rgb, uint32_t selector);
LVN_API void lvn_obj_set_style_arc_color(lv_obj_t *obj, uint32_t rgb, uint32_t selector);

/* ---- inline helpers that have no exported symbol ---------------------- */
LVN_API void lvn_obj_set_style_pad_all(lv_obj_t *obj, int32_t value, uint32_t selector);
LVN_API void lvn_style_set_pad_all(lv_style_t *style, int32_t value);

/* ---- reusable styles -------------------------------------------------- */
LVN_API lv_style_t *lvn_style_create(void);
LVN_API void lvn_style_delete(lv_style_t *style);
LVN_API void lvn_style_set_bg_color(lv_style_t *style, uint32_t rgb);
LVN_API void lvn_style_set_bg_grad_color(lv_style_t *style, uint32_t rgb);
LVN_API void lvn_style_set_text_color(lv_style_t *style, uint32_t rgb);
LVN_API void lvn_style_set_border_color(lv_style_t *style, uint32_t rgb);
LVN_API void lvn_style_set_line_color(lv_style_t *style, uint32_t rgb);
LVN_API void lvn_style_set_arc_color(lv_style_t *style, uint32_t rgb);
LVN_API void lvn_style_set_text_font(lv_style_t *style, const lv_font_t *font);

/* ---- charts ----------------------------------------------------------- */
LVN_API lv_chart_series_t *lvn_chart_add_series(lv_obj_t *chart, uint32_t rgb, int32_t axis);

/* ---- optional bundled demos ------------------------------------------- */
LVN_API int32_t lvn_demo_widgets(void);
LVN_API int32_t lvn_demo_benchmark(void);

#ifdef __cplusplus
}
#endif

#endif /* LVGLNET_SHIM_H */
