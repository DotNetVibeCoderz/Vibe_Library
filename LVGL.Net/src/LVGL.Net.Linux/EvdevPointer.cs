using Lvgl.Backends;

namespace Lvgl.Linux;

/// <summary>
/// Reads a touchscreen or mouse through the Linux evdev interface (<c>/dev/input/eventN</c>).
/// </summary>
/// <remarks>
/// <para>
/// Handles both kinds of device: touch panels report absolute positions (<c>EV_ABS</c>) in their
/// own coordinate space, which is rescaled to the screen using the range the driver advertises;
/// mice report relative motion (<c>EV_REL</c>), which is accumulated and clamped.
/// </para>
/// <para>
/// <b>Permissions.</b> Input devices belong to the <c>input</c> group:
/// <c>sudo usermod -aG input $USER</c>.
/// </para>
/// </remarks>
public sealed unsafe class EvdevPointer : IPointerSource
{
    // <linux/input-event-codes.h>
    private const ushort EV_SYN = 0x00;
    private const ushort EV_KEY = 0x01;
    private const ushort EV_REL = 0x02;
    private const ushort EV_ABS = 0x03;

    private const ushort ABS_X = 0x00;
    private const ushort ABS_Y = 0x01;
    private const ushort ABS_MT_POSITION_X = 0x35;
    private const ushort ABS_MT_POSITION_Y = 0x36;

    private const ushort REL_X = 0x00;
    private const ushort REL_Y = 0x01;

    private const ushort BTN_LEFT = 0x110;
    private const ushort BTN_TOUCH = 0x14A;

    private readonly int _screenWidth;
    private readonly int _screenHeight;
    private readonly byte[] _readBuffer = new byte[EventSize * 64];

    private int _fd = -1;
    private int _x;
    private int _y;
    private bool _pressed;
    private AbsRange _rangeX = AbsRange.Unset;
    private AbsRange _rangeY = AbsRange.Unset;
    private bool _disposed;

    /// <summary>
    /// Size of one <c>struct input_event</c>. It begins with a <c>timeval</c> of two
    /// pointer-sized fields, so it is 24 bytes on 64-bit userland and 16 on 32-bit.
    /// </summary>
    private static int EventSize => nint.Size == 8 ? 24 : 16;

    private static int TypeOffset => nint.Size == 8 ? 16 : 8;

    private readonly record struct AbsRange(int Minimum, int Maximum)
    {
        public static AbsRange Unset => new(0, 0);

        public bool IsUsable => Maximum > Minimum;

        public int ScaleTo(int value, int extent)
        {
            if (!IsUsable) return value;
            var normalized = (long)(value - Minimum) * (extent - 1) / (Maximum - Minimum);
            return (int)Math.Clamp(normalized, 0, extent - 1);
        }
    }

