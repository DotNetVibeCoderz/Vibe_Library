/*
 * lv_conf.h - LVGL build configuration used by LVGL.Net.
 *
 * Only the options that LVGL.Net actually depends on are listed here; every
 * other option falls through to the upstream default in lv_conf_internal.h.
 * Keeping this file small means a new LVGL point release does not have to be
 * merged by hand each time.
 *
 * IMPORTANT: the default colour format must stay XRGB8888 (byte order
 * B,G,R,X on little-endian), which makes LV_COLOR_DEPTH 32. The managed side
 * blits that buffer straight to SDL / the Linux framebuffer with no per-pixel
 * conversion, and LvglRuntime refuses any other depth at start-up. Changing it
 * here without changing every backend produces garbled output.
 */
#ifndef LV_CONF_H
#define LV_CONF_H

#include <stdint.h>

/*====================
   COLOR & MEMORY
 *====================*/

/* Since 9.5 LVGL derives LV_COLOR_DEPTH from this, and defining the depth directly
 * is deprecated (with a #warning that MSVC's C11 mode treats as an error). */
#define LV_COLOR_FORMAT_DEFAULT LV_COLOR_FORMAT_XRGB8888

/* The host has a real allocator; the built-in pool allocator would need a
 * fixed LV_MEM_SIZE tuned per screen size, which we do not want to hard-code. */
#define LV_USE_STDLIB_MALLOC  LV_STDLIB_CLIB
#define LV_USE_STDLIB_STRING  LV_STDLIB_CLIB
#define LV_USE_STDLIB_SPRINTF LV_STDLIB_CLIB

/* Decoded images are cached so a PNG is not re-inflated on every redraw. 1 MiB holds a
 * screen's worth of icons at 800x480 and stays small enough for a 1 GB Pi. */
#define LV_CACHE_DEF_SIZE (1024 * 1024)
#define LV_IMAGE_HEADER_CACHE_DEF_CNT 16

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
#define LV_USE_LED        1
#define LV_USE_SPINBOX    1

/*====================
   FILES, IMAGES, FONTS
 *====================*/

/* One drive letter onto the C library's fopen. LvImage and LvFont prefix every path with
 * "A:" themselves: without a letter LVGL would read the "C" of a Windows path as the drive. */
#define LV_USE_FS_STDIO    1
#define LV_FS_STDIO_LETTER 'A'
#define LV_FS_STDIO_PATH   ""

/* PNG and BMP decoding inside the native library, so the managed core needs no image codec. */
#define LV_USE_LODEPNG 1
#define LV_USE_BMP     1

/* TrueType at run time, for scripts the built-in Montserrat does not cover. */
#define LV_USE_TINY_TTF          1
#define LV_TINY_TTF_FILE_SUPPORT 1

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
/* The 9.6 benchmark draws at 26 px as well; only linked in when the demos are. */
#define LV_FONT_MONTSERRAT_26 1
#define LV_USE_PERF_MONITOR   0
#endif

#endif /* LV_CONF_H */
