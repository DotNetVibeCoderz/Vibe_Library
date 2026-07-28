/*
 * lv_conf.h - LVGL build configuration used by LVGL.Net.
 *
 * Only the options that LVGL.Net actually depends on are listed here; every
 * other option falls through to the upstream default in lv_conf_internal.h.
 * Keeping this file small means a new LVGL point release does not have to be
 * merged by hand each time.
 *
 * IMPORTANT: LV_COLOR_DEPTH must stay 32. The managed side hands LVGL a
 * buffer it interprets as XRGB8888 (byte order B,G,R,X on little-endian) and
 * blits it straight to SDL / the Linux framebuffer with no per-pixel
 * conversion. Changing the depth here without changing PixelFormat in
 * LvglDisplay.cs will produce colour-swapped output.
 */
#ifndef LV_CONF_H
#define LV_CONF_H

#include <stdint.h>

/*====================
   COLOR & MEMORY
 *====================*/

#define LV_COLOR_DEPTH 32

/* The host has a real allocator; the built-in pool allocator would need a
 * fixed LV_MEM_SIZE tuned per screen size, which we do not want to hard-code. */
#define LV_USE_STDLIB_MALLOC  LV_STDLIB_CLIB
#define LV_USE_STDLIB_STRING  LV_STDLIB_CLIB
#define LV_USE_STDLIB_SPRINTF LV_STDLIB_CLIB

/*====================
   HAL / TIMING
 *====================*/

/* Managed code drives the clock through lv_tick_inc() from LvglApplication. */
#define LV_DEF_REFR_PERIOD 16   /* ~60 FPS refresh cadence */
#define LV_DPI_DEF         130

#define LV_USE_OS LV_OS_NONE

/*====================
   RENDERING
 *====================*/

#define LV_USE_DRAW_SW 1
/* 0 = render on the calling thread. The wrapper owns a single UI thread and
 * extra draw threads only add contention on a Pi's small core count. */
#define LV_DRAW_SW_DRAW_UNIT_CNT 1
#define LV_DRAW_SW_COMPLEX       1

/*====================
   LOGGING / DEBUG
 *====================*/

#ifndef LV_USE_LOG
#define LV_USE_LOG 0
#endif
#if LV_USE_LOG
#define LV_LOG_LEVEL LV_LOG_LEVEL_WARN
#define LV_LOG_PRINTF 1
#endif

#define LV_USE_ASSERT_NULL          1
#define LV_USE_ASSERT_MALLOC        1
#define LV_USE_ASSERT_OBJ           0
#define LV_USE_ASSERT_STYLE         0

/*====================
   FONTS
 *====================*/

#define LV_FONT_MONTSERRAT_12 1
#define LV_FONT_MONTSERRAT_14 1
#define LV_FONT_MONTSERRAT_16 1
#define LV_FONT_MONTSERRAT_20 1
#define LV_FONT_MONTSERRAT_24 1   /* also required by lv_demo_benchmark */
#define LV_FONT_MONTSERRAT_28 1
#define LV_FONT_MONTSERRAT_36 1

#define LV_FONT_DEFAULT &lv_font_montserrat_14

/*====================
   WIDGETS
 *====================*/

#define LV_USE_ARC        1
#define LV_USE_BAR        1
#define LV_USE_BUTTON     1
#define LV_USE_BUTTONMATRIX 1
#define LV_USE_CANVAS     1
#define LV_USE_CHECKBOX   1
#define LV_USE_DROPDOWN   1
#define LV_USE_IMAGE      1
#define LV_USE_LABEL      1
#define LV_LABEL_TEXT_SELECTION 1
#define LV_USE_LINE       1
#define LV_USE_ROLLER     1
#define LV_USE_SCALE      1
#define LV_USE_SLIDER     1
#define LV_USE_SWITCH     1
#define LV_USE_TEXTAREA   1
#define LV_USE_TABLE      1
#define LV_USE_CHART      1
#define LV_USE_KEYBOARD   1
#define LV_USE_LIST       1
#define LV_USE_MENU       1
#define LV_USE_MSGBOX     1
#define LV_USE_SPINNER    1
#define LV_USE_TABVIEW    1
#define LV_USE_TILEVIEW   1
#define LV_USE_WIN        1

/*====================
   THEMES & LAYOUTS
 *====================*/

#define LV_USE_THEME_DEFAULT 1
#define LV_THEME_DEFAULT_DARK 0
#define LV_THEME_DEFAULT_GROW 1

#define LV_USE_FLEX 1
#define LV_USE_GRID 1

/*====================
   DEMOS (enabled by the LVGLNET_WITH_DEMOS CMake option)
 *====================*/

#ifdef LVGLNET_WITH_DEMOS
#define LV_USE_DEMO_WIDGETS   1
#define LV_USE_DEMO_BENCHMARK 1
#define LV_USE_PERF_MONITOR   0
#endif

#endif /* LV_CONF_H */
