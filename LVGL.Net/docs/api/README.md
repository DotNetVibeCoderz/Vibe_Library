# LVGL.Net API reference

Generated from the XML documentation by `tools/LVGL.ApiDocs`; do not edit by hand.
Regenerate with `dotnet run --project tools/LVGL.ApiDocs` after changing a public API.

Di port oleh Kang Fadhil dari Gravicode Studios.

## [Lvgl](Lvgl.md)

Package: LVGL.Net

| Type | Kind | Summary |
|------|------|---------|
| [LvAlign](Lvgl.md#lvalign) | enum | Alignment of an object inside its parent (or relative to another object). |
| [LvAnimPath](Lvgl.md#lvanimpath) | enum | Easing curve of an animation. |
| [LvAnimation](Lvgl.md#lvanimation) | sealed class | Animates an integer from one value to another over time, driven by LVGL's own animation timer. |
| [LvAnimations](Lvgl.md#lvanimations) | static class | Shortcuts for animating common widget properties. |
| [LvChartAxis](Lvgl.md#lvchartaxis) | enum | Axis a chart series is bound to. |
| [LvChartType](Lvgl.md#lvcharttype) | enum | Chart plot style. |
| [LvChartUpdateMode](Lvgl.md#lvchartupdatemode) | enum | How new chart points are inserted. |
| [LvDir](Lvgl.md#lvdir) | enum | A direction, or a set of them (tab bar side, allowed tile scroll directions). |
| [LvEventCode](Lvgl.md#lveventcode) | enum | Event kinds. |
| [LvFlexAlign](Lvgl.md#lvflexalign) | enum | Distribution of items along a flex axis. |
| [LvFlexFlow](Lvgl.md#lvflexflow) | enum | Direction and wrapping of a flex layout. |
| [LvImageAlign](Lvgl.md#lvimagealign) | enum | How an image's pixels are placed inside the widget when the sizes differ. |
| [LvIndevType](Lvgl.md#lvindevtype) | enum | Kind of input device fed into LVGL. |
| [LvKeyboardMode](Lvgl.md#lvkeyboardmode) | enum | Key layout shown by an on-screen keyboard. |
| [LvLabelLongMode](Lvgl.md#lvlabellongmode) | enum | How a label handles text that does not fit. |
| [LvObjFlag](Lvgl.md#lvobjflag) | enum | Per-object behaviour switches. |
| [LvPart](Lvgl.md#lvpart) | enum | The visual part of a widget a style applies to. |
| [LvRenderMode](Lvgl.md#lvrendermode) | enum | How LVGL hands rendered pixels to the backend. |
| [LvRollerMode](Lvgl.md#lvrollermode) | enum | Roller scrolling behaviour. |
| [LvScaleMode](Lvgl.md#lvscalemode) | enum | Shape and tick side of a scale. |
| [LvScrollbarMode](Lvgl.md#lvscrollbarmode) | enum | Scrollbar visibility policy. |
| [LvState](Lvgl.md#lvstate) | enum | Interaction state of a widget. |
| [LvSymbols](Lvgl.md#lvsymbols) | static class | Icon glyphs from LVGL's built-in symbol font, for use inside label and button text. |
| [LvTextAlign](Lvgl.md#lvtextalign) | enum | Text alignment inside a widget. |
| [LvTimer](Lvgl.md#lvtimer) | sealed class | Runs a callback periodically on the LVGL thread, inside the run loop. |
| [LvglAbout](Lvgl.md#lvglabout) | static class | Provenance of this library, for about screens, log banners and `--version` output. |
| [LvglApplication](Lvgl.md#lvglapplication) | sealed class | Owns the LVGL run loop: the tick source, the timer handler, the display and the input devices. |
| [LvglCallbackFailedEventArgs](Lvgl.md#lvglcallbackfailedeventargs) | sealed class | Describes an exception caught where LVGL called into managed code. |
| [LvglDemos](Lvgl.md#lvgldemos) | static class | The demo scenes bundled with LVGL, available when the native library was built with |
| [LvglException](Lvgl.md#lvglexception) | class | Error raised by the LVGL.Net wrapper. |
| [LvglOptions](Lvgl.md#lvgloptions) | sealed class | Tuning knobs for [`LvglApplication`](#lvglapplication). |
| [LvglRuntime](Lvgl.md#lvglruntime) | static class | Process-wide LVGL state: initialisation, version checks and UI-thread affinity. |
| [LvglThreadAccessException](Lvgl.md#lvglthreadaccessexception) | sealed class | Thrown when LVGL is touched from a thread that does not own it. |

## [Lvgl.Backends](Lvgl.Backends.md)

Package: LVGL.Net

| Type | Kind | Summary |
|------|------|---------|
| [HeadlessBackend](Lvgl.Backends.md#headlessbackend) | sealed class | Renders into a managed pixel buffer instead of a screen. |
| [ILvglBackend](Lvgl.Backends.md#ilvglbackend) | interface | Everything platform-specific that LVGL.Net needs: somewhere to put pixels and somewhere to get |
| [ILvglKeyboardSource](Lvgl.Backends.md#ilvglkeyboardsource) | interface | Implemented by backends that can also deliver key presses to LVGL. |
| [LvPointerState](Lvgl.Backends.md#lvpointerstate) | readonly struct | Current state of the pointing device (mouse, touch panel, ...). |

## [Lvgl.Display](Lvgl.Display.md)

Package: LVGL.Net

| Type | Kind | Summary |
|------|------|---------|
| [LvglDisplay](Lvgl.Display.md#lvgldisplay) | sealed class | Binds an [`ILvglBackend`](Lvgl.Backends.md#ilvglbackend) to a native `lv_display_t` and owns its draw buffers. |

## [Lvgl.Drawing](Lvgl.Drawing.md)

Package: LVGL.Net

| Type | Kind | Summary |
|------|------|---------|
| [LvArea](Lvgl.Drawing.md#lvarea) | readonly struct | An inclusive rectangle, matching LVGL's `lv_area_t` semantics: [`X2`](#lvarea) and |
| [LvColor](Lvgl.Drawing.md#lvcolor) | readonly struct | A 24-bit RGB colour. |
| [LvCoord](Lvgl.Drawing.md#lvcoord) | static class | Helpers for LVGL's "special" coordinate values. |
| [LvFileSystem](Lvgl.Drawing.md#lvfilesystem) | static class | Maps host paths onto LVGL's drive-letter file system. |
| [LvFont](Lvgl.Drawing.md#lvfont) | sealed class | A font: one of the Montserrat sizes compiled into the native library, or a TrueType font |
| [LvImageSource](Lvgl.Drawing.md#lvimagesource) | sealed class | Pixels or a file for an [`LvImage`](Lvgl.Widgets.md#lvimage) to show. |

## [Lvgl.Events](Lvgl.Events.md)

Package: LVGL.Net

| Type | Kind | Summary |
|------|------|---------|
| [LvEventArgs](Lvgl.Events.md#lveventargs) | sealed class | Payload handed to LVGL event handlers. |

## [Lvgl.Input](Lvgl.Input.md)

Package: LVGL.Net

| Type | Kind | Summary |
|------|------|---------|
| [LvGroup](Lvgl.Input.md#lvgroup) | sealed class | A focus group: the set of widgets a keyboard or encoder can move between. |
| [LvglInputDevice](Lvgl.Input.md#lvglinputdevice) | sealed class | Feeds pointer and key state from an [`ILvglBackend`](Lvgl.Backends.md#ilvglbackend) into a native |

## [Lvgl.Interop](Lvgl.Interop.md)

Package: LVGL.Net

| Type | Kind | Summary |
|------|------|---------|
| [LvglNativeLibrary](Lvgl.Interop.md#lvglnativelibrary) | static class | Locates the `lvglnet` shared library. |
| [LvglNativeNotFoundException](Lvgl.Interop.md#lvglnativenotfoundexception) | sealed class | Thrown when the `lvglnet` shared library cannot be found or loaded. |

## [Lvgl.Linux](Lvgl.Linux.md)

Package: LVGL.Net.Linux

| Type | Kind | Summary |
|------|------|---------|
| [DrmBackend](Lvgl.Linux.md#drmbackend) | sealed class | Draws LVGL through the kernel's DRM/KMS interface (`/dev/dri/card*`) with double |
| [EvdevPointer](Lvgl.Linux.md#evdevpointer) | sealed class | Reads a touchscreen or mouse through the Linux evdev interface (`/dev/input/eventN`). |
| [FramebufferBackend](Lvgl.Linux.md#framebufferbackend) | sealed class | Draws LVGL straight into a Linux framebuffer device (`/dev/fb0`), with no window system. |
| [IPointerSource](Lvgl.Linux.md#ipointersource) | interface | A source of pointer state that must be polled once per frame. |

## [Lvgl.Sdl](Lvgl.Sdl.md)

Package: LVGL.Net.Sdl

| Type | Kind | Summary |
|------|------|---------|
| [SdlBackend](Lvgl.Sdl.md#sdlbackend) | sealed class | Renders LVGL into an SDL2 window and feeds it mouse and keyboard input. |

## [Lvgl.Styling](Lvgl.Styling.md)

Package: LVGL.Net

| Type | Kind | Summary |
|------|------|---------|
| [LvStyle](Lvgl.Styling.md#lvstyle) | sealed class | A reusable LVGL style that can be applied to many widgets. |

## [Lvgl.Ui](Lvgl.Ui.md)

Package: LVGL.Net.Ui

| Type | Kind | Summary |
|------|------|---------|
| [CSharpUiGenerator](Lvgl.Ui.md#csharpuigenerator) | sealed class | Emits C# that rebuilds a [`UiDocument`](#uidocument) with the LVGL.Net widget API. |
| [UiBuilder](Lvgl.Ui.md#uibuilder) | sealed class | Turns a [`UiDocument`](#uidocument) into live LVGL widgets. |
| [UiDocument](Lvgl.Ui.md#uidocument) | sealed class | A saved screen layout. |
| [UiJson](Lvgl.Ui.md#uijson) | static class | Loads and saves [`UiDocument`](#uidocument) files (`*.lvgl.json`). |
| [UiNode](Lvgl.Ui.md#uinode) | sealed class | One widget in a [`UiDocument`](#uidocument). |
| [UiStyle](Lvgl.Ui.md#uistyle) | sealed class | A shared style: a set of properties defined once and applied to many widgets. |
| [UiSymbols](Lvgl.Ui.md#uisymbols) | static class | The [`LvSymbols`](Lvgl.md#lvsymbols) glyphs by name, so a layout document can say `"Symbol": "Wifi"`. |
| [UiWidgetType](Lvgl.Ui.md#uiwidgettype) | enum | Widget kinds the designer and the loader understand. |

## [Lvgl.Widgets](Lvgl.Widgets.md)

Package: LVGL.Net

| Type | Kind | Summary |
|------|------|---------|
| [LvArc](Lvgl.Widgets.md#lvarc) | sealed class | A circular arc, used both as a dial-style indicator and as a rotary input. |
| [LvBar](Lvgl.Widgets.md#lvbar) | sealed class | A read-only progress bar. |
| [LvButton](Lvgl.Widgets.md#lvbutton) | sealed class | A push button. |
| [LvChart](Lvgl.Widgets.md#lvchart) | sealed class | A line, bar or scatter chart. |
| [LvChartSeries](Lvgl.Widgets.md#lvchartseries) | sealed class | One data series inside an [`LvChart`](#lvchart). |
| [LvCheckbox](Lvgl.Widgets.md#lvcheckbox) | sealed class | A checkbox with a caption. |
| [LvDropdown](Lvgl.Widgets.md#lvdropdown) | sealed class | A drop-down list. |
| [LvImage](Lvgl.Widgets.md#lvimage) | sealed class | Shows a picture: a PNG or BMP file, pixels produced at run time, or a built-in symbol. |
| [LvKeyboard](Lvgl.Widgets.md#lvkeyboard) | sealed class | An on-screen keyboard that types into an [`LvTextArea`](#lvtextarea) - text entry on a touch |
| [LvLabel](Lvgl.Widgets.md#lvlabel) | sealed class | A text label. |
| [LvLed](Lvgl.Widgets.md#lvled) | sealed class | A round status light with adjustable colour and brightness. |
| [LvList](Lvgl.Widgets.md#lvlist) | sealed class | A vertical, scrollable menu of buttons with optional section headings - the usual settings |
| [LvListButton](Lvgl.Widgets.md#lvlistbutton) | sealed class | A button inside an [`LvList`](#lvlist). |
| [LvMessageBox](Lvgl.Widgets.md#lvmessagebox) | sealed class | A dialog with a title, text and buttons. |
| [LvObject](Lvgl.Widgets.md#lvobject) | class | Managed wrapper around an `lv_obj_t` - the base of every LVGL widget. |
| [LvPanel](Lvgl.Widgets.md#lvpanel) | sealed class | A plain container. |
| [LvRoller](Lvgl.Widgets.md#lvroller) | sealed class | A scrolling picker, well suited to touch input. |
| [LvScale](Lvgl.Widgets.md#lvscale) | sealed class | A ruler or gauge face: ticks and labels along a line or an arc. |
| [LvScreen](Lvgl.Widgets.md#lvscreen) | sealed class | A root object. |
| [LvSlider](Lvgl.Widgets.md#lvslider) | sealed class | A draggable slider. |
| [LvSpinbox](Lvgl.Widgets.md#lvspinbox) | sealed class | A numeric field stepped digit by digit - set points, thresholds, calibration values. |
| [LvSpinner](Lvgl.Widgets.md#lvspinner) | sealed class | A rotating arc that says "working on it". |
| [LvSwitch](Lvgl.Widgets.md#lvswitch) | sealed class | An on/off switch. |
| [LvTabPage](Lvgl.Widgets.md#lvtabpage) | sealed class | One page of an [`LvTabView`](#lvtabview); an ordinary container for the tab's widgets. |
| [LvTabView](Lvgl.Widgets.md#lvtabview) | sealed class | Pages behind a row of tabs. |
| [LvTable](Lvgl.Widgets.md#lvtable) | sealed class | A grid of text cells. |
| [LvTextArea](Lvgl.Widgets.md#lvtextarea) | sealed class | An editable text field. |
| [LvTile](Lvgl.Widgets.md#lvtile) | sealed class | One page of an [`LvTileView`](#lvtileview); an ordinary container for its widgets. |
| [LvTileView](Lvgl.Widgets.md#lvtileview) | sealed class | Full-size pages laid out on a grid that the user swipes between - a phone-style home screen. |

