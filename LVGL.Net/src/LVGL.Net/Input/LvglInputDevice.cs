using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Lvgl.Backends;
using Lvgl.Interop;

namespace Lvgl.Input;

/// <summary>
/// Feeds pointer and key state from an <see cref="ILvglBackend"/> into a native
/// <c>lv_indev_t</c>.
/// </summary>
/// <remarks>
/// LVGL polls input devices from inside <c>lv_timer_handler</c> rather than accepting pushed
/// events, so this class is a pull adapter: the backend keeps the latest state, and the read
/// callback samples it whenever LVGL asks.
/// </remarks>
public sealed unsafe class LvglInputDevice : IDisposable
{
    // lv_indev_t does not expose a user-data slot in every 9.x point release, so instances are
    // resolved through a table instead. The read callback runs at LVGL's input rate (~30 ms),
    // where a dictionary probe is irrelevant next to the redraw it may trigger.
    private static readonly ConcurrentDictionary<nint, LvglInputDevice> Devices = new();

    private readonly ILvglBackend _backend;
    private readonly ILvglKeyboardSource? _keyboard;
    private bool _disposed;

    internal LvglInputDevice(ILvglBackend backend, LvIndevType type, nint display)
    {
        _backend = backend;
        _keyboard = type == LvIndevType.Keypad ? backend as ILvglKeyboardSource : null;
        Type = type;

        if (type == LvIndevType.Keypad && _keyboard is null)
        {
            throw new LvglException(
                $"{backend.GetType().Name} does not implement {nameof(ILvglKeyboardSource)}, so it cannot drive a keypad device.");
        }

        Handle = LvglNative.lv_indev_create();
        if (Handle == 0) throw new LvglException("lv_indev_create failed.");

        Devices[Handle] = this;

        LvglNative.lv_indev_set_type(Handle, (int)type);
        LvglNative.lv_indev_set_display(Handle, display);
        LvglNative.lv_indev_set_read_cb(Handle, &ReadThunk);
    }

    /// <summary>Native <c>lv_indev_t*</c>.</summary>
    public nint Handle { get; }

    /// <summary>Kind of device this adapter drives.</summary>
    public LvIndevType Type { get; }

    /// <summary>
    /// Binds this device to a focus group. Required for keypad and encoder devices: LVGL delivers
    /// their input to the focused widget of a group, so without one the device is inert.
    /// </summary>
    public void SetGroup(LvGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        LvglNative.lv_indev_set_group(Handle, group.Handle);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void ReadThunk(nint indev, nint data)
    {
        try
        {
            if (Devices.TryGetValue(indev, out var device)) device.OnRead(data);
        }
        catch
        {
            // Swallowing here is deliberate: an input read that throws must not unwind into C.
            // A failed sample simply means LVGL sees the previous state for one cycle.
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void OnRead(nint data)
    {
        if (Type == LvIndevType.Keypad)
        {
            if (_keyboard is not null && _keyboard.TryDequeueKey(out var key, out var pressed))
            {
                LvglNative.lvn_indev_data_set_key(data, key, pressed ? (byte)1 : (byte)0);
            }
            return;
        }

        var pointer = _backend.Pointer;
        LvglNative.lvn_indev_data_set_pointer(data, pointer.X, pointer.Y, pointer.Pressed ? (byte)1 : (byte)0);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Devices.TryRemove(Handle, out _);
        if (Handle != 0) LvglNative.lv_indev_delete(Handle);
    }
}
