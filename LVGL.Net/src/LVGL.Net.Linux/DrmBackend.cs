using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Lvgl.Backends;
using Lvgl.Drawing;

namespace Lvgl.Linux;

/// <summary>
/// Draws LVGL through the kernel's DRM/KMS interface (<c>/dev/dri/card*</c>) with double
/// buffering and page flips on vertical blank - no tearing, and no window system.
/// </summary>
/// <remarks>
/// <para>
/// Current Raspberry Pi OS drives displays through the KMS driver (<c>vc4-kms-v3d</c>); the fbdev
/// device that <see cref="FramebufferBackend"/> uses is an emulation layer on top of it, which can
/// only copy into the visible image and therefore tears on movement. This backend talks to KMS
/// directly: it allocates two scan-out buffers, renders into the hidden one and flips them at
/// vertical blank.
/// </para>
/// <para>
/// <b>How a frame is presented.</b> LVGL renders partial strips into a shadow copy of the screen.
/// At the end of the frame the damaged region is copied into the hidden buffer and a page flip is
/// queued. The hidden buffer last showed the frame <i>before</i> the previous one, so the copy
/// covers this frame's damage and the previous frame's together - small regions stay small, and
/// neither buffer is ever stale. The wait for the flip happens at the start of the next present,
/// so LVGL renders the next frame while the display catches up.
/// </para>
/// <para>
/// <b>Permissions and ownership.</b> The device is owned by the <c>video</c> group, and only one
/// process may drive it: run this with no desktop on the same display (a console boot, or the
/// desktop stopped). If page flipping is refused by the driver, the backend falls back to drawing
/// straight into the visible buffer and reports it through <see cref="IsDoubleBuffered"/>.
/// </para>
/// </remarks>
public sealed unsafe class DrmBackend : ILvglBackend
{
    private readonly IPointerSource? _pointerSource;
    private readonly Buffer[] _buffers = new Buffer[2];
    private int _fd = -1;
    private uint _crtcId;
    private uint _connectorId;
    private Drm.ModeInfo _mode;
    private Drm.Crtc _savedCrtc;
    private bool _hasSavedCrtc;
    private byte* _shadow;
    private int _front;
    private bool _flipPending;
    private LvArea _damage = Empty;
    private LvArea _previousDamage = Empty;
    private bool _disposed;

    private static readonly LvArea Empty = new(0, 0, -1, -1);

    private struct Buffer
    {
        public uint Handle;
        public uint FbId;
        public uint Pitch;
        public ulong Size;
        public byte* Pixels;
    }

    /// <summary>Opens a DRM device and takes over its first connected display.</summary>
    /// <param name="devicePath">
    /// The card, usually <c>/dev/dri/card0</c> or, on a Pi 4/5 running KMS, <c>/dev/dri/card1</c>.
    /// Null picks the first card that has a connected display.
    /// </param>
    /// <param name="pointerSource">Optional touch or mouse source, normally an <see cref="EvdevPointer"/>.</param>
    /// <param name="width">Preferred mode width, or 0 for the display's preferred mode.</param>
    /// <param name="height">Preferred mode height, or 0 for the display's preferred mode.</param>
    public DrmBackend(string? devicePath = null, IPointerSource? pointerSource = null, int width = 0, int height = 0)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("The DRM backend requires Linux.");

        _pointerSource = pointerSource;

