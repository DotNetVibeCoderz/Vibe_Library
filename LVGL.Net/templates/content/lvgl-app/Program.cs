using System.Diagnostics;
using Lvgl;
using Lvgl.Backends;
using Lvgl.Linux;
using Lvgl.Sdl;
using Company.LvglTemplateApp;

// One program for the desk and the device:
//   dotnet run                          a desktop window (SDL)
//   dotnet run -- --drm                 the Pi display through DRM/KMS - no tearing, no desktop needed
//   dotnet run -- --fb                  the Pi display through the framebuffer (/dev/fb0)
//   dotnet run -- --headless --frames 60 --screenshot shot.png   off-screen, saves a PNG
var options = Options.Parse(args);

using var backend = CreateBackend(options);
using var app = LvglApplication.Create(backend, new LvglOptions
{
    // Two small strips instead of two full-screen buffers: what a 1 GB Pi wants.
    RenderMode = LvRenderMode.Partial,
    PartialBufferLines = 32,
});

Console.WriteLine($"{LvglAbout.Banner}  |  LVGL {LvglRuntime.LvglVersion} on {backend.GetType().Name} {backend.Width}x{backend.Height}");

// A failing event handler, timer or animation is caught before it reaches LVGL's C code; log it.
LvglRuntime.CallbackFailed += (_, e) => Console.Error.WriteLine($"{e.Source}: {e.Exception}");

var screen = new MainScreen();
screen.Build(app.Screen);

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancellation.Cancel();
};

if (options.Frames > 0)
{
    // A bounded run, for scripts and CI: render, optionally save what was drawn, exit.
    var clock = Stopwatch.StartNew();
    for (var i = 0; i < options.Frames && app.RunFrame(); i++) Thread.Sleep(15);

    if (options.Screenshot is { } path && backend is HeadlessBackend headless)
    {
        app.Display.RefreshNow();
        headless.SavePng(path);
        Console.WriteLine($"Saved {path}");
    }

    Console.WriteLine($"Rendered {app.FrameCount} frames in {clock.ElapsedMilliseconds} ms ({app.Display.FlushCount} flushes)");
}
else
{
    app.Run(cancellation.Token);
}

// Widgets first, then what they point at.
app.Screen.Clear();
screen.Destroy();
return 0;

static ILvglBackend CreateBackend(Options options)
{
    if (options.Headless) return new HeadlessBackend(options.Width, options.Height);

    if (options.Drm || options.Framebuffer)
    {
        // Touch panel or mouse, scaled to the display. Null when there is none: the UI still shows.
        var pointer = EvdevPointer.AutoDetect(options.Width, options.Height);
        return options.Drm
            ? new DrmBackend(options.Device, pointer, options.Width, options.Height)
            : new FramebufferBackend(options.Device ?? "/dev/fb0", pointer);
    }

    return new SdlBackend(options.Width, options.Height, "Company.LvglTemplateApp");
}

internal sealed record Options(int Width, int Height, bool Drm, bool Framebuffer, bool Headless, string? Device, int Frames, string? Screenshot)
{
    public static Options Parse(string[] args)
    {
        int width = MainScreen.DesignWidth, height = MainScreen.DesignHeight, frames = 0;
        bool drm = false, framebuffer = false, headless = false;
        string? device = null, screenshot = null;

        for (var i = 0; i < args.Length; i++)
        {
            string? Next() => i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[++i] : null;

            switch (args[i])
            {
                case "--drm": drm = true; device = Next(); break;
                case "--fb": framebuffer = true; device = Next(); break;
                case "--headless": headless = true; break;
                case "--width": width = int.Parse(Next() ?? "0"); break;
                case "--height": height = int.Parse(Next() ?? "0"); break;
                case "--frames": frames = int.Parse(Next() ?? "0"); break;
                case "--screenshot": screenshot = Next(); headless = true; frames = Math.Max(frames, 60); break;
                default: Console.Error.WriteLine($"Ignoring unknown argument '{args[i]}'."); break;
            }
        }

        return new Options(width, height, drm, framebuffer, headless, device, frames, screenshot);
    }
}
