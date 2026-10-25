# Lvgl.Events

[API reference](README.md) · generated from the XML documentation; do not edit by hand.

## LvEventArgs

`sealed class` in `LVGL.Net`

Payload handed to LVGL event handlers.

| Member | Description |
|--------|-------------|
| `LvEventArgs(LvEventCode code, nint nativeEvent, nint target)` | Payload handed to LVGL event handlers. |
| `LvEventCode Code { get; }` | Which event fired. |
| `nint NativeEvent { get; }` | Native `lv_event_t*`. Valid only for the duration of the handler; useful when calling an LVGL API that LVGL.Net does not wrap yet. |
| `nint Target { get; }` | Native handle of the object the event is delivered to. |

