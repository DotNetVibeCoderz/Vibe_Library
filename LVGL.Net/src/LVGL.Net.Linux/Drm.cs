using System.Runtime.InteropServices;

namespace Lvgl.Linux;

/// <summary>
/// The slice of the kernel's DRM/KMS interface (<c>&lt;drm/drm.h&gt;</c>, <c>&lt;drm/drm_mode.h&gt;</c>)
/// that <see cref="DrmBackend"/> uses, declared directly so no libdrm is needed.
/// </summary>
/// <remarks>
/// Unlike the fbdev structures, every DRM ioctl argument is built from fixed-width fields with
/// pointers passed as <c>__u64</c>, so one layout is correct on 32-bit ARM, arm64 and x64 alike.
/// The sizes are pinned by unit tests against the kernel's values.
/// </remarks>
internal static class Drm
{
    private const uint IocWrite = 1;
    private const uint IocRead = 2;
    private const uint Type = 'd';

    /// <summary>Linux's <c>_IOC(dir, type, nr, size)</c>.</summary>
    internal static nuint Ioc(uint direction, uint number, int size) =>
        (direction << 30) | ((uint)size << 16) | (Type << 8) | number;

    private static nuint ReadWrite<T>(uint number) where T : unmanaged => Ioc(IocRead | IocWrite, number, Marshal.SizeOf<T>());

    internal static readonly nuint SetMaster = Ioc(0, 0x1E, 0);
    internal static readonly nuint DropMaster = Ioc(0, 0x1F, 0);
    internal static readonly nuint ModeGetResources = ReadWrite<CardResources>(0xA0);
    internal static readonly nuint ModeGetCrtc = ReadWrite<Crtc>(0xA1);
    internal static readonly nuint ModeSetCrtc = ReadWrite<Crtc>(0xA2);
    internal static readonly nuint ModeGetEncoder = ReadWrite<Encoder>(0xA6);
    internal static readonly nuint ModeGetConnector = ReadWrite<Connector>(0xA7);
    internal static readonly nuint ModeAddFb = ReadWrite<FbCommand>(0xAE);
    internal static readonly nuint ModeRemoveFb = Ioc(IocRead | IocWrite, 0xAF, sizeof(uint));
    internal static readonly nuint ModePageFlip = ReadWrite<PageFlip>(0xB0);
    internal static readonly nuint ModeCreateDumb = ReadWrite<CreateDumb>(0xB2);
    internal static readonly nuint ModeMapDumb = ReadWrite<MapDumb>(0xB3);
    internal static readonly nuint ModeDestroyDumb = Ioc(IocRead | IocWrite, 0xB4, sizeof(uint));

    internal const uint ConnectionConnected = 1;
    internal const uint ModeTypePreferred = 1 << 3;
    internal const uint PageFlipEvent = 0x01;
    internal const uint EventFlipComplete = 0x02;

    /// <summary><c>struct drm_mode_modeinfo</c>, 68 bytes.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct ModeInfo
    {
        public uint Clock;
        public ushort HDisplay, HSyncStart, HSyncEnd, HTotal, HSkew;
        public ushort VDisplay, VSyncStart, VSyncEnd, VTotal, VScan;
        public uint VRefresh;
        public uint Flags;
        public uint Type;
        public fixed byte Name[32];

        public readonly override string ToString() => $"{HDisplay}x{VDisplay}@{VRefresh}";
    }

    /// <summary><c>struct drm_mode_card_res</c>, 64 bytes.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct CardResources
    {
        public ulong FbIdPtr, CrtcIdPtr, ConnectorIdPtr, EncoderIdPtr;
        public uint CountFbs, CountCrtcs, CountConnectors, CountEncoders;
        public uint MinWidth, MaxWidth, MinHeight, MaxHeight;
    }

    /// <summary><c>struct drm_mode_get_connector</c>, 80 bytes.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct Connector
    {
        public ulong EncodersPtr, ModesPtr, PropsPtr, PropValuesPtr;
        public uint CountModes, CountProps, CountEncoders;
        public uint EncoderId, ConnectorId, ConnectorType, ConnectorTypeId;
        public uint Connection;
        public uint MmWidth, MmHeight;
        public uint Subpixel;
        public uint Pad;
    }

    /// <summary><c>struct drm_mode_get_encoder</c>, 20 bytes.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct Encoder
    {
        public uint EncoderId, EncoderType, CrtcId, PossibleCrtcs, PossibleClones;
    }

    /// <summary><c>struct drm_mode_crtc</c>, 104 bytes.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct Crtc
    {
        public ulong SetConnectorsPtr;
        public uint CountConnectors;
        public uint CrtcId;
        public uint FbId;
        public uint X, Y;
        public uint GammaSize;
        public uint ModeValid;
        public ModeInfo Mode;
    }

    /// <summary><c>struct drm_mode_fb_cmd</c>, 28 bytes.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct FbCommand
    {
        public uint FbId, Width, Height, Pitch, Bpp, Depth, Handle;
    }

    /// <summary><c>struct drm_mode_crtc_page_flip</c>, 24 bytes.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct PageFlip
    {
        public uint CrtcId, FbId, Flags, Reserved;
        public ulong UserData;
    }

    /// <summary><c>struct drm_mode_create_dumb</c>, 32 bytes.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct CreateDumb
    {
        public uint Height, Width, Bpp, Flags;
        public uint Handle, Pitch;
        public ulong Size;
    }

    /// <summary><c>struct drm_mode_map_dumb</c>, 16 bytes.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct MapDumb
    {
        public uint Handle, Pad;
        public ulong Offset;
    }

    /// <summary><c>struct drm_event</c> header, 8 bytes.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct Event
    {
        public uint Type, Length;
    }
}
