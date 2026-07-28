using Lvgl.Interop;

namespace Lvgl.Widgets;

/// <summary>An editable text field.</summary>
public sealed class LvTextArea : LvObject
{
    /// <summary>Creates a text area on <paramref name="parent"/>.</summary>
    public LvTextArea(LvObject? parent) : base(LvglNative.lv_textarea_create(ResolveParent(parent))) { }

    /// <summary>The current contents.</summary>
    public string Text
    {
        get => LvglNative.ReadUtf8(LvglNative.lv_textarea_get_text(Handle));
        set => LvglNative.lv_textarea_set_text(Handle, value ?? string.Empty);
    }

    /// <summary>Hint shown while the field is empty.</summary>
    public string PlaceholderText
    {
        set => LvglNative.lv_textarea_set_placeholder_text(Handle, value ?? string.Empty);
    }

    /// <summary>Restricts the field to a single line, suppressing the Enter key.</summary>
    public bool IsSingleLine
    {
        set => LvglNative.lv_textarea_set_one_line(Handle, value);
    }
}
