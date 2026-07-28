# Architecture

## The shape of the thing

```
        managed                                    native
  ┌───────────────────────┐              ┌────────────────────────┐
  │ Your application      │              │                        │
  ├───────────────────────┤   P/Invoke   │  lvglnet (one library) │
  │ LVGL.Net              │─────────────▶│    LVGL v9 sources     │
  │  widgets, styles,     │◀─────────────│    + lvn_* ABI shim    │
  │  events, run loop     │  callbacks   │                        │
  ├───────────────────────┤              └────────────────────────┘
  │ ILvglBackend          │
  │  SDL / framebuffer /  │   pixels out, input in
  │  headless             │
  └───────────────────────┘
```

The important design decision is where the platform boundary sits.

## Why the native library has no display drivers

LVGL ships drivers for SDL, X11, Wayland, framebuffer and more. LVGL.Net compiles **none** of
them. Instead the native build is a plain, driver-free LVGL: it renders into a buffer and calls a
flush callback, and everything after that — getting pixels onto a screen, reading a touch panel —
is managed code behind `ILvglBackend`.

This buys three things:

1. **One native artifact for every scenario.** The same `lvglnet` binary serves an SDL window, a
   Raspberry Pi framebuffer and the designer's off-screen preview. Adding a new output target is a
   C# class, not a recompile of C.
2. **No native dependency fan-out.** The library links nothing but libc and libm, so deploying to
   a Pi does not drag in an X11 stack.
3. **A testable seam.** `HeadlessBackend` renders into a `byte[]`, which is how the designer shows
   a true preview and how rendering can be asserted on in tests.

The cost is one `memcpy` per flushed region, which is what a driver would do anyway.

## The ABI shim

Most of LVGL's API is directly P/Invoke-able. A small part is not, and `native/shim/lvglnet_shim.c`
covers exactly that part:

| Problem | Why it breaks | Shim solution |
|---------|---------------|---------------|
| `lv_color_t` passed by value | A 3-byte struct goes in a register on SysV/AAPCS but by hidden pointer on Windows x64 | `lvn_*_color(..., uint32_t rgb, ...)` takes packed `0xRRGGBB` |
| `lv_style_t` allocated by the caller | Its size is a compile-time detail of `lv_conf.h` | `lvn_style_create` / `lvn_style_delete` |
| `lv_indev_data_t`, `lv_area_t` | Managed code would have to hard-code struct offsets | `lvn_indev_data_set_*`, `lvn_area_get` |
| `lv_font_montserrat_*` | Exported *variables*, which P/Invoke cannot bind portably | `lvn_font_montserrat(size)` |
| `lv_event_code_t` ordinals | LVGL inserted `LV_EVENT_ROTARY` mid-enum in 9.1, shifting later values | `lvn_event_code(stable_id)` translates at run time |
| `LV_PCT`, `LV_SIZE_CONTENT` | C macros do not exist in a shared library | Reimplemented in `LvCoord`, asserted by tests |

Everything else calls `lv_*` directly. The shim adds no per-frame cost: the render path
(`lv_timer_handler`, the flush callback) does not go through it.

`LVN_ABI_VERSION` is checked at startup, so a stale native build fails with a clear message instead
of corrupting memory.

## Threading

LVGL is single-threaded and unsynchronised. LVGL.Net records the thread that called
`LvglRuntime.Initialize` and enforces it:

```csharp
LvglRuntime.EnsureUiThread();     // throws LvglThreadAccessException from the wrong thread
```

Background work hands results back through the application's queue:

```csharp
// on a sensor thread
var reading = sensors.Read();
app.Post(() => dashboard.Update(reading));   // runs at the top of the next iteration
```

`Post` is fire-and-forget; `Invoke` blocks until the action has run.

## The run loop

One iteration of `LvglApplication.RunFrame`:

1. Drain the posted-work queue.
2. Feed LVGL the real elapsed milliseconds (`lv_tick_inc`), so animations stay correct when a frame
   runs long.
3. `lv_timer_handler()` — runs timers, redraws invalid areas, calls the flush callback for each
   region.
4. Rethrow anything the backend threw while LVGL was calling back into managed code.
5. `backend.PumpEvents()` — host events and fresh input state; returns false to quit.

`Run` then sleeps for `min(LVGL's suggested idle, MaxIdleSleepMs)`, clamped below by
`MinIdleSleepMs` so an idle UI does not spin a core.

## Callbacks into managed code

Callbacks are `[UnmanagedCallersOnly]` statics with unmanaged function pointers — no delegate
marshalling stub on the render path.

- **Display flush** resolves its instance from a `GCHandle` stored in the display's user data. This
  runs many times per frame, so it must not do a lookup.
- **Widget events** resolve through a static registry keyed by the native pointer. A `GCHandle`
  would be wrong here: LVGL fires callbacks *while* it tears an object down, and reading a freed
  `GCHandle` is undefined behaviour, whereas a missed dictionary lookup is a harmless no-op.

No managed exception is ever allowed to unwind into C. The flush path parks the exception and the
run loop rethrows it; event handlers swallow theirs.

## Colour format

`LV_COLOR_DEPTH` is fixed at 32. LVGL produces XRGB8888 (bytes B, G, R, X on little-endian), which
is copied unchanged to SDL's `ARGB8888` texture and to a 32-bit framebuffer, and reinterpreted as
WPF's `Bgr32` in the designer. `LvglRuntime.Initialize` refuses a native library built with a
different depth rather than drawing colour-swapped output.

## Project boundaries

- `LVGL.Net` depends on nothing but the BCL. No UI framework, no NuGet packages.
- `LVGL.Net.Sdl` and `LVGL.Net.Linux` are separate assemblies so a Pi deployment does not carry SDL
  bindings it will not use.
- `LVGL.Net.Ui` holds the layout model shared by the designer, the runtime loader and the code
  generator, so a previewed layout and a running one cannot drift apart.
- `LVGL.Designer` is the only Windows-only project, and only because WPF is.
