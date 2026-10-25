using Lvgl.Drawing;
using Lvgl.Interop;

namespace Lvgl.Widgets;

/// <summary>
/// Shows a picture: a PNG or BMP file, pixels produced at run time, or a built-in symbol.
/// </summary>
/// <remarks>
/// The widget sizes itself to the source unless given an explicit size; with an explicit size,
/// <see cref="InnerAlign"/> decides whether the pixels are centred, stretched, tiled or fitted.
/// A pixel <see cref="LvImageSource"/> must outlive the widget - see its remarks.
/// </remarks>
public sealed class LvImage : LvObject
{
    private LvImageSource? _source;

    /// <summary>Creates an empty image on <paramref name="parent"/>.</summary>
    public LvImage(LvObject? parent) : base(LvglNative.lv_image_create(ResolveParent(parent))) { }

    /// <summary>Creates an image showing <paramref name="source"/>.</summary>
    public LvImage(LvObject? parent, LvImageSource source) : this(parent) => Source = source;

    /// <summary>The source being shown, or null for none or for a symbol.</summary>
    public LvImageSource? Source
    {
        get => _source;
        set
        {
            if (value is null)
            {
                LvglNative.lv_image_set_src(Handle, 0);
            }
            else if (value.IsFile)
            {
                LvglNative.lv_image_set_src_text(Handle, LvFileSystem.ToLvglPath(value.FilePath!));
            }
            else
            {
                LvglNative.lv_image_set_src(Handle, value.Descriptor);
            }

            // Held so the source is reachable for as long as the image is - a convenience, not a
            // substitute for disposing it after the image is gone.
            _source = value;
        }
    }

    /// <summary>Shows a file directly. Equivalent to <c>Source = LvImageSource.FromFile(path)</c>.</summary>
    public void SetFile(string path) => Source = LvImageSource.FromFile(path);

    /// <summary>
    /// Shows a built-in symbol glyph from <see cref="LvSymbols"/>, drawn in the text colour.
    /// </summary>
    public void SetSymbol(string symbol)
    {
        ArgumentException.ThrowIfNullOrEmpty(symbol);
        LvglNative.lv_image_set_src_text(Handle, symbol);
        _source = null;
    }

    /// <summary>Rotation in tenths of a degree (900 is a quarter turn), about <see cref="SetPivot"/>.</summary>
    public int Rotation
    {
        get => LvglNative.lv_image_get_rotation(Handle);
        set => LvglNative.lv_image_set_rotation(Handle, value);
    }

    /// <summary>Zoom where 256 is 100 %, 512 is 200 % and 128 is 50 %.</summary>
    public int Scale
    {
        get => LvglNative.lv_image_get_scale(Handle);
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            LvglNative.lv_image_set_scale(Handle, (uint)value);
        }
    }

    /// <summary>Sets the point rotation and zoom are applied around, relative to the image.</summary>
    public void SetPivot(int x, int y) => LvglNative.lv_image_set_pivot(Handle, x, y);

    /// <summary>Placement of the pixels when the widget's size differs from the source's.</summary>
    public LvImageAlign InnerAlign
    {
        set => LvglNative.lv_image_set_inner_align(Handle, (int)value);
    }

    /// <summary>Smooths rotated and scaled images, at some rendering cost.</summary>
    public bool Antialias
    {
        set => LvglNative.lv_image_set_antialias(Handle, value);
    }

    /// <summary>Width of the source in pixels, once LVGL has read it.</summary>
    public int SourceWidth => LvglNative.lv_image_get_src_width(Handle);

    /// <summary>Height of the source in pixels, once LVGL has read it.</summary>
    public int SourceHeight => LvglNative.lv_image_get_src_height(Handle);

    protected override void OnDeleted() => _source = null;
}
