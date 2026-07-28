using System.Reflection;
using System.Runtime.InteropServices;
using Lvgl.Interop;

namespace Lvgl.Sdl;

/// <summary>
/// The slice of SDL2 the backend needs.
/// </summary>
/// <remarks>
/// <para>
/// Bound by hand rather than through a binding package: the backend uses roughly a dozen
/// functions, and a dependency-free assembly is easier to deploy onto a device than one that drags
/// in a marshalling layer.
/// </para>
/// <para>
/// <c>SDL_Event</c> is a large union whose exact layout varies with the SDL build, so events are
/// read out of a raw byte buffer at documented offsets instead of being described as a struct.
/// </para>
/// </remarks>
internal static unsafe partial class Sdl2
{
    private const string Library = "SDL2";

    static Sdl2() => NativeLibrary.SetDllImportResolver(typeof(Sdl2).Assembly, Resolve);

    // Distribution names differ per platform, and Linux ships only the versioned soname.
    private static readonly string[] CandidateNames =
    [
        "SDL2.dll",
        "libSDL2-2.0.so.0",
        "libSDL2.so.0",
        "libSDL2.so",
        "libSDL2-2.0.0.dylib",
        "libSDL2.dylib",
        "SDL2",
    ];

    private static nint Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName != Library) return 0;

        // Same probe order as the core library, so an SDL2 binary dropped into
        // runtimes/<rid>/native is picked up exactly like lvglnet - which matters on Windows,
        // where SDL2.dll is not installed system-wide.
        foreach (var candidate in CandidateNames)
        {
            if (LvglNativeLibrary.TryLoadFromProbePaths(candidate, out var handle)) return handle;
        }

        return 0;
    }

    internal const uint InitVideo = 0x00000020;
    internal const uint InitEvents = 0x00004000;

    internal const int WindowPosCentered = 0x2FFF0000;
    internal const uint WindowShown = 0x00000004;
    internal const uint WindowAllowHighDpi = 0x00002000;

    internal const uint RendererAccelerated = 0x00000002;
    internal const uint RendererSoftware = 0x00000001;
    internal const uint RendererPresentVSync = 0x00000004;

    /// <summary>SDL_PIXELFORMAT_ARGB8888 - byte order B,G,R,A on little-endian, matching LVGL's 32-bit buffer.</summary>
    internal const uint PixelFormatArgb8888 = 0x16362004;

    internal const int TextureAccessStreaming = 1;

    internal const uint EventQuit = 0x100;
    internal const uint EventWindow = 0x200;
    internal const uint EventKeyDown = 0x300;
    internal const uint EventKeyUp = 0x301;
    internal const uint EventMouseMotion = 0x400;
    internal const uint EventMouseButtonDown = 0x401;
    internal const uint EventMouseButtonUp = 0x402;

    internal const byte WindowEventClose = 14;
    internal const byte ButtonLeft = 1;

    // Offsets into the SDL_Event union. All mouse events share the same header layout, so x and y
    // sit at the same place for motion and button events.
    internal const int OffsetType = 0;
    internal const int OffsetMouseX = 20;
    internal const int OffsetMouseY = 24;
    internal const int OffsetMouseButton = 16;
    internal const int OffsetWindowEventId = 12;
    internal const int OffsetKeySym = 20;

    /// <summary>SDL_Event is 56 bytes in SDL2; 128 leaves room for any build's padding.</summary>
    internal const int EventBufferSize = 128;

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        public int X;
        public int Y;
        public int W;
        public int H;
    }

    [LibraryImport(Library, EntryPoint = "SDL_Init")]
    internal static partial int Init(uint flags);

    [LibraryImport(Library, EntryPoint = "SDL_Quit")]
    internal static partial void Quit();

    [LibraryImport(Library, EntryPoint = "SDL_GetError")]
    internal static partial nint GetErrorRaw();

    internal static string GetError() => Marshal.PtrToStringUTF8(GetErrorRaw()) ?? "unknown SDL error";

    [LibraryImport(Library, EntryPoint = "SDL_CreateWindow", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint CreateWindow(string title, int x, int y, int w, int h, uint flags);

    [LibraryImport(Library, EntryPoint = "SDL_DestroyWindow")]
    internal static partial void DestroyWindow(nint window);

    [LibraryImport(Library, EntryPoint = "SDL_CreateRenderer")]
    internal static partial nint CreateRenderer(nint window, int index, uint flags);

    [LibraryImport(Library, EntryPoint = "SDL_DestroyRenderer")]
    internal static partial void DestroyRenderer(nint renderer);

    [LibraryImport(Library, EntryPoint = "SDL_CreateTexture")]
    internal static partial nint CreateTexture(nint renderer, uint format, int access, int w, int h);

    [LibraryImport(Library, EntryPoint = "SDL_DestroyTexture")]
    internal static partial void DestroyTexture(nint texture);

    [LibraryImport(Library, EntryPoint = "SDL_UpdateTexture")]
    internal static partial int UpdateTexture(nint texture, Rect* rect, void* pixels, int pitch);

    [LibraryImport(Library, EntryPoint = "SDL_RenderClear")]
    internal static partial int RenderClear(nint renderer);

    [LibraryImport(Library, EntryPoint = "SDL_RenderCopy")]
    internal static partial int RenderCopy(nint renderer, nint texture, Rect* source, Rect* destination);

    [LibraryImport(Library, EntryPoint = "SDL_RenderPresent")]
    internal static partial void RenderPresent(nint renderer);

    [LibraryImport(Library, EntryPoint = "SDL_PollEvent")]
    internal static partial int PollEvent(byte* e);

    [LibraryImport(Library, EntryPoint = "SDL_SetWindowTitle", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void SetWindowTitle(nint window, string title);
}
