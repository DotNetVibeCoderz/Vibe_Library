using System.Diagnostics;
using Lvgl;
using Lvgl.Backends;
using Lvgl.Gallery;
using Lvgl.Gallery.Shell;
using Lvgl.Gallery.Work;
using Lvgl.Interop;
using Lvgl.Linux;
using Lvgl.Sdl;

// LVGL.Net Gallery - the library at work across use cases, each demo with its own source.
//
//   dotnet run --project samples/LVGL.Gallery                       a window (SDL)
//   dotnet run --project samples/LVGL.Gallery -- --drm              a Raspberry Pi display, no desktop
//   dotnet run --project samples/LVGL.Gallery -- --demo snake       open one demo
//   dotnet run --project samples/LVGL.Gallery -- --screenshots out  every demo as a PNG, off-screen
var options = GalleryOptions.Parse(args);
if (options is null) return 0;

var failures = 0;
LvglRuntime.CallbackFailed += (_, e) =>
{
    failures++;
    Console.Error.WriteLine($"{e.Source}: {e.Exception}");
};

try
{
    using var backend = CreateBackend(options);
    using var app = LvglApplication.Create(backend, new LvglOptions
    {
        RenderMode = LvRenderMode.Partial,
        PartialBufferLines = 40,
        EnableKeyboard = true,
    });

    Console.WriteLine($"{LvglAbout.Banner}  |  LVGL {LvglRuntime.LvglVersion}  |  {backend.Width}x{backend.Height}");

    // Screenshots get a fresh database each time, so the Work demos always show the seeded data.
    var databasePath = options.ScreenshotFolder is not null
        ? Path.Combine(Path.GetTempPath(), $"lvglnet-gallery-{Environment.ProcessId}.db")
        : options.DatabasePath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LVGL.Net", "gallery.db");

    GalleryDatabase? database = null;
    using var fonts = new Typeface(Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts"));
    var context = new DemoContext(app, fonts, () => database ??= new GalleryDatabase(databasePath))
    {
        Screenshot = options.ScreenshotFolder is not null,
    };

    var demos = DemoCatalog.Create();
    var shell = new GalleryShell(app, context, demos);
    shell.Build();
    shell.Show(DemoCatalog.Find(demos, options.Demo) ?? demos[0], options.Source);

    if (options.ScreenshotFolder is { } folder && backend is HeadlessBackend headless)
    {
        Directory.CreateDirectory(folder);

        void Capture(string name)
        {
            // Long enough for entrance animations to finish and timers to have produced data.
            var settle = Stopwatch.StartNew();
            while (settle.ElapsedMilliseconds < 1800)
            {
                app.RunFrame();
                Thread.Sleep(8);
            }

            app.Display.RefreshNow();
            var path = Path.Combine(folder, name);
            headless.SavePng(path);
            Console.WriteLine($"Saved {path}");
        }

        foreach (var demo in demos)
        {
            shell.Show(demo);
            Capture($"gallery-{DemoCatalog.Slug(demo.Category.ToString())}-{DemoCatalog.Slug(demo.Title)}.png");
        }

        shell.Show(demos[0], source: true);
        Capture("gallery-source.png");
    }
    else if (options.Frames > 0)
    {
        for (var i = 0; i < options.Frames && app.RunFrame(); i++) Thread.Sleep(10);
    }
    else
    {
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cancellation.Cancel();
        };
        app.Run(cancellation.Token);
    }

    // Widgets first, then what the demo owned, then the fonts and the database.
    app.Screen.Clear();
    shell.Live?.Unmount();
    database?.Dispose();
    if (options.ScreenshotFolder is not null) File.Delete(databasePath);
}
catch (LvglNativeNotFoundException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}

// A demo whose handler or timer threw is a broken demo, even if the window looked fine.
return failures == 0 ? 0 : 1;

static ILvglBackend CreateBackend(GalleryOptions options)
{
    if (options.ScreenshotFolder is not null || options.Headless) return new HeadlessBackend(options.Width, options.Height);

    if (options.Drm || options.Framebuffer)
    {
        var pointer = EvdevPointer.AutoDetect(options.Width, options.Height);
        return options.Drm
            ? new DrmBackend(options.Device, pointer, options.Width, options.Height)
            : new FramebufferBackend(options.Device ?? "/dev/fb0", pointer);
    }

    return new SdlBackend(options.Width, options.Height, "LVGL.Net Gallery");
}

internal sealed record GalleryOptions(
    int Width, int Height, string? Demo, bool Source, string? ScreenshotFolder, int Frames,
    bool Headless, bool Drm, bool Framebuffer, string? Device, string? DatabasePath)
{
    public static GalleryOptions? Parse(string[] args)
    {
        int width = 1024, height = 600, frames = 0;
        string? demo = null, screenshots = null, device = null, database = null;
        bool source = false, headless = false, drm = false, framebuffer = false;

        for (var i = 0; i < args.Length; i++)
        {
            string? Next() => i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[++i] : null;

            switch (args[i])
            {
                case "--width": width = int.Parse(Next() ?? "1024"); break;
                case "--height": height = int.Parse(Next() ?? "600"); break;
                case "--demo": demo = Next(); break;
                case "--source": source = true; break;
                case "--screenshots": screenshots = Next() ?? "screenshots"; break;
                case "--frames": frames = int.Parse(Next() ?? "0"); break;
                case "--headless": headless = true; frames = Math.Max(frames, 60); break;
                case "--drm": drm = true; device = Next(); break;
                case "--fb": framebuffer = true; device = Next(); break;
                case "--db": database = Next(); break;
                case "--list":
                    foreach (var d in DemoCatalog.Create()) Console.WriteLine($"{DemoCatalog.Slug(d.Title),-18} {d.Category.Name(),-12} {d.Summary}");
                    return null;
                default:
                    Console.WriteLine("""
                        LVGL.Net Gallery

                          --demo <name>          open a demo (see --list), e.g. --demo snake
                          --source               open it in Source view
                          --list                 list the demos
                          --width/--height <px>  display size (default 1024x600)
                          --drm [card] | --fb [dev]   run on a Linux display without a desktop
                          --screenshots <dir>    render every demo off-screen into PNGs and exit
                          --headless --frames <n>     render n frames off-screen and exit
                          --db <file>            SQLite file for the Work demos
                        """);
                    return null;
            }
        }

        return new GalleryOptions(width, height, demo, source, screenshots, frames, headless, drm, framebuffer, device, database);
    }
}
