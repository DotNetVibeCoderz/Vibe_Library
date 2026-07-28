using Lvgl;
using Lvgl.Backends;
using Lvgl.Interop;
using Lvgl.Linux;
using Lvgl.Samples.Raspi;
using Lvgl.Samples.Raspi.Sensors;
using Lvgl.Sdl;

var options = RaspiCommandLine.Parse(args);
if (options is null) return 0;

ISensorSource? sensors = null;
ILvglBackend? backend = null;

try
{
    backend = CreateBackend(options);
    sensors = options.Simulate || !LinuxSensorSource.IsAvailable
        ? new SimulatedSensorSource()
        : new LinuxSensorSource();

    using var application = LvglApplication.Create(backend, new LvglOptions
    {
        RenderMode = LvRenderMode.Partial,

        // A shallower buffer than the default tenth of the screen: on a 1 GiB Pi the extra
        // headroom is worth more than the handful of additional flush calls it costs.
        PartialBufferLines = 32,
    });

    Console.WriteLine(LvglAbout.Banner);
    Console.WriteLine($"LVGL {LvglRuntime.LvglVersion} on {backend.GetType().Name} " +
                      $"{backend.Width}x{backend.Height}, draw buffers " +
                      $"{application.Display.AllocatedBytes / 1024} KiB");
    Console.WriteLine(sensors.Description);

    var dashboard = new Dashboard(backend.Width, backend.Height, sensors.Description);
    dashboard.Build(application.Screen);

    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cancellation.Cancel();
    };

    // Sampling runs off the UI thread because reading /proc and /sys hits the filesystem, and a
    // blocked render loop shows up immediately as a stuttering chart. Results are handed back
    // with Post, the only supported way to touch widgets from another thread.
    var sampler = new Thread(() => SampleLoop(application, dashboard, sensors, options.IntervalMs, cancellation.Token))
    {
        IsBackground = true,
        Name = "sensor-sampler",
    };
    sampler.Start();

    application.Run(cancellation.Token);
    cancellation.Cancel();

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
    return 1;
}
finally
{
    sensors?.Dispose();
    backend?.Dispose();
}

static void SampleLoop(
    LvglApplication application,
    Dashboard dashboard,
    ISensorSource sensors,
    int intervalMs,
    CancellationToken cancellationToken)
{
    while (!cancellationToken.IsCancellationRequested)
    {
        try
        {
            var reading = sensors.Read();
            application.Post(() => dashboard.Update(reading));
        }
        catch (Exception ex)
        {
            var message = ex.Message;
            application.Post(() => dashboard.SetStatus($"{LvSymbols.Warning} sensor read failed: {message}"));
        }

        try
        {
            Task.Delay(intervalMs, cancellationToken).Wait(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }
    }
}

static ILvglBackend CreateBackend(RaspiOptions options)
{
    if (options.UseSdl)
    {
        return new SdlBackend(options.Width, options.Height, "LVGL.Raspi (development window)");
    }

    if (!OperatingSystem.IsLinux())
    {
        Console.WriteLine("Not running on Linux - falling back to an SDL window.");
        return new SdlBackend(options.Width, options.Height, "LVGL.Raspi (development window)");
    }

    try
    {
        // Probe the framebuffer first so the pointer can be scaled to its real geometry.
        var probe = new FramebufferBackend(options.FramebufferDevice);
        var pointer = EvdevPointer.AutoDetect(probe.Width, probe.Height);
        if (pointer is null)
        {
            Console.WriteLine("No usable input device found; the dashboard will run without touch input.");
            return probe;
        }

        probe.Dispose();
        return new FramebufferBackend(options.FramebufferDevice, pointer);
    }
    catch (LvglException ex)
    {
        Console.WriteLine($"Framebuffer unavailable ({ex.Message.Split('.')[0]}); falling back to an SDL window.");
        return new SdlBackend(options.Width, options.Height, "LVGL.Raspi (development window)");
    }
}

internal sealed record RaspiOptions(
    bool UseSdl,
    bool Simulate,
    int Width,
    int Height,
    int IntervalMs,
    string FramebufferDevice);

internal static class RaspiCommandLine
{
    public static RaspiOptions? Parse(string[] args)
    {
        var useSdl = false;
        var simulate = false;
        var width = 800;
        var height = 480;
        var interval = 1000;
        var device = "/dev/fb0";

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--sdl":
                    useSdl = true;
                    break;

                case "--simulate":
                    simulate = true;
                    break;

                case "--fb" when i + 1 < args.Length:
                    device = args[++i];
                    break;

                case "--width" or "-w" when i + 1 < args.Length && int.TryParse(args[++i], out var w):
                    width = w;
                    break;

                case "--height" or "-h" when i + 1 < args.Length && int.TryParse(args[++i], out var h):
                    height = h;
                    break;

                case "--interval" when i + 1 < args.Length && int.TryParse(args[++i], out var ms):
                    interval = Math.Max(100, ms);
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

        return new RaspiOptions(useSdl, simulate, width, height, interval, device);
    }

    private static void PrintUsage() => Console.WriteLine("""
        LVGL.Raspi - live sensor dashboard for a Raspberry Pi 4

          dotnet run --project samples/LVGL.Raspi -- [options]

        Options:
              --sdl              Render into a window instead of the framebuffer (for development)
              --simulate         Use fake telemetry instead of the board's real sensors
              --fb <device>      Framebuffer device (default /dev/fb0)
          -w, --width <px>       Window width when using --sdl (default 800)
          -h, --height <px>      Window height when using --sdl (default 480)
              --interval <ms>    Sampling interval (default 1000)
          -?, --help             Show this help

        On the Pi, the framebuffer and input devices are group-owned:
          sudo usermod -aG video,input $USER   # then log out and back in
        """);
}
