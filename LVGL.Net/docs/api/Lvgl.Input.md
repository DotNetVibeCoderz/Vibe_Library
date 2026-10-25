# Lvgl.Input

[API reference](README.md) · generated from the XML documentation; do not edit by hand.

## LvGroup

`sealed class` in `LVGL.Net`

A focus group: the set of widgets a keyboard or encoder can move between.

LVGL routes key input through a group rather than to widgets directly. A keypad device with no
group has nowhere to deliver its input, so typing does nothing at all - no error, no warning.
[`LvglApplication`](Lvgl.md#lvglapplication) therefore creates one automatically whenever
[`EnableKeyboard`](Lvgl.md#lvgloptions) is set.





Making a group the default is what gets widgets into it: LVGL adds every focusable widget to
the default group as it is constructed. That has to happen **before** the widgets are
created, which is why the application sets it up during construction rather than on demand.

| Member | Description |
|--------|-------------|
| `nint Handle { get; }` | Native `lv_group_t*`. |
| `void Add(LvObject widget)` | Adds an existing widget to the group. |
| `static LvGroup CreateDefault()` | Creates a group and makes it the default, so widgets created afterwards join it automatically. |
| `static LvGroup Create()` | Creates an empty group. |
| `void Dispose()` |  |
| `static void Focus(LvObject widget)` | Moves focus to a widget, which must already be in a group. |
| `void MakeDefault()` | Makes this the group new focusable widgets join on creation. Widgets that already exist are unaffected - add them with [`Add`](#lvgroup). |
| `static void Remove(LvObject widget)` | Removes a widget from whatever group it belongs to. LVGL does this itself when a widget is deleted, so this is only needed to take a live widget out of the focus order. |

## LvglInputDevice

`sealed class` in `LVGL.Net`

Feeds pointer and key state from an [`ILvglBackend`](Lvgl.Backends.md#ilvglbackend) into a native
`lv_indev_t`.

LVGL polls input devices from inside `lv_timer_handler` rather than accepting pushed
events, so this class is a pull adapter: the backend keeps the latest state, and the read
callback samples it whenever LVGL asks.

| Member | Description |
|--------|-------------|
| `nint Handle { get; }` | Native `lv_indev_t*`. |
| `LvIndevType Type { get; }` | Kind of device this adapter drives. |
| `void Dispose()` |  |
| `void SetGroup(LvGroup group)` | Binds this device to a focus group. Required for keypad and encoder devices: LVGL delivers their input to the focused widget of a group, so without one the device is inert. |