    /// <summary>Opens an input device and prepares to translate its events for a given screen size.</summary>
    /// <param name="devicePath">Device node, for example <c>/dev/input/event0</c>.</param>
    /// <param name="screenWidth">Screen width, used to rescale absolute coordinates.</param>
    /// <param name="screenHeight">Screen height, used to rescale absolute coordinates.</param>
    public EvdevPointer(string devicePath, int screenWidth, int screenHeight)
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException("evdev input requires Linux.");
        }

        _screenWidth = screenWidth;
        _screenHeight = screenHeight;

        // Non-blocking: Poll() is called from the render loop and must never stall it.
        _fd = LibC.Open(devicePath, LibC.O_RDONLY | LibC.O_NONBLOCK);
        if (_fd < 0)
        {
            throw new LvglException(
                $"Could not open {devicePath}: {LibC.LastError()}. " +
                "Add your user to the 'input' group (sudo usermod -aG input $USER) and log in again.");
        }

        _rangeX = QueryAbsRange(ABS_X);
        _rangeY = QueryAbsRange(ABS_Y);

        // Multi-touch panels often advertise only the MT axes.
        if (!_rangeX.IsUsable) _rangeX = QueryAbsRange(ABS_MT_POSITION_X);
        if (!_rangeY.IsUsable) _rangeY = QueryAbsRange(ABS_MT_POSITION_Y);
    }

    /// <summary>
    /// Picks the first device under <c>/dev/input</c> that looks like a touchscreen or mouse.
    /// </summary>
    /// <returns>An open device, or <see langword="null"/> when none could be opened.</returns>
    public static EvdevPointer? AutoDetect(int screenWidth, int screenHeight)
    {
        if (!OperatingSystem.IsLinux()) return null;

        // by-path names are stable and descriptive; fall back to raw event nodes.
        var candidates = new List<string>();

        foreach (var directory in (string[])["/dev/input/by-id", "/dev/input/by-path"])
        {
            if (!Directory.Exists(directory)) continue;

            foreach (var entry in Directory.EnumerateFiles(directory))
            {
                if (entry.Contains("mouse", StringComparison.OrdinalIgnoreCase) ||
                    entry.Contains("touch", StringComparison.OrdinalIgnoreCase) ||
                    entry.EndsWith("-event-mouse", StringComparison.Ordinal))
                {
                    candidates.Add(entry);
                }
            }
        }

        if (Directory.Exists("/dev/input"))
        {
            candidates.AddRange(Directory.EnumerateFiles("/dev/input", "event*").Order(StringComparer.Ordinal));
        }

        foreach (var candidate in candidates)
        {
            try
            {
                return new EvdevPointer(candidate, screenWidth, screenHeight);
            }
            catch (LvglException)
            {
                // Not readable or not an input device - try the next one.
            }
        }

        return null;
    }

    public LvPointerState Pointer => new(_x, _y, _pressed);

    private AbsRange QueryAbsRange(ushort axis)
    {
        // EVIOCGABS(axis) = _IOR('E', 0x40 + axis, struct input_absinfo), which is six int32s.
        const nuint direction = 2u << 30;
        const nuint size = 24u << 16;
        const nuint type = (nuint)'E' << 8;
        var request = direction | size | type | (nuint)(0x40 + axis);

        var info = stackalloc int[6];
        if (LibC.Ioctl(_fd, request, info) != 0) return AbsRange.Unset;

        return new AbsRange(info[1], info[2]);
    }

    public void Poll()
    {
        if (_fd < 0) return;

        fixed (byte* buffer = _readBuffer)
        {
            while (true)
            {
                var read = LibC.Read(_fd, buffer, (nuint)_readBuffer.Length);
                if (read <= 0) return;   // EAGAIN on an empty queue

                var count = (int)read / EventSize;
                for (var i = 0; i < count; i++)
                {
                    Handle(buffer + i * EventSize);
                }

                // A short read means the queue is drained.
                if (read < _readBuffer.Length) return;
            }
        }
    }

    private void Handle(byte* e)
    {
        var type = *(ushort*)(e + TypeOffset);
        var code = *(ushort*)(e + TypeOffset + 2);
        var value = *(int*)(e + TypeOffset + 4);

        switch (type)
        {
            case EV_ABS:
                switch (code)
                {
                    case ABS_X:
                    case ABS_MT_POSITION_X:
                        _x = _rangeX.ScaleTo(value, _screenWidth);
                        break;
                    case ABS_Y:
                    case ABS_MT_POSITION_Y:
                        _y = _rangeY.ScaleTo(value, _screenHeight);
                        break;
                }
                break;

            case EV_REL:
                switch (code)
                {
                    case REL_X:
                        _x = Math.Clamp(_x + value, 0, _screenWidth - 1);
                        break;
                    case REL_Y:
                        _y = Math.Clamp(_y + value, 0, _screenHeight - 1);
                        break;
                }
                break;

            case EV_KEY when code is BTN_TOUCH or BTN_LEFT:
                _pressed = value != 0;
                break;

            case EV_SYN:
                break;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_fd >= 0)
        {
            LibC.Close(_fd);
            _fd = -1;
        }
    }
}
