#include "lvglnet_shim.h"

#include <stdlib.h>

#if LV_USE_DEMO_WIDGETS
#include "demos/widgets/lv_demo_widgets.h"
#endif
#if LV_USE_DEMO_BENCHMARK
#include "demos/benchmark/lv_demo_benchmark.h"
#endif

/* ---- build information ------------------------------------------------ */

uint32_t lvn_abi_version(void)
{
    return LVN_ABI_VERSION;
}

uint32_t lvn_lvgl_version(void)
{
    return ((uint32_t)LVGL_VERSION_MAJOR << 16) |
           ((uint32_t)LVGL_VERSION_MINOR << 8) |
           ((uint32_t)LVGL_VERSION_PATCH);
}

uint32_t lvn_color_depth(void)
{
    return LV_COLOR_DEPTH;
}

uint32_t lvn_style_size(void)
{
    return (uint32_t)sizeof(lv_style_t);
}

int32_t lvn_size_content(void)
{
    return (int32_t)LV_SIZE_CONTENT;
}

int32_t lvn_coord_max(void)
{
    return (int32_t)LV_COORD_MAX;
}

/* ---- geometry --------------------------------------------------------- */

void lvn_area_get(const lv_area_t *area, int32_t *x1, int32_t *y1, int32_t *x2, int32_t *y2)
{
    if (area == NULL) return;
    if (x1) *x1 = area->x1;
    if (y1) *y1 = area->y1;
    if (x2) *x2 = area->x2;
    if (y2) *y2 = area->y2;
}

/* ---- event codes ------------------------------------------------------ */

int32_t lvn_event_code(int32_t stable_id)
{
    switch (stable_id) {
        case LVN_EVENT_ALL:                 return LV_EVENT_ALL;
        case LVN_EVENT_PRESSED:             return LV_EVENT_PRESSED;
        case LVN_EVENT_PRESSING:            return LV_EVENT_PRESSING;
        case LVN_EVENT_PRESS_LOST:          return LV_EVENT_PRESS_LOST;
        case LVN_EVENT_SHORT_CLICKED:       return LV_EVENT_SHORT_CLICKED;
        case LVN_EVENT_LONG_PRESSED:        return LV_EVENT_LONG_PRESSED;
        case LVN_EVENT_LONG_PRESSED_REPEAT: return LV_EVENT_LONG_PRESSED_REPEAT;
        case LVN_EVENT_CLICKED:             return LV_EVENT_CLICKED;
        case LVN_EVENT_RELEASED:            return LV_EVENT_RELEASED;
        case LVN_EVENT_SCROLL_BEGIN:        return LV_EVENT_SCROLL_BEGIN;
        case LVN_EVENT_SCROLL_END:          return LV_EVENT_SCROLL_END;
        case LVN_EVENT_SCROLL:              return LV_EVENT_SCROLL;
        case LVN_EVENT_GESTURE:             return LV_EVENT_GESTURE;
        case LVN_EVENT_KEY:                 return LV_EVENT_KEY;
        case LVN_EVENT_FOCUSED:             return LV_EVENT_FOCUSED;
        case LVN_EVENT_DEFOCUSED:           return LV_EVENT_DEFOCUSED;
        case LVN_EVENT_LEAVE:               return LV_EVENT_LEAVE;
        case LVN_EVENT_VALUE_CHANGED:       return LV_EVENT_VALUE_CHANGED;
        case LVN_EVENT_INSERT:              return LV_EVENT_INSERT;
        case LVN_EVENT_REFRESH:             return LV_EVENT_REFRESH;
        case LVN_EVENT_READY:               return LV_EVENT_READY;
        case LVN_EVENT_CANCEL:              return LV_EVENT_CANCEL;
        case LVN_EVENT_DELETE:              return LV_EVENT_DELETE;
        case LVN_EVENT_CHILD_CHANGED:       return LV_EVENT_CHILD_CHANGED;
        case LVN_EVENT_SIZE_CHANGED:        return LV_EVENT_SIZE_CHANGED;
        case LVN_EVENT_STYLE_CHANGED:       return LV_EVENT_STYLE_CHANGED;
        case LVN_EVENT_SCREEN_LOADED:       return LV_EVENT_SCREEN_LOADED;
        case LVN_EVENT_SCREEN_UNLOADED:     return LV_EVENT_SCREEN_UNLOADED;
        default:                            return -1;
    }
}

/* ---- input devices ---------------------------------------------------- */

void lvn_indev_data_set_pointer(lv_indev_data_t *data, int32_t x, int32_t y, uint8_t pressed)
{
    if (data == NULL) return;
    data->point.x = x;
    data->point.y = y;
    data->state = pressed ? LV_INDEV_STATE_PRESSED : LV_INDEV_STATE_RELEASED;
}

void lvn_indev_data_set_key(lv_indev_data_t *data, uint32_t key, uint8_t pressed)
{
    if (data == NULL) return;
    data->key = key;
    data->state = pressed ? LV_INDEV_STATE_PRESSED : LV_INDEV_STATE_RELEASED;
}

void lvn_indev_data_set_encoder(lv_indev_data_t *data, int32_t diff, uint8_t pressed)
{
    if (data == NULL) return;
    data->enc_diff = (int16_t)diff;
    data->state = pressed ? LV_INDEV_STATE_PRESSED : LV_INDEV_STATE_RELEASED;
}

/* ---- fonts ------------------------------------------------------------ */

