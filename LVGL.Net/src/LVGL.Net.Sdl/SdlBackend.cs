using System.Runtime.InteropServices;
using Lvgl.Backends;
using Lvgl.Drawing;

namespace Lvgl.Sdl;

/// <summary>
/// Renders LVGL into an SDL2 window and feeds it mouse and keyboard input. Works anywhere SDL2
/// does: Windows, macOS, and Linux desktops including a Raspberry Pi running X11 or Wayland.
/// </summary>
/// <remarks>
/// LVGL renders into a buffer this class owns; each flushed region is pushed into a streaming
/// texture and the texture is presented once per frame. The texture retains its contents between
/// frames, which is what makes LVGL's partial redraw mode correct here - only the changed strips
/// are uploaded.
/// </remarks>
public sealed unsafe class SdlBackend : ILvglBackend, ILvglKeyboardSource
{
    private readonly Queue<(uint Key, bool Pressed)> _keys = new();
    private readonly byte[] _eventBuffer = new byte[Sdl2.EventBufferSize];
    private nint _window;
    private nint _renderer;
    private nint _texture;
    private LvPointerState _pointer;
    private bool _closeRequested;
    private bool _disposed;

    /// <summary>Opens a window and prepares the rendering pipeline.</summary>
    /// <param name="width">Surface width in pixels.</param>
    /// <param name="height">Surface height in pixels.</param>
    /// <param name="title">Window caption.</param>
    /// <param name="vsync">
    /// Synchronise presentation to the display refresh. Leave on for desktop use; turning it off is
    /// mainly useful when benchmarking, where a capped frame rate hides the real cost.
    /// </param>
    public SdlBackend(int width, int height, string title = "LVGL.Net", bool vsync = true)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        Width = width;
        Height = height;

        if (Sdl2.Init(Sdl2.InitVideo | Sdl2.InitEvents) != 0)
        {
            throw new LvglException($"SDL_Init failed: {Sdl2.GetError()}. Is SDL2 installed? {InstallHint}");
        }

        _window = Sdl2.CreateWindow(title, Sdl2.WindowPosCentered, Sdl2.WindowPosCentered, width, height,
            Sdl2.WindowShown | Sdl2.WindowAllowHighDpi);
        if (_window == 0) throw Fail("SDL_CreateWindow");

        var rendererFlags = Sdl2.RendererAccelerated | (vsync ? Sdl2.RendererPresentVSync : 0);
        _renderer = Sdl2.CreateRenderer(_window, -1, rendererFlags);
        if (_renderer == 0)
        {
            // Headless CI machines and some Pi configurations have no accelerated driver.
            _renderer = Sdl2.CreateRenderer(_window, -1, Sdl2.RendererSoftware);
            if (_renderer == 0) throw Fail("SDL_CreateRenderer");
        }

