namespace Lvgl.Input;

/// <summary>
/// LVGL's control-key codes (<c>LV_KEY_*</c>), as reported by <see cref="Events.LvEventArgs.Key"/>.
/// Printable keys arrive as their Unicode code point.
/// </summary>
/// <remarks>
/// <c>lv_key_t</c> in <c>lv_group.h</c> gives every member an explicit value (they are ASCII control
/// characters), so unlike <c>lv_event_code_t</c> they do not move between LVGL releases.
/// </remarks>
public static class LvKey
{
    /// <summary>Arrow up.</summary>
    public const uint Up = 17;
    /// <summary>Arrow down.</summary>
    public const uint Down = 18;
    /// <summary>Arrow right.</summary>
    public const uint Right = 19;
    /// <summary>Arrow left.</summary>
    public const uint Left = 20;
    /// <summary>Escape.</summary>
    public const uint Escape = 27;
    /// <summary>Delete.</summary>
    public const uint Delete = 127;
    /// <summary>Backspace.</summary>
    public const uint Backspace = 8;
    /// <summary>Enter or Return.</summary>
    public const uint Enter = 10;
    /// <summary>Tab: focus the next widget.</summary>
    public const uint Next = 9;
    /// <summary>Shift+Tab: focus the previous widget.</summary>
    public const uint Previous = 11;
    /// <summary>Home.</summary>
    public const uint Home = 2;
    /// <summary>End.</summary>
    public const uint End = 3;
}