const lv_font_t *lvn_font_montserrat(uint32_t size)
{
    /* Only the sizes enabled in lv_conf.h are linked in; the rest fall through
     * to the default font so a UI built on another machine still renders. */
    switch (size) {
#if LV_FONT_MONTSERRAT_12
        case 12: return &lv_font_montserrat_12;
#endif
#if LV_FONT_MONTSERRAT_14
        case 14: return &lv_font_montserrat_14;
#endif
#if LV_FONT_MONTSERRAT_16
        case 16: return &lv_font_montserrat_16;
#endif
#if LV_FONT_MONTSERRAT_20
        case 20: return &lv_font_montserrat_20;
#endif
#if LV_FONT_MONTSERRAT_24
        case 24: return &lv_font_montserrat_24;
#endif
#if LV_FONT_MONTSERRAT_28
        case 28: return &lv_font_montserrat_28;
#endif
#if LV_FONT_MONTSERRAT_36
        case 36: return &lv_font_montserrat_36;
#endif
        default: break;
    }
    return LV_FONT_DEFAULT;
}

void lvn_obj_set_style_text_font(lv_obj_t *obj, const lv_font_t *font, uint32_t selector)
{
    if (obj == NULL || font == NULL) return;
    lv_obj_set_style_text_font(obj, font, (lv_style_selector_t)selector);
}

/* ---- colours ---------------------------------------------------------- */

#define LVN_COLOR_SETTER(name, fn)                                                    \
    void name(lv_obj_t *obj, uint32_t rgb, uint32_t selector)                         \
    {                                                                                 \
        if (obj == NULL) return;                                                      \
        fn(obj, lv_color_hex(rgb), (lv_style_selector_t)selector);                    \
    }

LVN_COLOR_SETTER(lvn_obj_set_style_bg_color, lv_obj_set_style_bg_color)
LVN_COLOR_SETTER(lvn_obj_set_style_bg_grad_color, lv_obj_set_style_bg_grad_color)
LVN_COLOR_SETTER(lvn_obj_set_style_text_color, lv_obj_set_style_text_color)
LVN_COLOR_SETTER(lvn_obj_set_style_border_color, lv_obj_set_style_border_color)
LVN_COLOR_SETTER(lvn_obj_set_style_outline_color, lv_obj_set_style_outline_color)
LVN_COLOR_SETTER(lvn_obj_set_style_shadow_color, lv_obj_set_style_shadow_color)
LVN_COLOR_SETTER(lvn_obj_set_style_line_color, lv_obj_set_style_line_color)
LVN_COLOR_SETTER(lvn_obj_set_style_arc_color, lv_obj_set_style_arc_color)

/* ---- inline helpers that have no exported symbol ---------------------- */

void lvn_obj_set_style_pad_all(lv_obj_t *obj, int32_t value, uint32_t selector)
{
    if (obj == NULL) return;
    lv_obj_set_style_pad_all(obj, value, (lv_style_selector_t)selector);
}

void lvn_style_set_pad_all(lv_style_t *style, int32_t value)
{
    if (style == NULL) return;
    lv_style_set_pad_all(style, value);
}

/* ---- reusable styles -------------------------------------------------- */

lv_style_t *lvn_style_create(void)
{
    lv_style_t *style = (lv_style_t *)malloc(sizeof(lv_style_t));
    if (style == NULL) return NULL;
    lv_style_init(style);
    return style;
}

void lvn_style_delete(lv_style_t *style)
{
    if (style == NULL) return;
    lv_style_reset(style);
    free(style);
}

#define LVN_STYLE_COLOR_SETTER(name, fn)                       \
    void name(lv_style_t *style, uint32_t rgb)                 \
    {                                                          \
        if (style == NULL) return;                             \
        fn(style, lv_color_hex(rgb));                          \
    }

LVN_STYLE_COLOR_SETTER(lvn_style_set_bg_color, lv_style_set_bg_color)
LVN_STYLE_COLOR_SETTER(lvn_style_set_bg_grad_color, lv_style_set_bg_grad_color)
LVN_STYLE_COLOR_SETTER(lvn_style_set_text_color, lv_style_set_text_color)
LVN_STYLE_COLOR_SETTER(lvn_style_set_border_color, lv_style_set_border_color)
LVN_STYLE_COLOR_SETTER(lvn_style_set_line_color, lv_style_set_line_color)
LVN_STYLE_COLOR_SETTER(lvn_style_set_arc_color, lv_style_set_arc_color)

void lvn_style_set_text_font(lv_style_t *style, const lv_font_t *font)
{
    if (style == NULL || font == NULL) return;
    lv_style_set_text_font(style, font);
}

/* ---- charts ----------------------------------------------------------- */

lv_chart_series_t *lvn_chart_add_series(lv_obj_t *chart, uint32_t rgb, int32_t axis)
{
    if (chart == NULL) return NULL;
    return lv_chart_add_series(chart, lv_color_hex(rgb), (lv_chart_axis_t)axis);
}

/* ---- optional bundled demos ------------------------------------------- */

int32_t lvn_demo_widgets(void)
{
#if LV_USE_DEMO_WIDGETS
    lv_demo_widgets();
    return 1;
#else
    return 0;
#endif
}

int32_t lvn_demo_benchmark(void)
{
#if LV_USE_DEMO_BENCHMARK
    lv_demo_benchmark();
    return 1;
#else
    return 0;
#endif
}
