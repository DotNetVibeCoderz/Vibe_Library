using Lvgl.Interop;

namespace Lvgl.Widgets;

/// <summary>
/// A vertical, scrollable menu of buttons with optional section headings - the usual settings
/// screen on a touch panel.
/// </summary>
public sealed class LvList : LvObject
{
    /// <summary>Creates a list on <paramref name="parent"/>.</summary>
    public LvList(LvObject? parent) : base(LvglNative.lv_list_create(ResolveParent(parent))) { }

    /// <summary>Adds a section heading.</summary>
    public LvLabel AddText(string text) =>
        new(LvglNative.lv_list_add_text(Handle, text ?? string.Empty));

    /// <summary>Adds a button, optionally with a leading <see cref="LvSymbols"/> icon.</summary>
    /// <remarks>Handle the click with <see cref="LvObject.Clicked"/> on the returned button.</remarks>
    public LvListButton AddButton(string text, string? symbol = null) =>
        new(this, LvglNative.lv_list_add_button(Handle, string.IsNullOrEmpty(symbol) ? null : symbol, text ?? string.Empty));
}

/// <summary>A button inside an <see cref="LvList"/>.</summary>
public sealed class LvListButton : LvObject
{
    private readonly LvList _list;

    internal LvListButton(LvList list, nint handle) : base(handle) => _list = list;

    /// <summary>The caption.</summary>
    public string Text
    {
        get => LvglNative.ReadUtf8(LvglNative.lv_list_get_button_text(_list.Handle, Handle));
        set => LvglNative.lv_list_set_button_text(_list.Handle, Handle, value ?? string.Empty);
    }
}
