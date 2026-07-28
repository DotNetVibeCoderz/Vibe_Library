using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Lvgl.Backends;
using Lvgl.Drawing;
using Lvgl.Interop;

namespace Lvgl.Display;

/// <summary>
/// Binds an <see cref="ILvglBackend"/> to a native <c>lv_display_t</c> and owns its draw buffers.
/// </summary>
public sealed unsafe class LvglDisplay : IDisposable
{
    private readonly ILvglBackend _backend;
    private GCHandle _self;
    private void* _buffer1;
    private void* _buffer2;
    private Exception? _callbackException;
    private bool _disposed;

    internal LvglDisplay(ILvglBackend backend, LvRenderMode renderMode, int bufferLines)
    {
        _backend = backend;
        RenderMode = renderMode;

        Handle = LvglNative.lv_display_create(backend.Width, backend.Height);
        if (Handle == 0) throw new LvglException("lv_display_create failed (out of memory?).");

        // The display's user data carries the GCHandle so the static flush thunk can find this
        // instance without a dictionary lookup on the render path.
        _self = GCHandle.Alloc(this, GCHandleType.Normal);
        LvglNative.lv_display_set_user_data(Handle, (void*)GCHandle.ToIntPtr(_self));

        // Direct and full render modes hand LVGL the whole screen; partial mode uses a strip.
        var lines = renderMode == LvRenderMode.Partial
            ? Math.Clamp(bufferLines, 1, backend.Height)
            : backend.Height;

        BufferLines = lines;
        BufferSizeBytes = (uint)(backend.Width * lines * BytesPerPixel);

        // 64-byte alignment keeps the software renderer's row copies on cache-line boundaries;
        // on the Pi this is worth a few percent on large fills.
        _buffer1 = NativeMemory.AlignedAlloc(BufferSizeBytes, 64);
        _buffer2 = NativeMemory.AlignedAlloc(BufferSizeBytes, 64);
        if (_buffer1 is null || _buffer2 is null) throw new LvglException("Could not allocate LVGL draw buffers.");

        NativeMemory.Clear(_buffer1, BufferSizeBytes);
        NativeMemory.Clear(_buffer2, BufferSizeBytes);

        LvglNative.lv_display_set_buffers(Handle, _buffer1, _buffer2, BufferSizeBytes, (int)renderMode);
        LvglNative.lv_display_set_flush_cb(Handle, &FlushThunk);
    }

    /// <summary>Bytes per pixel of the render buffer. Fixed at 4 by LV_COLOR_DEPTH=32.</summary>
    public const int BytesPerPixel = 4;

    /// <summary>Native <c>lv_display_t*</c>.</summary>
    public nint Handle { get; }

    /// <summary>Render mode this display was created with.</summary>
    public LvRenderMode RenderMode { get; }

    /// <summary>Height in scan lines of each draw buffer.</summary>
    public int BufferLines { get; }

    /// <summary>Size of each of the two draw buffers, in bytes.</summary>
    public uint BufferSizeBytes { get; }

    /// <summary>Total bytes of unmanaged memory held for draw buffers.</summary>
    public long AllocatedBytes => BufferSizeBytes * 2L;

    public int Width => _backend.Width;

    public int Height => _backend.Height;

    /// <summary>
    /// Rethrows an exception that escaped a backend call during flushing. Native code cannot
    /// propagate managed exceptions, so the thunk parks it here and the run loop drains it.
    /// </summary>
    internal void ThrowPendingCallbackException()
    {
        var pending = Interlocked.Exchange(ref _callbackException, null);
        if (pending is not null)
        {
            throw new LvglException("The display backend threw while flushing a frame.", pending);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void FlushThunk(nint display, nint area, byte* pixels)
    {
        // Must never let an exception cross back into C: LVGL would be left mid-refresh with its
        // invalidation state inconsistent, and on most platforms the process would simply abort.
        LvglDisplay? self = null;
        try
        {
            var userData = LvglNative.lv_display_get_user_data(display);
            if (userData is null) return;

            self = GCHandle.FromIntPtr((nint)userData).Target as LvglDisplay;
            self?.OnFlush(display, area, pixels);
        }
        catch (Exception ex)
        {
            if (self is not null) Interlocked.CompareExchange(ref self._callbackException, ex, null);
        }
        finally
        {
            LvglNative.lv_display_flush_ready(display);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void OnFlush(nint display, nint areaPtr, byte* pixels)
    {
        int x1, y1, x2, y2;
        LvglNative.lvn_area_get(areaPtr, &x1, &y1, &x2, &y2);

        var area = new LvArea(x1, y1, x2, y2);
        if (area.IsEmpty) return;

        _backend.Blit(area, (nint)pixels, LvglNative.lv_display_flush_is_last(display));
        FlushCount++;
    }

    /// <summary>Number of regions flushed since creation. Useful when profiling redraw churn.</summary>
    public long FlushCount { get; private set; }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (Handle != 0) LvglNative.lv_display_delete(Handle);

        if (_buffer1 is not null) { NativeMemory.AlignedFree(_buffer1); _buffer1 = null; }
        if (_buffer2 is not null) { NativeMemory.AlignedFree(_buffer2); _buffer2 = null; }

        if (_self.IsAllocated) _self.Free();
    }
}
