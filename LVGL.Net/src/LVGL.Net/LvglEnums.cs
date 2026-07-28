namespace Lvgl;

/// <summary>Alignment of an object inside its parent (or relative to another object).</summary>
public enum LvAlign
{
    Default = 0,
    TopLeft = 1,
    TopMid = 2,
    TopRight = 3,
    BottomLeft = 4,
    BottomMid = 5,
    BottomRight = 6,
    LeftMid = 7,
    RightMid = 8,
    Center = 9,
    OutTopLeft = 10,
    OutTopMid = 11,
    OutTopRight = 12,
    OutBottomLeft = 13,
    OutBottomMid = 14,
    OutBottomRight = 15,
    OutLeftTop = 16,
    OutLeftMid = 17,
    OutLeftBottom = 18,
    OutRightTop = 19,
    OutRightMid = 20,
    OutRightBottom = 21,
}

/// <summary>
/// The visual part of a widget a style applies to. Combine with <see cref="LvState"/> to form the
/// style selector, e.g. <c>LvPart.Knob | (uint)LvState.Pressed</c>.
/// </summary>
[Flags]
public enum LvPart : uint
{
    Main = 0x000000,
    Scrollbar = 0x010000,
    Indicator = 0x020000,
    Knob = 0x030000,
    Selected = 0x040000,
    Items = 0x050000,
    Cursor = 0x060000,
    Any = 0x0F0000,
}

/// <summary>Interaction state of a widget.</summary>
[Flags]
public enum LvState : ushort
{
    Default = 0x0000,
    Checked = 0x0001,
    Focused = 0x0002,
    FocusKey = 0x0004,
    Edited = 0x0008,
    Hovered = 0x0010,
    Pressed = 0x0020,
    Scrolled = 0x0040,
    Disabled = 0x0080,
    Any = 0xFFFF,
}

/// <summary>Per-object behaviour switches.</summary>
[Flags]
public enum LvObjFlag : uint
{
    Hidden = 1u << 0,
    Clickable = 1u << 1,
    ClickFocusable = 1u << 2,
    Checkable = 1u << 3,
    Scrollable = 1u << 4,
    ScrollElastic = 1u << 5,
    ScrollMomentum = 1u << 6,
    ScrollOne = 1u << 7,
    ScrollChainHorizontal = 1u << 8,
    ScrollChainVertical = 1u << 9,
    ScrollOnFocus = 1u << 10,
    ScrollWithArrow = 1u << 11,
    Snappable = 1u << 12,
    PressLock = 1u << 13,
    EventBubble = 1u << 14,
    GestureBubble = 1u << 15,
    AdvancedHitTest = 1u << 16,
    IgnoreLayout = 1u << 17,
    Floating = 1u << 18,
    OverflowVisible = 1u << 20,
}

/// <summary>
/// Event kinds. These are LVGL.Net's own stable identifiers; the native code translates them into
/// this LVGL build's <c>lv_event_code_t</c>, whose ordinals differ between point releases.
/// </summary>
public enum LvEventCode
{
    All = 0,
    Pressed = 1,
    Pressing = 2,
    PressLost = 3,
    ShortClicked = 4,
    LongPressed = 5,
    LongPressedRepeat = 6,
    Clicked = 7,
    Released = 8,
    ScrollBegin = 9,
    ScrollEnd = 10,
    Scroll = 11,
    Gesture = 12,
    Key = 13,
    Focused = 14,
    Defocused = 15,
    Leave = 16,
    ValueChanged = 17,
    Insert = 18,
    Refresh = 19,
    Ready = 20,
    Cancel = 21,
    Delete = 22,
    ChildChanged = 23,
    SizeChanged = 24,
    StyleChanged = 25,
    ScreenLoaded = 26,
    ScreenUnloaded = 27,
}

/// <summary>How LVGL hands rendered pixels to the backend.</summary>
public enum LvRenderMode
{
    /// <summary>Render into a small buffer and flush it in horizontal slices. Lowest memory use.</summary>
    Partial = 0,
    /// <summary>Render into a full-screen buffer; only changed areas are redrawn.</summary>
    Direct = 1,
    /// <summary>Redraw the whole screen into a full-screen buffer every frame.</summary>
    Full = 2,
}

/// <summary>Kind of input device fed into LVGL.</summary>
public enum LvIndevType
{
    None = 0,
    Pointer = 1,
    Keypad = 2,
    Button = 3,
    Encoder = 4,
}

/// <summary>How a label handles text that does not fit.</summary>
public enum LvLabelLongMode
{
    Wrap = 0,
    Dot = 1,
    Scroll = 2,
    ScrollCircular = 3,
    Clip = 4,
}

/// <summary>Text alignment inside a widget.</summary>
public enum LvTextAlign
{
    Auto = 0,
    Left = 1,
    Center = 2,
    Right = 3,
}

/// <summary>Direction and wrapping of a flex layout.</summary>
public enum LvFlexFlow
{
    Row = 0x00,
    Column = 0x01,
    RowWrap = 0x04,
    RowReverse = 0x08,
    RowWrapReverse = 0x0C,
    ColumnWrap = 0x05,
    ColumnReverse = 0x09,
    ColumnWrapReverse = 0x0D,
}

/// <summary>Distribution of items along a flex axis.</summary>
public enum LvFlexAlign
{
    Start = 0,
    End = 1,
    Center = 2,
    SpaceEvenly = 3,
    SpaceAround = 4,
    SpaceBetween = 5,
}

/// <summary>Scrollbar visibility policy.</summary>
public enum LvScrollbarMode
{
    Off = 0,
    On = 1,
    Active = 2,
    Auto = 3,
}

/// <summary>Chart plot style.</summary>
public enum LvChartType
{
    None = 0,
    Line = 1,
    Bar = 2,
    Scatter = 3,
}

/// <summary>Axis a chart series is bound to.</summary>
public enum LvChartAxis
{
    PrimaryY = 0x00,
    SecondaryY = 0x01,
    PrimaryX = 0x02,
    SecondaryX = 0x04,
}

/// <summary>How new chart points are inserted.</summary>
public enum LvChartUpdateMode
{
    /// <summary>Old points shift left as new ones arrive - the usual choice for live telemetry.</summary>
    Shift = 0,
    /// <summary>New points overwrite the oldest in place, like an oscilloscope sweep.</summary>
    Circular = 1,
}

/// <summary>Roller scrolling behaviour.</summary>
public enum LvRollerMode
{
    Normal = 0,
    Infinite = 1,
}
