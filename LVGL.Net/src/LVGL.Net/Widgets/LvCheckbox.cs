using Lvgl.Interop;

namespace Lvgl.Widgets;

/// <summary>A checkbox with a caption.</summary>
public sealed class LvCheckbox : LvObject
{
    private string _text = string.Empty;

    /// <summary>Creates a checkbox on <paramref name="parent"/>.</summary>
    public LvCheckbox(LvObject? parent) : base(LvglNative.lv_checkbox_create(ResolveParent(parent))) { }

    /// <summary>Creates a checkbox with a caption.</summary>
    public LvCheckbox(LvObject? parent, string text) : this(parent) => Text = text;

    /// <summary>
    /// Caption text. Cached on the managed side because LVGL exposes no getter that is safe to
    /// call after the widget has been deleted.
    /// </summary>
    public string Text
    {
        get => _text;
        set
        {
            _text = value ?? string.Empty;
            LvglNative.lv_checkbox_set_text(Handle, _text);
        }
    }
}
