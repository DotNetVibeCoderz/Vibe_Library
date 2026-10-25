#include "lvglnet_shim.h"

#include <stdlib.h>
#include <string.h>

/* Internal in 9.6 (not under include/), but exported: needed so a freed image
 * descriptor cannot leave a cache entry keyed by its address. */
#include "src/misc/cache/instance/lv_image_cache.h"

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

/* ---- enum constants --------------------------------------------------- */

typedef struct {
    const char *name;
    int32_t value;
} lvn_constant_t;

#define LVN_K(name) { #name, (int32_t)(name) }

/* Every constant the managed side mirrors in LvglEnums.cs. A name missing here
 * makes the cross-check test fail loudly instead of passing on a guess. */
static const lvn_constant_t lvn_constants[] = {
    LVN_K(LV_STATE_DEFAULT), LVN_K(LV_STATE_ALT), LVN_K(LV_STATE_CHECKED), LVN_K(LV_STATE_FOCUSED),
    LVN_K(LV_STATE_FOCUS_KEY), LVN_K(LV_STATE_EDITED), LVN_K(LV_STATE_HOVERED), LVN_K(LV_STATE_PRESSED),
    LVN_K(LV_STATE_SCROLLED), LVN_K(LV_STATE_DISABLED), LVN_K(LV_STATE_USER_1), LVN_K(LV_STATE_USER_2),
    LVN_K(LV_STATE_USER_3), LVN_K(LV_STATE_USER_4), LVN_K(LV_STATE_ANY),

    LVN_K(LV_PART_MAIN), LVN_K(LV_PART_SCROLLBAR), LVN_K(LV_PART_INDICATOR), LVN_K(LV_PART_KNOB),
    LVN_K(LV_PART_SELECTED), LVN_K(LV_PART_ITEMS), LVN_K(LV_PART_CURSOR), LVN_K(LV_PART_ANY),

    LVN_K(LV_OBJ_FLAG_HIDDEN), LVN_K(LV_OBJ_FLAG_CLICKABLE), LVN_K(LV_OBJ_FLAG_CLICK_FOCUSABLE),
    LVN_K(LV_OBJ_FLAG_CHECKABLE), LVN_K(LV_OBJ_FLAG_SCROLLABLE), LVN_K(LV_OBJ_FLAG_SCROLL_ELASTIC),
    LVN_K(LV_OBJ_FLAG_SCROLL_MOMENTUM), LVN_K(LV_OBJ_FLAG_SCROLL_ONE), LVN_K(LV_OBJ_FLAG_SCROLL_CHAIN_HOR),
    LVN_K(LV_OBJ_FLAG_SCROLL_CHAIN_VER), LVN_K(LV_OBJ_FLAG_SCROLL_ON_FOCUS),
    LVN_K(LV_OBJ_FLAG_SCROLL_WITH_ARROW), LVN_K(LV_OBJ_FLAG_SNAPPABLE), LVN_K(LV_OBJ_FLAG_PRESS_LOCK),
    LVN_K(LV_OBJ_FLAG_EVENT_BUBBLE), LVN_K(LV_OBJ_FLAG_GESTURE_BUBBLE), LVN_K(LV_OBJ_FLAG_ADV_HITTEST),
    LVN_K(LV_OBJ_FLAG_IGNORE_LAYOUT), LVN_K(LV_OBJ_FLAG_FLOATING), LVN_K(LV_OBJ_FLAG_OVERFLOW_VISIBLE),

    LVN_K(LV_ALIGN_DEFAULT), LVN_K(LV_ALIGN_TOP_LEFT), LVN_K(LV_ALIGN_TOP_MID), LVN_K(LV_ALIGN_TOP_RIGHT),
    LVN_K(LV_ALIGN_BOTTOM_LEFT), LVN_K(LV_ALIGN_BOTTOM_MID), LVN_K(LV_ALIGN_BOTTOM_RIGHT),
    LVN_K(LV_ALIGN_LEFT_MID), LVN_K(LV_ALIGN_RIGHT_MID), LVN_K(LV_ALIGN_CENTER),
    LVN_K(LV_ALIGN_OUT_TOP_LEFT), LVN_K(LV_ALIGN_OUT_TOP_MID), LVN_K(LV_ALIGN_OUT_TOP_RIGHT),
    LVN_K(LV_ALIGN_OUT_BOTTOM_LEFT), LVN_K(LV_ALIGN_OUT_BOTTOM_MID), LVN_K(LV_ALIGN_OUT_BOTTOM_RIGHT),
    LVN_K(LV_ALIGN_OUT_LEFT_TOP), LVN_K(LV_ALIGN_OUT_LEFT_MID), LVN_K(LV_ALIGN_OUT_LEFT_BOTTOM),
    LVN_K(LV_ALIGN_OUT_RIGHT_TOP), LVN_K(LV_ALIGN_OUT_RIGHT_MID), LVN_K(LV_ALIGN_OUT_RIGHT_BOTTOM),

    LVN_K(LV_DIR_NONE), LVN_K(LV_DIR_LEFT), LVN_K(LV_DIR_RIGHT), LVN_K(LV_DIR_TOP), LVN_K(LV_DIR_BOTTOM),
    LVN_K(LV_DIR_HOR), LVN_K(LV_DIR_VER), LVN_K(LV_DIR_ALL),

    LVN_K(LV_DISPLAY_RENDER_MODE_PARTIAL), LVN_K(LV_DISPLAY_RENDER_MODE_DIRECT),
    LVN_K(LV_DISPLAY_RENDER_MODE_FULL),
    LVN_K(LV_INDEV_TYPE_NONE), LVN_K(LV_INDEV_TYPE_POINTER), LVN_K(LV_INDEV_TYPE_KEYPAD),
    LVN_K(LV_INDEV_TYPE_BUTTON), LVN_K(LV_INDEV_TYPE_ENCODER),

    LVN_K(LV_LABEL_LONG_MODE_WRAP), LVN_K(LV_LABEL_LONG_MODE_DOTS), LVN_K(LV_LABEL_LONG_MODE_SCROLL),
    LVN_K(LV_LABEL_LONG_MODE_SCROLL_CIRCULAR), LVN_K(LV_LABEL_LONG_MODE_CLIP),
    LVN_K(LV_TEXT_ALIGN_AUTO), LVN_K(LV_TEXT_ALIGN_LEFT), LVN_K(LV_TEXT_ALIGN_CENTER),
    LVN_K(LV_TEXT_ALIGN_RIGHT),

    LVN_K(LV_FLEX_FLOW_ROW), LVN_K(LV_FLEX_FLOW_COLUMN), LVN_K(LV_FLEX_FLOW_ROW_WRAP),
    LVN_K(LV_FLEX_FLOW_ROW_REVERSE), LVN_K(LV_FLEX_FLOW_ROW_WRAP_REVERSE), LVN_K(LV_FLEX_FLOW_COLUMN_WRAP),
    LVN_K(LV_FLEX_FLOW_COLUMN_REVERSE), LVN_K(LV_FLEX_FLOW_COLUMN_WRAP_REVERSE),
    LVN_K(LV_FLEX_ALIGN_START), LVN_K(LV_FLEX_ALIGN_END), LVN_K(LV_FLEX_ALIGN_CENTER),
    LVN_K(LV_FLEX_ALIGN_SPACE_EVENLY), LVN_K(LV_FLEX_ALIGN_SPACE_AROUND),
    LVN_K(LV_FLEX_ALIGN_SPACE_BETWEEN),
    LVN_K(LV_SCROLLBAR_MODE_OFF), LVN_K(LV_SCROLLBAR_MODE_ON), LVN_K(LV_SCROLLBAR_MODE_ACTIVE),
    LVN_K(LV_SCROLLBAR_MODE_AUTO),

    LVN_K(LV_CHART_TYPE_NONE), LVN_K(LV_CHART_TYPE_LINE), LVN_K(LV_CHART_TYPE_CURVE),
    LVN_K(LV_CHART_TYPE_BAR), LVN_K(LV_CHART_TYPE_STACKED), LVN_K(LV_CHART_TYPE_SCATTER),
    LVN_K(LV_CHART_AXIS_PRIMARY_Y), LVN_K(LV_CHART_AXIS_SECONDARY_Y), LVN_K(LV_CHART_AXIS_PRIMARY_X),
    LVN_K(LV_CHART_AXIS_SECONDARY_X),
    LVN_K(LV_CHART_UPDATE_MODE_SHIFT), LVN_K(LV_CHART_UPDATE_MODE_CIRCULAR),
    LVN_K(LV_ROLLER_MODE_NORMAL), LVN_K(LV_ROLLER_MODE_INFINITE),

    LVN_K(LV_KEYBOARD_MODE_TEXT_LOWER), LVN_K(LV_KEYBOARD_MODE_TEXT_UPPER),
    LVN_K(LV_KEYBOARD_MODE_SPECIAL), LVN_K(LV_KEYBOARD_MODE_NUMBER),
    LVN_K(LV_SCALE_MODE_HORIZONTAL_TOP), LVN_K(LV_SCALE_MODE_HORIZONTAL_BOTTOM),
    LVN_K(LV_SCALE_MODE_VERTICAL_LEFT), LVN_K(LV_SCALE_MODE_VERTICAL_RIGHT),
    LVN_K(LV_SCALE_MODE_ROUND_INNER), LVN_K(LV_SCALE_MODE_ROUND_OUTER),
    LVN_K(LV_IMAGE_ALIGN_DEFAULT), LVN_K(LV_IMAGE_ALIGN_TOP_LEFT), LVN_K(LV_IMAGE_ALIGN_TOP_MID),
    LVN_K(LV_IMAGE_ALIGN_TOP_RIGHT), LVN_K(LV_IMAGE_ALIGN_BOTTOM_LEFT), LVN_K(LV_IMAGE_ALIGN_BOTTOM_MID),
    LVN_K(LV_IMAGE_ALIGN_BOTTOM_RIGHT), LVN_K(LV_IMAGE_ALIGN_LEFT_MID), LVN_K(LV_IMAGE_ALIGN_RIGHT_MID),
    LVN_K(LV_IMAGE_ALIGN_CENTER), LVN_K(LV_IMAGE_ALIGN_STRETCH), LVN_K(LV_IMAGE_ALIGN_TILE),
    LVN_K(LV_IMAGE_ALIGN_CONTAIN), LVN_K(LV_IMAGE_ALIGN_CONTAIN_DOWNSCALE),
    LVN_K(LV_IMAGE_ALIGN_COVER),

    LVN_K(LV_COLOR_FORMAT_ARGB8888), LVN_K(LV_COLOR_FORMAT_XRGB8888),
};

