using System.Runtime.InteropServices;
using Lvgl.Interop;

namespace Lvgl.Drawing;

/// <summary>
/// A font: one of the Montserrat sizes compiled into the native library, or a TrueType font
/// rasterised at run time by LVGL's TinyTTF.
/// </summary>
/// <remarks>
/// <para>
/// TrueType is what makes scripts outside Montserrat's Latin range usable - Arabic, Thai, CJK -
/// and lets an application ship its own brand font without rebuilding the native library.
/// Glyphs are rendered on demand and cached, so the first frame that uses a new glyph costs more
/// than later ones.
/// </para>
/// <para>
/// <b>Lifetime matters</b>, exactly as for <see cref="Styling.LvStyle"/>: widgets keep a bare
/// pointer to the font, so dispose it only after every widget using it has been deleted. Built-in
/// fonts are owned by the native library and their <see cref="Dispose"/> does nothing.
/// </para>
/// </remarks>
public sealed unsafe class LvFont : IDisposable
{
    private nint _handle;
    private void* _data;
    private int _size;

    private LvFont(nint handle, int size, bool owned, void* data)
    {
        _handle = handle;
        _size = size;
        IsTrueType = owned;
        _data = data;
    }

    /// <summary>Native <c>lv_font_t*</c>.</summary>
    /// <exception cref="ObjectDisposedException">The font has been disposed.</exception>
    public nint Handle => _handle != 0 ? _handle : throw new ObjectDisposedException(nameof(LvFont));

    /// <summary>True for a TinyTTF font, false for a built-in bitmap font.</summary>
    public bool IsTrueType { get; }

    /// <summary>
    /// Pixel size. Settable for TrueType fonts, which re-rasterise; built-in fonts have one size.
    /// </summary>
    public int Size
    {
        get => _size;
        set
        {
            if (!IsTrueType) throw new InvalidOperationException("A built-in font has a fixed size; pick another with Montserrat(size).");
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            LvglNative.lv_tiny_ttf_set_size(Handle, value);
            _size = value;
        }
    }

    /// <summary>
    /// The closest built-in Montserrat size (12, 14, 16, 20, 24, 28, 36). Sizes that are not
    /// compiled in fall back to the default font rather than failing.
    /// </summary>
    public static LvFont Montserrat(int size)
    {
        LvglRuntime.Initialize();
        var handle = LvglNative.lvn_font_montserrat((uint)size);
        if (handle == 0) throw new LvglException("The native library has no built-in font.");
        return new LvFont(handle, size, owned: false, data: null);
    }

    /// <summary>Loads a TrueType font from a file. LVGL reads the file lazily as glyphs are needed.</summary>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    public static LvFont FromFile(string path, int size)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);

        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath)) throw new FileNotFoundException("Font file not found.", fullPath);

        LvglRuntime.Initialize();
        LvglRuntime.EnsureUiThread();

        var handle = LvglNative.lv_tiny_ttf_create_file(LvFileSystem.ToLvglPath(fullPath), size);
        if (handle == 0) throw new LvglException($"LVGL could not load '{fullPath}' as a TrueType font.");

        return new LvFont(handle, size, owned: true, data: null);
    }

    /// <summary>
    /// Loads a TrueType font from memory - an embedded resource, for example. The bytes are copied
    /// into unmanaged memory that lives as long as the font, because TinyTTF reads glyphs from
    /// them on demand.
    /// </summary>
    public static LvFont FromData(ReadOnlySpan<byte> ttf, int size)
    {
        if (ttf.IsEmpty) throw new ArgumentException("The font data is empty.", nameof(ttf));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);

        LvglRuntime.Initialize();
        LvglRuntime.EnsureUiThread();

        var data = NativeMemory.Alloc((nuint)ttf.Length);
        ttf.CopyTo(new Span<byte>(data, ttf.Length));

        var handle = LvglNative.lv_tiny_ttf_create_data(data, (nuint)ttf.Length, size);
        if (handle == 0)
        {
            NativeMemory.Free(data);
            throw new LvglException("LVGL could not parse the data as a TrueType font.");
        }

        return new LvFont(handle, size, owned: true, data);
    }

    /// <summary>Frees a TrueType font. Only call this once every widget using it is deleted.</summary>
    public void Dispose()
    {
        if (_handle == 0) return;

        if (IsTrueType) LvglNative.lv_tiny_ttf_destroy(_handle);
        _handle = 0;

        if (_data is not null)
        {
            NativeMemory.Free(_data);
            _data = null;
        }
    }
}
