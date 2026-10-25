using System.Runtime.InteropServices;

namespace Lvgl.Linux;

/// <summary>Syscalls needed to talk to the framebuffer, DRM/KMS and evdev input devices.</summary>
internal static unsafe partial class LibC
{
    private const string Library = "libc";

    internal const int O_RDONLY = 0x0000;
    internal const int O_RDWR = 0x0002;
    internal const int O_NONBLOCK = 0x0800;
    internal const int O_CLOEXEC = 0x80000;

    internal const short POLLIN = 0x001;

    internal const int PROT_READ = 0x1;
    internal const int PROT_WRITE = 0x2;
    internal const int MAP_SHARED = 0x01;

    internal static readonly nint MAP_FAILED = -1;

    // <linux/fb.h>
    internal const uint FBIOGET_VSCREENINFO = 0x4600;
    internal const uint FBIOGET_FSCREENINFO = 0x4602;

    [LibraryImport(Library, EntryPoint = "open", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    internal static partial int Open(string path, int flags);

    [LibraryImport(Library, EntryPoint = "close", SetLastError = true)]
    internal static partial int Close(int fd);

    [LibraryImport(Library, EntryPoint = "read", SetLastError = true)]
    internal static partial nint Read(int fd, void* buffer, nuint count);

    [LibraryImport(Library, EntryPoint = "ioctl", SetLastError = true)]
    internal static partial int Ioctl(int fd, nuint request, void* argument);

    [LibraryImport(Library, EntryPoint = "mmap", SetLastError = true)]
    internal static partial nint Mmap(nint address, nuint length, int protection, int flags, int fd, nint offset);

    [LibraryImport(Library, EntryPoint = "munmap", SetLastError = true)]
    internal static partial int Munmap(nint address, nuint length);

    /// <summary><c>struct pollfd</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct PollFd
    {
        public int Fd;
        public short Events;
        public short Revents;
    }

    [LibraryImport(Library, EntryPoint = "poll", SetLastError = true)]
    internal static partial int Poll(PollFd* fds, nuint count, int timeoutMs);

    internal static string LastError() => Marshal.GetLastPInvokeErrorMessage();
}
