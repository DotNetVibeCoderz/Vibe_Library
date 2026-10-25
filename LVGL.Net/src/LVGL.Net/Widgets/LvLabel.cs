using Lvgl.Interop;

namespace Lvgl.Widgets;

/// <summary>A text label.</summary>
public sealed class LvLabel : LvObject
{
    /// <summary>Creates a label on <paramref name="parent"/>.</summary>
    public LvLabel(LvObject? parent) : base(LvglNative.lv_label_create(ResolveParent(parent))) { }

    /// <summary>Creates a label with initial text.</summary>
    public LvLabel(LvObject? parent, string text) : this(parent) => Text = text;

    /// <summary>Wraps a label LVGL created itself, such as a list heading.</summary>
    internal LvLabel(nint handle) : base(handle) { }

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

    /// <summary>
    /// Enables in-line colour commands: <c>"#FF8800 warm# and plain"</c> draws "warm" in orange.
    /// </summary>
    /// <remarks>
    /// With this on, every <c>#</c> starts or ends a command and LVGL has no escape for a literal
    /// one, so keep it off for text that may contain <c>#</c>. <see cref="Recolored"/> builds the
    /// commands.
    /// </remarks>
    public bool Recolor
    {
        get => LvglNative.lv_label_get_recolor(Handle);
        set => LvglNative.lv_label_set_recolor(Handle, value);
    }

    /// <summary>Wraps <paramref name="text"/> in a colour command for a label with <see cref="Recolor"/> on.</summary>
    public static string Recolored(string text, Drawing.LvColor color) => $"#{color.Rgb:X6} {text}#";
}
