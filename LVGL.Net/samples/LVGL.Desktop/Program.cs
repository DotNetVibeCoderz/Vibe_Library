using Lvgl;
using Lvgl.Interop;
using Lvgl.Samples.Desktop;
using Lvgl.Samples.Desktop.Demos;
using Lvgl.Sdl;

var options = CommandLine.Parse(args);
if (options is null) return 0;

try
{
    using var backend = new SdlBackend(options.Width, options.Height, "LVGL.Net - desktop demos", options.VSync);
    using var application = LvglApplication.Create(backend, new LvglOptions
    {
        // Partial rendering keeps the draw buffers small, which is the configuration the
        // Raspberry Pi sample runs with too - the desktop demo should exercise the same path.
        RenderMode = LvRenderMode.Partial,
        EnableKeyboard = true,
    });

    Console.WriteLine(LvglAbout.Banner);
    Console.WriteLine($"LVGL {LvglRuntime.LvglVersion}  |  {backend.Width}x{backend.Height}  |  " +
                      $"draw buffers {application.Display.AllocatedBytes / 1024} KiB");

    DemoPage[] pages =
    [
        new WidgetGalleryDemo(),
        new ChartDemo(),
        new LayoutDemo(),
        new StyleDemo(),
        new DesignerLayoutDemo(),
        new NativeDemoPage(),
    ];

    var shell = new DemoShell(application, pages);
    shell.Build();

    // A cheap frame-rate readout: the status text is refreshed once a second from the run loop.
    var lastReport = Environment.TickCount64;
    var lastFrame = 0L;
    application.FrameCompleted += (_, _) =>
    {
        var now = Environment.TickCount64;
        if (now - lastReport < 1000) return;

        var frames = application.FrameCount - lastFrame;
        shell.SetStatus($"{frames} fps");
        lastFrame = application.FrameCount;
        lastReport = now;
    };

    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cancellation.Cancel();
    };

    if (options.Frames > 0)
    {
        // Bounded run for smoke tests: render a fixed number of frames, report what happened and
        // exit, so the sample can be verified without someone watching the window.
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        for (var i = 0; i < options.Frames; i++)
        {
            if (!application.RunFrame()) break;
            Thread.Sleep(1);
        }

        stopwatch.Stop();

        Console.WriteLine($"Rendered {application.FrameCount} frames in {stopwatch.ElapsedMilliseconds} ms " +
                          $"({application.Display.FlushCount} region flushes)");

        return application.Display.FlushCount > 0 ? 0 : 3;
    }

    application.Run(cancellation.Token);
    return 0;
}
catch (LvglNativeNotFoundException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}
catch (LvglException ex)
{
    Console.Error.WriteLine($"LVGL error: {ex.Message}");
    if (ex.InnerException is not null) Console.Error.WriteLine(ex.InnerException);
    return 1;
}

internal sealed record CommandLineOptions(int Width, int Height, bool VSync, int Frames);

internal static class CommandLine
{
    /// <summary>Parses the command line, or returns null when help was requested.</summary>
    public static CommandLineOptions? Parse(string[] args)
    {
        var width = 1024;
        var height = 600;
        var vsync = true;
        var frames = 0;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--width" or "-w" when i + 1 < args.Length && int.TryParse(args[++i], out var w):
                    width = w;
                    break;

                case "--height" or "-h" when i + 1 < args.Length && int.TryParse(args[++i], out var h):
                    height = h;
                    break;

                case "--no-vsync":
                    vsync = false;
                    break;

                case "--frames" when i + 1 < args.Length && int.TryParse(args[++i], out var f):
                    frames = f;
                    break;

                case "--help" or "-?":
                    PrintUsage();
                    return null;

                default:
                    Console.Error.WriteLine($"Unrecognised argument '{args[i]}'.");
                    PrintUsage();
                    return null;
            }
        }

        return new CommandLineOptions(width, height, vsync, frames);
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
            LVGL.Net desktop demos

              dotnet run --project samples/LVGL.Desktop -- [options]

            Options:
              -w, --width <px>     Window width  (default 1024)
              -h, --height <px>    Window height (default 600)
                  --no-vsync       Do not synchronise to the display refresh
                  --frames <n>     Render n frames, print a summary and exit (smoke test)
              -?, --help           Show this help

            Requires SDL2 and the native lvglnet library (build it with native/build.sh or native/build.ps1).
            """);
    }
}
