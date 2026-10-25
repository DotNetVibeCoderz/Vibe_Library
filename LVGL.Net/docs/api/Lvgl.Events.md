# Lvgl.Events

[API reference](README.md) · generated from the XML documentation; do not edit by hand.

## LvEventArgs

`sealed class` in `LVGL.Net`

Payload handed to LVGL event handlers.

| Member | Description |
|--------|-------------|
| `LvEventArgs(LvEventCode code, nint nativeEvent, nint target)` | Payload handed to LVGL event handlers. |
| `LvEventCode Code { get; }` | Which event fired. |
| `LvDir GestureDirection { get; }` | For [`Gesture`](Lvgl.md#lveventcode): the direction of the swipe. [`None`](Lvgl.md#lvdir) otherwise. Read it inside the handler. |
| `uint Key { get; }` | For [`Key`](Lvgl.md#lveventcode): the key pressed - an [`LvKey`](Lvgl.Input.md#lvkey) code or a Unicode code point. 0 for other events. Read it inside the handler. |
| `nint NativeEvent { get; }` | Native `lv_event_t*`. Valid only for the duration of the handler; useful when calling an LVGL API that LVGL.Net does not wrap yet. |
| `nint Target { get; }` | Native handle of the object the event is delivered to. |
| `bool TryGetPointer(out int x, out int y)` | Where the pointer (mouse or touch) driving this event is, in screen coordinates. False when no pointer is involved, e.g. for a key or a programmatic event. Read it inside the handler. |

