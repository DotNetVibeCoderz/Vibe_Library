using Lvgl.Interop;

namespace Lvgl.Widgets;

/// <summary>A text label.</summary>
public sealed class LvLabel : LvObject
{
    /// <summary>Creates a label on <paramref name="parent"/>.</summary>
    public LvLabel(LvObject? parent) : base(LvglNative.lv_label_create(ResolveParent(parent))) { }

    /// <summary>Creates a label with initial text.</summary>
    public LvLabel(LvObject? parent, string text) : this(parent) => Text = text;

    /// <summary>
    /// The displayed text. LVGL copies the string into its own buffer on assignment, so the
    /// managed string does not need to be kept alive.
    /// </summary>
    public string Text
    {
        get => LvglNative.ReadUtf8(LvglNative.lv_label_get_text(Handle));
        set => LvglNative.lv_label_set_text(Handle, value ?? string.Empty);
    }

    /// <summary>How text that does not fit the label's width is handled.</summary>
    public LvLabelLongMode LongMode
    {
        set => LvglNative.lv_label_set_long_mode(Handle, (int)value);
    }
}