int32_t lvn_constant(const char *name)
{
    size_t i;
    if (name == NULL) return INT32_MIN;
    for (i = 0; i < sizeof(lvn_constants) / sizeof(lvn_constants[0]); i++) {
        if (strcmp(lvn_constants[i].name, name) == 0) return lvn_constants[i].value;
    }
    return INT32_MIN;
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

void lvn_obj_get_coords(const lv_obj_t *obj, int32_t *x1, int32_t *y1, int32_t *x2, int32_t *y2)
{
    lv_area_t area;
    if (obj == NULL) return;
    lv_obj_get_coords(obj, &area);
    lvn_area_get(&area, x1, y1, x2, y2);
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
#if LV_FONT_MONTSERRAT_26
        case 26: return &lv_font_montserrat_26;
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
LVN_COLOR_SETTER(lvn_obj_set_style_image_recolor, lv_obj_set_style_image_recolor)

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

/* ---- widgets with lv_color_t parameters --------------------------------- */

void lvn_led_set_color(lv_obj_t *led, uint32_t rgb)
{
    if (led == NULL) return;
    lv_led_set_color(led, lv_color_hex(rgb));
}

/* ---- images ------------------------------------------------------------- */

lv_image_dsc_t *lvn_image_dsc_create(uint32_t width, uint32_t height, uint32_t cf,
                                     uint32_t stride, const void *data, uint32_t data_size)
{
    lv_image_dsc_t *dsc;
    if (data == NULL || width == 0 || height == 0 || width > 0xFFFF || height > 0xFFFF || stride > 0xFFFF) {
        return NULL;
    }

    dsc = (lv_image_dsc_t *)calloc(1, sizeof(lv_image_dsc_t));
    if (dsc == NULL) return NULL;

    dsc->header.magic = LV_IMAGE_HEADER_MAGIC;
    dsc->header.cf = (uint8_t)cf;
    dsc->header.w = (uint16_t)width;
    dsc->header.h = (uint16_t)height;
    dsc->header.stride = (uint16_t)stride;
    dsc->data_size = data_size;
    dsc->data = (const uint8_t *)data;
    return dsc;
}

void lvn_image_dsc_delete(lv_image_dsc_t *dsc)
{
    if (dsc == NULL) return;
    /* The cache keys decoded images by source pointer; a later descriptor
     * allocated at the same address must not hit this one's entry. */
    lv_image_cache_drop(dsc);
    free(dsc);
}

/* ---- animations --------------------------------------------------------- */

static lvn_anim_exec_cb_t lvn_anim_exec;
static lvn_anim_event_cb_t lvn_anim_completed;
static lvn_anim_event_cb_t lvn_anim_deleted;

static void lvn_anim_exec_trampoline(void *var, int32_t value)
{
    if (lvn_anim_exec) lvn_anim_exec(var, value);
}

static void lvn_anim_completed_trampoline(lv_anim_t *a)
{
    if (lvn_anim_completed) lvn_anim_completed(a->var);
}

static void lvn_anim_deleted_trampoline(lv_anim_t *a)
{
    if (lvn_anim_deleted) lvn_anim_deleted(a->var);
}

void lvn_anim_set_callbacks(lvn_anim_exec_cb_t exec, lvn_anim_event_cb_t completed,
                            lvn_anim_event_cb_t deleted)
{
    lvn_anim_exec = exec;
    lvn_anim_completed = completed;
    lvn_anim_deleted = deleted;
}

static lv_anim_path_cb_t lvn_anim_path(int32_t path)
{
    switch (path) {
        case LVN_ANIM_PATH_EASE_IN:     return lv_anim_path_ease_in;
        case LVN_ANIM_PATH_EASE_OUT:    return lv_anim_path_ease_out;
        case LVN_ANIM_PATH_EASE_IN_OUT: return lv_anim_path_ease_in_out;
        case LVN_ANIM_PATH_OVERSHOOT:   return lv_anim_path_overshoot;
        case LVN_ANIM_PATH_BOUNCE:      return lv_anim_path_bounce;
        case LVN_ANIM_PATH_STEP:        return lv_anim_path_step;
        default:                        return lv_anim_path_linear;
    }
}

int32_t lvn_anim_start(void *id, int32_t start, int32_t end, uint32_t duration, uint32_t delay,
                       uint32_t reverse_duration, uint32_t reverse_delay,
                       uint32_t repeat_count, uint32_t repeat_delay, int32_t path,
                       uint8_t early_apply)
{
    lv_anim_t a;
    if (id == NULL) return 0;

    lv_anim_init(&a);
    lv_anim_set_var(&a, id);
    lv_anim_set_exec_cb(&a, lvn_anim_exec_trampoline);
    lv_anim_set_completed_cb(&a, lvn_anim_completed_trampoline);
    lv_anim_set_deleted_cb(&a, lvn_anim_deleted_trampoline);
    lv_anim_set_values(&a, start, end);
    lv_anim_set_duration(&a, duration);
    lv_anim_set_delay(&a, delay);
    lv_anim_set_reverse_duration(&a, reverse_duration);
    lv_anim_set_reverse_delay(&a, reverse_delay);
    lv_anim_set_repeat_count(&a, repeat_count);
    lv_anim_set_repeat_delay(&a, repeat_delay);
    lv_anim_set_path_cb(&a, lvn_anim_path(path));
    lv_anim_set_early_apply(&a, early_apply != 0);

    return lv_anim_start(&a) != NULL ? 1 : 0;
}

int32_t lvn_anim_stop(void *id)
{
    if (id == NULL) return 0;
    return lv_anim_delete(id, NULL) ? 1 : 0;
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