        try
        {
            if (devicePath is not null) OpenCard(devicePath, width, height);
            else OpenFirstConnectedCard(width, height);

            CreateBuffers();
            ModeSet();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public int Width { get; private set; }

    public int Height { get; private set; }

    /// <summary>The device that was opened.</summary>
    public string DevicePath { get; private set; } = string.Empty;

    /// <summary>The display mode in use, e.g. <c>800x480@60</c>.</summary>
    public string Mode => _mode.ToString();

    /// <summary>
    /// True when frames are presented by page flipping. False when the driver refused flips and
    /// the backend draws into the visible buffer instead (which can tear, like fbdev).
    /// </summary>
    public bool IsDoubleBuffered { get; private set; } = true;

    /// <summary>Frames presented so far.</summary>
    public long FramesPresented { get; private set; }

    public LvPointerState Pointer => _pointerSource?.Pointer ?? default;

    #region Setup

    private void OpenFirstConnectedCard(int width, int height)
    {
        var errors = new List<string>();

        foreach (var path in Directory.Exists("/dev/dri")
                     ? Directory.GetFiles("/dev/dri", "card*").Order(StringComparer.Ordinal)
                     : Enumerable.Empty<string>())
        {
            try
            {
                OpenCard(path, width, height);
                return;
            }
            catch (LvglException ex)
            {
                errors.Add($"{path}: {ex.Message}");
                if (_fd >= 0)
                {
                    LibC.Close(_fd);
                    _fd = -1;
                }
            }
        }

        throw new LvglException(errors.Count == 0
            ? "No DRM device found under /dev/dri. Enable KMS (dtoverlay=vc4-kms-v3d on a Pi) or use FramebufferBackend."
            : "No DRM device has a connected display:" + Environment.NewLine + string.Join(Environment.NewLine, errors));
    }

    private void OpenCard(string path, int width, int height)
    {
        _fd = LibC.Open(path, LibC.O_RDWR | LibC.O_CLOEXEC);
        if (_fd < 0)
        {
            throw new LvglException(
                $"Could not open {path}: {LibC.LastError()}. Add your user to the 'video' group (sudo usermod -aG video $USER) and log in again.");
        }

        DevicePath = path;

        // Becoming master is what allows mode setting; the first client usually is already, so a
        // refusal is only fatal if the later mode set fails too.
        LibC.Ioctl(_fd, Drm.SetMaster, null);

        var resources = new Drm.CardResources();
        Check(LibC.Ioctl(_fd, Drm.ModeGetResources, &resources), "DRM_IOCTL_MODE_GETRESOURCES");

        var crtcs = new uint[resources.CountCrtcs];
        var connectors = new uint[resources.CountConnectors];
        var encoders = new uint[resources.CountEncoders];
        fixed (uint* crtcPtr = crtcs)
        fixed (uint* connectorPtr = connectors)
        fixed (uint* encoderPtr = encoders)
        {
            resources = new Drm.CardResources
            {
                CrtcIdPtr = (ulong)crtcPtr,
                ConnectorIdPtr = (ulong)connectorPtr,
                EncoderIdPtr = (ulong)encoderPtr,
                CountCrtcs = (uint)crtcs.Length,
                CountConnectors = (uint)connectors.Length,
                CountEncoders = (uint)encoders.Length,
            };
            Check(LibC.Ioctl(_fd, Drm.ModeGetResources, &resources), "DRM_IOCTL_MODE_GETRESOURCES");
        }

        foreach (var connectorId in connectors)
        {
            if (TryConnector(connectorId, crtcs, width, height)) return;
        }

        throw new LvglException("no connected display");
    }

    private bool TryConnector(uint connectorId, uint[] crtcs, int width, int height)
    {
        var connector = new Drm.Connector { ConnectorId = connectorId };
        Check(LibC.Ioctl(_fd, Drm.ModeGetConnector, &connector), "DRM_IOCTL_MODE_GETCONNECTOR");

        if (connector.Connection != Drm.ConnectionConnected || connector.CountModes == 0) return false;

        var modes = new Drm.ModeInfo[connector.CountModes];
        var connectorEncoders = new uint[connector.CountEncoders];
        fixed (Drm.ModeInfo* modePtr = modes)
        fixed (uint* encoderPtr = connectorEncoders)
        {
            connector = new Drm.Connector
            {
                ConnectorId = connectorId,
                ModesPtr = (ulong)modePtr,
                CountModes = (uint)modes.Length,
                EncodersPtr = (ulong)encoderPtr,
                CountEncoders = (uint)connectorEncoders.Length,
            };
            Check(LibC.Ioctl(_fd, Drm.ModeGetConnector, &connector), "DRM_IOCTL_MODE_GETCONNECTOR");
        }

        _mode = ChooseMode(modes, width, height);
        _connectorId = connectorId;
        _crtcId = ChooseCrtc(connector.EncoderId, connectorEncoders, crtcs);
        if (_crtcId == 0) return false;

        Width = _mode.HDisplay;
        Height = _mode.VDisplay;
        return true;
    }

    /// <summary>The requested size if the display offers it, else its preferred mode, else its first.</summary>
    internal static Drm.ModeInfo ChooseMode(IReadOnlyList<Drm.ModeInfo> modes, int width, int height)
    {
        if (width > 0 && height > 0)
        {
            foreach (var mode in modes)
            {
                if (mode.HDisplay == width && mode.VDisplay == height) return mode;
            }
        }

        foreach (var mode in modes)
        {
            if ((mode.Type & Drm.ModeTypePreferred) != 0) return mode;
        }

        return modes[0];
    }

    private uint ChooseCrtc(uint currentEncoderId, uint[] encoderIds, uint[] crtcs)
    {
        // The CRTC already driving the connector is the natural choice - no re-routing needed.
        if (currentEncoderId != 0)
        {
            var current = new Drm.Encoder { EncoderId = currentEncoderId };
            if (LibC.Ioctl(_fd, Drm.ModeGetEncoder, &current) == 0 && current.CrtcId != 0) return current.CrtcId;
        }

        foreach (var encoderId in encoderIds)
        {
            var encoder = new Drm.Encoder { EncoderId = encoderId };
            if (LibC.Ioctl(_fd, Drm.ModeGetEncoder, &encoder) != 0) continue;

            for (var i = 0; i < crtcs.Length; i++)
            {
                if ((encoder.PossibleCrtcs & (1u << i)) != 0) return crtcs[i];
            }
        }

        return 0;
    }

    private void CreateBuffers()
    {
        for (var i = 0; i < _buffers.Length; i++)
        {
            var create = new Drm.CreateDumb { Width = (uint)Width, Height = (uint)Height, Bpp = 32 };
            Check(LibC.Ioctl(_fd, Drm.ModeCreateDumb, &create), "DRM_IOCTL_MODE_CREATE_DUMB");

            // Depth 24 in 32 bits is XRGB8888 - the exact byte order LVGL renders, so presenting
            // is a straight copy with no per-pixel work.
            var fb = new Drm.FbCommand { Width = (uint)Width, Height = (uint)Height, Pitch = create.Pitch, Bpp = 32, Depth = 24, Handle = create.Handle };
            Check(LibC.Ioctl(_fd, Drm.ModeAddFb, &fb), "DRM_IOCTL_MODE_ADDFB");

            var map = new Drm.MapDumb { Handle = create.Handle };
            Check(LibC.Ioctl(_fd, Drm.ModeMapDumb, &map), "DRM_IOCTL_MODE_MAP_DUMB");

            var address = LibC.Mmap(0, (nuint)create.Size, LibC.PROT_READ | LibC.PROT_WRITE, LibC.MAP_SHARED, _fd, (nint)map.Offset);
            if (address == LibC.MAP_FAILED || address == 0) throw new LvglException($"Could not map a DRM buffer: {LibC.LastError()}");

            _buffers[i] = new Buffer
            {
                Handle = create.Handle,
                FbId = fb.FbId,
                Pitch = create.Pitch,
                Size = create.Size,
                Pixels = (byte*)address,
            };

            new Span<byte>((void*)address, (int)create.Size).Clear();
        }

        var shadowBytes = (nuint)Width * (nuint)Height * 4;
        _shadow = (byte*)NativeMemory.AllocZeroed(shadowBytes);
    }

    private void ModeSet()
    {
        // Remember what the console had, so it comes back when the application exits.
        var saved = new Drm.Crtc { CrtcId = _crtcId };
        if (LibC.Ioctl(_fd, Drm.ModeGetCrtc, &saved) == 0)
        {
            _savedCrtc = saved;
            _hasSavedCrtc = true;
        }

        var connector = _connectorId;
        var crtc = new Drm.Crtc
        {
            CrtcId = _crtcId,
            FbId = _buffers[0].FbId,
            SetConnectorsPtr = (ulong)&connector,
            CountConnectors = 1,
            ModeValid = 1,
            Mode = _mode,
        };

        Check(LibC.Ioctl(_fd, Drm.ModeSetCrtc, &crtc),
            "DRM_IOCTL_MODE_SETCRTC (is a desktop or another program driving this display?)");
        _front = 0;
    }

    private static void Check(int result, string what)
    {
        if (result != 0) throw new LvglException($"{what} failed: {LibC.LastError()}");
    }

    #endregion

    #region Rendering

    public void Blit(in LvArea area, nint pixels, bool isLastFlush)
    {
        if (_shadow is null) return;

        var clipped = area.Clip(new LvArea(0, 0, Width - 1, Height - 1));
        if (!clipped.IsEmpty)
        {
            var source = (byte*)pixels;
            var sourceStride = area.Width * 4;
            var leading = (clipped.X1 - area.X1) * 4;
            var shadowStride = Width * 4;

            for (var y = clipped.Y1; y <= clipped.Y2; y++)
            {
                Unsafe.CopyBlock(
                    _shadow + y * (long)shadowStride + clipped.X1 * 4,
                    source + (y - area.Y1) * sourceStride + leading,
                    (uint)(clipped.Width * 4));
            }

            _damage = Union(_damage, clipped);
        }

        if (isLastFlush) Present();
    }

    private void Present()
    {
        if (_damage.IsEmpty) return;

        if (!IsDoubleBuffered)
        {
            CopyRegion(_buffers[_front], _damage);
            _damage = Empty;
            FramesPresented++;
            return;
        }

        // The hidden buffer may still be on screen until the last flip completes.
        WaitForFlip();

        var back = 1 - _front;
        CopyRegion(_buffers[back], Union(_damage, _previousDamage));

        var flip = new Drm.PageFlip { CrtcId = _crtcId, FbId = _buffers[back].FbId, Flags = Drm.PageFlipEvent };
        if (LibC.Ioctl(_fd, Drm.ModePageFlip, &flip) != 0)
        {
            // Some drivers refuse flips (or the mode is not flippable): keep going single-buffered
            // rather than stop rendering. Bring the visible buffer up to date first.
            IsDoubleBuffered = false;
            new Span<byte>(_buffers[_front].Pixels, (int)_buffers[_front].Size).Clear();
            CopyRegion(_buffers[_front], new LvArea(0, 0, Width - 1, Height - 1));
        }
        else
        {
            _flipPending = true;
            _front = back;
        }

        _previousDamage = _damage;
        _damage = Empty;
        FramesPresented++;
    }

    private void CopyRegion(in Buffer target, LvArea region)
    {
        region = region.Clip(new LvArea(0, 0, Width - 1, Height - 1));
        if (region.IsEmpty) return;

        var shadowStride = Width * 4;
        for (var y = region.Y1; y <= region.Y2; y++)
        {
            Unsafe.CopyBlock(
                target.Pixels + y * (long)target.Pitch + region.X1 * 4,
                _shadow + y * (long)shadowStride + region.X1 * 4,
                (uint)(region.Width * 4));
        }
    }

    /// <summary>Blocks until the queued page flip has happened, at most ~one frame at 30 Hz.</summary>
    private void WaitForFlip()
    {
        if (!_flipPending) return;

        var poll = new LibC.PollFd { Fd = _fd, Events = LibC.POLLIN };
        var buffer = stackalloc byte[1024];

        for (var attempt = 0; attempt < 4 && _flipPending; attempt++)
        {
            if (LibC.Poll(&poll, 1, 50) <= 0) continue;

            var read = LibC.Read(_fd, buffer, 1024);
            for (nint offset = 0; offset + sizeof(Drm.Event) <= read;)
            {
                var header = (Drm.Event*)(buffer + offset);
                if (header->Type == Drm.EventFlipComplete) _flipPending = false;
                if (header->Length == 0) break;
                offset += (nint)header->Length;
            }
        }

        // A lost event must not freeze the display: carry on as if the flip happened.
        _flipPending = false;
    }

    /// <summary>Bounding box of two inclusive areas; an empty area contributes nothing.</summary>
    internal static LvArea Union(LvArea a, LvArea b)
    {
        if (a.IsEmpty) return b;
        if (b.IsEmpty) return a;
        return new LvArea(Math.Min(a.X1, b.X1), Math.Min(a.Y1, b.Y1), Math.Max(a.X2, b.X2), Math.Max(a.Y2, b.Y2));
    }

    #endregion

    public bool PumpEvents()
    {
        _pointerSource?.Poll();
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_fd >= 0)
        {
            WaitForFlip();

            if (_hasSavedCrtc && _savedCrtc.ModeValid != 0)
            {
                var connector = _connectorId;
                var restore = _savedCrtc;
                restore.SetConnectorsPtr = (ulong)&connector;
                restore.CountConnectors = 1;
                LibC.Ioctl(_fd, Drm.ModeSetCrtc, &restore);
            }

            foreach (var buffer in _buffers)
            {
                if (buffer.Pixels is not null) LibC.Munmap((nint)buffer.Pixels, (nuint)buffer.Size);

                if (buffer.FbId != 0)
                {
                    var fbId = buffer.FbId;
                    LibC.Ioctl(_fd, Drm.ModeRemoveFb, &fbId);
                }

                if (buffer.Handle != 0)
                {
                    var handle = buffer.Handle;
                    LibC.Ioctl(_fd, Drm.ModeDestroyDumb, &handle);
                }
            }

            LibC.Ioctl(_fd, Drm.DropMaster, null);
            LibC.Close(_fd);
            _fd = -1;
        }

        if (_shadow is not null)
        {
            NativeMemory.Free(_shadow);
            _shadow = null;
        }

        _pointerSource?.Dispose();
    }
}
