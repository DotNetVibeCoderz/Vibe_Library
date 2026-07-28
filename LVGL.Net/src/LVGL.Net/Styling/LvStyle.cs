using Lvgl.Drawing;
using Lvgl.Interop;

namespace Lvgl.Styling;

/// <summary>
/// A reusable LVGL style that can be applied to many widgets.
/// </summary>
/// <remarks>
/// <para>
/// Prefer this over per-widget style setters when the same look is applied repeatedly: LVGL stores
/// one property table and the widgets reference it, instead of every widget carrying its own copy.
/// </para>
/// <para>
/// <b>Lifetime matters.</b> LVGL stores a bare pointer to the style, so the style must outlive every
/// widget that uses it. Disposing a style that is still applied leaves dangling references and will
/// crash on the next redraw - keep styles in a field or a static, not in a <c>using</c> block that
/// closes before the UI does.
/// </para>
/// </remarks>
public sealed class LvStyle : IDisposable
{
    private nint _handle;

    /// <summary>Allocates a new, empty style.</summary>
    public LvStyle()
    {
        LvglRuntime.Initialize();

        _handle = LvglNative.lvn_style_create();
        if (_handle == 0) throw new LvglException("Could not allocate an LVGL style.");
    }

    /// <summary>Native <c>lv_style_t*</c>.</summary>
    public nint Handle => _handle != 0
        ? _handle
        : throw new ObjectDisposedException(nameof(LvStyle));

    public LvStyle BackgroundColor(LvColor color)
    {
        LvglNative.lvn_style_set_bg_color(Handle, color.Rgb);
        return this;
    }

    public LvStyle BackgroundGradientColor(LvColor color)
    {
        LvglNative.lvn_style_set_bg_grad_color(Handle, color.Rgb);
        return this;
    }

    public LvStyle TextColor(LvColor color)
    {
        LvglNative.lvn_style_set_text_color(Handle, color.Rgb);
        return this;
    }

    public LvStyle BorderColor(LvColor color)
    {
        LvglNative.lvn_style_set_border_color(Handle, color.Rgb);
        return this;
    }

    public LvStyle LineColor(LvColor color)
    {
        LvglNative.lvn_style_set_line_color(Handle, color.Rgb);
        return this;
    }

    public LvStyle ArcColor(LvColor color)
    {
        LvglNative.lvn_style_set_arc_color(Handle, color.Rgb);
        return this;
    }

    /// <summary>Selects a built-in Montserrat font size.</summary>
    public LvStyle FontSize(int size)
    {
        var font = LvglNative.lvn_font_montserrat((uint)size);
        if (font != 0) LvglNative.lvn_style_set_text_font(Handle, font);
        return this;
    }

    public LvStyle Radius(int radius)
    {
        LvglNative.lv_style_set_radius(Handle, radius);
        return this;
    }

    public LvStyle BorderWidth(int width)
    {
        LvglNative.lv_style_set_border_width(Handle, width);
        return this;
    }

    public LvStyle Padding(int padding)
    {
        LvglNative.lvn_style_set_pad_all(Handle, padding);
        return this;
    }

    /// <summary>Background opacity, 0 (transparent) to 255 (opaque).</summary>
    public LvStyle BackgroundOpacity(byte opacity)
    {
        LvglNative.lv_style_set_bg_opa(Handle, opacity);
        return this;
    }

    public LvStyle Size(int width, int height)
    {
        LvglNative.lv_style_set_width(Handle, width);
        LvglNative.lv_style_set_height(Handle, height);
        return this;
    }

    /// <summary>
    /// Frees the style. Only call this once every widget referencing it has been deleted.
    /// </summary>
    public void Dispose()
    {
        if (_handle == 0) return;
        LvglNative.lvn_style_delete(_handle);
        _handle = 0;
    }
}
