using System.Runtime.InteropServices;
using Lvgl.Drawing;
using Lvgl.Linux;

namespace Lvgl.Tests;

/// <summary>
/// The parts of the DRM/KMS backend that can be checked off-device: the kernel ABI it encodes,
/// mode selection and damage tracking. Driving a real display needs a Linux machine with KMS.
/// </summary>
public class DrmBackendTests
{
    // Request numbers as the kernel headers produce them; identical on every architecture
    // because each argument struct has a fixed size.
    [Theory]
    [InlineData("ModeGetResources", 0xC04064A0u)]
    [InlineData("ModeGetCrtc", 0xC06864A1u)]
    [InlineData("ModeSetCrtc", 0xC06864A2u)]
    [InlineData("ModeGetEncoder", 0xC01464A6u)]
    [InlineData("ModeGetConnector", 0xC05064A7u)]
    [InlineData("ModeAddFb", 0xC01C64AEu)]
    [InlineData("ModeRemoveFb", 0xC00464AFu)]
    [InlineData("ModePageFlip", 0xC01864B0u)]
    [InlineData("ModeCreateDumb", 0xC02064B2u)]
    [InlineData("ModeMapDumb", 0xC01064B3u)]
    [InlineData("ModeDestroyDumb", 0xC00464B4u)]
    [InlineData("SetMaster", 0x0000641Eu)]
    [InlineData("DropMaster", 0x0000641Fu)]
    public void Ioctl_requests_match_the_kernel(string name, uint expected)
    {
        var field = typeof(Drm).GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        Assert.Equal((nuint)expected, (nuint)field.GetValue(null)!);
    }

    [Fact]
    public void Structures_have_the_kernel_sizes()
    {
        Assert.Equal(68, Marshal.SizeOf<Drm.ModeInfo>());
        Assert.Equal(64, Marshal.SizeOf<Drm.CardResources>());
        Assert.Equal(80, Marshal.SizeOf<Drm.Connector>());
        Assert.Equal(20, Marshal.SizeOf<Drm.Encoder>());
        Assert.Equal(104, Marshal.SizeOf<Drm.Crtc>());
        Assert.Equal(28, Marshal.SizeOf<Drm.FbCommand>());
        Assert.Equal(24, Marshal.SizeOf<Drm.PageFlip>());
        Assert.Equal(32, Marshal.SizeOf<Drm.CreateDumb>());
        Assert.Equal(16, Marshal.SizeOf<Drm.MapDumb>());
        Assert.Equal(8, Marshal.SizeOf<Drm.Event>());
    }

    [Fact]
    public void The_requested_mode_wins_then_the_preferred_one()
    {
        Drm.ModeInfo Mode(ushort w, ushort h, bool preferred) =>
            new() { HDisplay = w, VDisplay = h, VRefresh = 60, Type = preferred ? Drm.ModeTypePreferred : 0 };

        Drm.ModeInfo[] modes = [Mode(1920, 1080, false), Mode(800, 480, true), Mode(1280, 720, false)];

        Assert.Equal(1280, DrmBackend.ChooseMode(modes, 1280, 720).HDisplay);
        Assert.Equal(800, DrmBackend.ChooseMode(modes, 0, 0).HDisplay);
        Assert.Equal(800, DrmBackend.ChooseMode(modes, 1024, 600).HDisplay);   // not offered: preferred
        Assert.Equal(1920, DrmBackend.ChooseMode([Mode(1920, 1080, false)], 0, 0).HDisplay);
    }

    [Fact]
    public void Damage_from_two_frames_is_combined_into_one_box()
    {
        var empty = new LvArea(0, 0, -1, -1);
        var a = new LvArea(10, 10, 19, 19);
        var b = new LvArea(50, 5, 59, 14);

        Assert.Equal(a.ToString(), DrmBackend.Union(empty, a).ToString());
        Assert.Equal(new LvArea(10, 5, 59, 19).ToString(), DrmBackend.Union(a, b).ToString());
        Assert.True(DrmBackend.Union(empty, empty).IsEmpty);
    }

    [Fact]
    public void Off_Linux_the_backend_says_so_instead_of_failing_obscurely()
    {
        if (OperatingSystem.IsLinux()) return;
        Assert.Throws<PlatformNotSupportedException>(() => new DrmBackend());
    }
}