        _texture = Sdl2.CreateTexture(_renderer, Sdl2.PixelFormatArgb8888, Sdl2.TextureAccessStreaming, width, height);
        if (_texture == 0) throw Fail("SDL_CreateTexture");
    }

    private static string InstallHint => OperatingSystem.IsWindows()
        ? "Windows has no system SDL2: download SDL2-<version>-win32-x64.zip from " +
          "https://github.com/libsdl-org/SDL/releases and put SDL2.dll in runtimes/win-x64/native/ " +
          "(next to lvglnet.dll), or anywhere else on the probe path."
        : OperatingSystem.IsMacOS()
            ? "Install it with: brew install sdl2"
            : "Install it with: sudo apt install libsdl2-2.0-0";

    private LvglException Fail(string call)
    {
        var error = Sdl2.GetError();
        Dispose();
        return new LvglException($"{call} failed: {error}");
    }

    public int Width { get; }

    public int Height { get; }

    public LvPointerState Pointer => _pointer;

    /// <summary>Changes the window caption.</summary>
    public void SetTitle(string title)
    {
        if (_window != 0) Sdl2.SetWindowTitle(_window, title);
    }

    public void Blit(in LvArea area, nint pixels, bool isLastFlush)
    {
        if (_texture == 0) return;

        var rect = new Sdl2.Rect { X = area.X1, Y = area.Y1, W = area.Width, H = area.Height };

        // The source pitch is the region's own width: LVGL renders each area tightly packed.
        Sdl2.UpdateTexture(_texture, &rect, (void*)pixels, area.Width * 4);

        if (!isLastFlush) return;

        Sdl2.RenderCopy(_renderer, _texture, null, null);
        Sdl2.RenderPresent(_renderer);
    }

    public bool PumpEvents()
    {
        fixed (byte* buffer = _eventBuffer)
        {
            while (Sdl2.PollEvent(buffer) != 0)
            {
                HandleEvent(buffer);
            }
        }

        return !_closeRequested;
    }

    private void HandleEvent(byte* e)
    {
        var type = *(uint*)(e + Sdl2.OffsetType);

        switch (type)
        {
            case Sdl2.EventQuit:
                _closeRequested = true;
                break;

            case Sdl2.EventWindow when e[Sdl2.OffsetWindowEventId] == Sdl2.WindowEventClose:
                _closeRequested = true;
                break;

            case Sdl2.EventMouseMotion:
                _pointer = _pointer with
                {
                    X = *(int*)(e + Sdl2.OffsetMouseX),
                    Y = *(int*)(e + Sdl2.OffsetMouseY),
                };
                break;

            case Sdl2.EventMouseButtonDown when e[Sdl2.OffsetMouseButton] == Sdl2.ButtonLeft:
                _pointer = new LvPointerState(*(int*)(e + Sdl2.OffsetMouseX), *(int*)(e + Sdl2.OffsetMouseY), true);
                break;

            case Sdl2.EventMouseButtonUp when e[Sdl2.OffsetMouseButton] == Sdl2.ButtonLeft:
                _pointer = new LvPointerState(*(int*)(e + Sdl2.OffsetMouseX), *(int*)(e + Sdl2.OffsetMouseY), false);
                break;

            case Sdl2.EventKeyDown:
            case Sdl2.EventKeyUp:
                {
                    var sym = *(int*)(e + Sdl2.OffsetKeySym);
                    if (SdlKeyMap.TryTranslate(sym, out var lvglKey))
                    {
                        _keys.Enqueue((lvglKey, type == Sdl2.EventKeyDown));
                    }
                    break;
                }
        }
    }

    public bool TryDequeueKey(out uint key, out bool pressed)
    {
        if (_keys.TryDequeue(out var entry))
        {
            (key, pressed) = entry;
            return true;
        }

        key = 0;
        pressed = false;
        return false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_texture != 0) { Sdl2.DestroyTexture(_texture); _texture = 0; }
        if (_renderer != 0) { Sdl2.DestroyRenderer(_renderer); _renderer = 0; }
        if (_window != 0) { Sdl2.DestroyWindow(_window); _window = 0; }

        Sdl2.Quit();
        GC.KeepAlive(this);
    }
}

/// <summary>Maps SDL keycodes onto LVGL's <c>LV_KEY_*</c> values.</summary>
internal static class SdlKeyMap
{
    // LV_KEY_* from lv_group.h. LVGL reuses ASCII control codes for navigation, so printable
    // characters pass through untouched.
    private const uint KeyUp = 17;
    private const uint KeyDown = 18;
    private const uint KeyRight = 19;
    private const uint KeyLeft = 20;
    private const uint KeyEsc = 27;
    private const uint KeyDelete = 127;
    private const uint KeyBackspace = 8;
    private const uint KeyEnter = 10;
    private const uint KeyNext = 9;
    private const uint KeyHome = 2;
    private const uint KeyEnd = 3;

    // SDL keycodes for non-printable keys are scancodes with bit 30 set.
    private const int SdlkReturn = 13;
    private const int SdlkEscape = 27;
    private const int SdlkBackspace = 8;
    private const int SdlkTab = 9;
    private const int SdlkDelete = 127;
    private const int SdlkRight = 0x4000004F;
    private const int SdlkLeft = 0x40000050;
    private const int SdlkDown = 0x40000051;
    private const int SdlkUp = 0x40000052;
    private const int SdlkHome = 0x4000004A;
    private const int SdlkEnd = 0x4000004D;

    internal static bool TryTranslate(int sdlKeycode, out uint lvglKey)
    {
        switch (sdlKeycode)
        {
            case SdlkReturn: lvglKey = KeyEnter; return true;
            case SdlkEscape: lvglKey = KeyEsc; return true;
            case SdlkBackspace: lvglKey = KeyBackspace; return true;
            case SdlkTab: lvglKey = KeyNext; return true;
            case SdlkDelete: lvglKey = KeyDelete; return true;
            case SdlkRight: lvglKey = KeyRight; return true;
            case SdlkLeft: lvglKey = KeyLeft; return true;
            case SdlkDown: lvglKey = KeyDown; return true;
            case SdlkUp: lvglKey = KeyUp; return true;
            case SdlkHome: lvglKey = KeyHome; return true;
            case SdlkEnd: lvglKey = KeyEnd; return true;
        }

        if (sdlKeycode is >= 32 and < 127)
        {
            lvglKey = (uint)sdlKeycode;
            return true;
        }

        lvglKey = 0;
        return false;
    }
}
