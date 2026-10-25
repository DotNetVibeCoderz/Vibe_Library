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

    if (options.ScreenshotPath is { } screenshot && backend is HeadlessBackend headless)
    {
        // Two seconds of samples, so the chart has a trace and the gauges have moved.
        var settle = System.Diagnostics.Stopwatch.StartNew();
        var next = 0L;
        while (settle.ElapsedMilliseconds < 2500)
        {
            if (settle.ElapsedMilliseconds >= next)
            {
                dashboard.Update(sensors.Read());
                next = settle.ElapsedMilliseconds + 60;
            }

            application.RunFrame();
            Thread.Sleep(10);
        }

        application.Display.RefreshNow();
        headless.SavePng(screenshot);
        Console.WriteLine($"Saved {screenshot}");
        return 0;
    }

    if (options.BenchSeconds > 0)
    {
        Console.WriteLine($"Benchmarking for {options.BenchSeconds} s...");
        var report = Bench.Run(application, dashboard, sensors, TimeSpan.FromSeconds(options.BenchSeconds), backend.GetType().Name);
        var json = Bench.ToJson(report);

        Console.WriteLine(json);
        if (options.ReportPath is { } reportPath) File.WriteAllText(reportPath, json);
        return 0;
    }

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
    if (options.Headless)
    {
        return new HeadlessBackend(options.Width, options.Height);
    }

    if (options.UseDrm)
    {
        // DRM first, for the pointer scaling to see the mode the display actually runs.
        var drm = new DrmBackend(options.DrmDevice);
        Console.WriteLine($"DRM/KMS on {drm.DevicePath}: {drm.Mode}");

        var drmPointer = EvdevPointer.AutoDetect(drm.Width, drm.Height);
        if (drmPointer is null) return drm;

        drm.Dispose();
        return new DrmBackend(options.DrmDevice, drmPointer);
    }

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
    string FramebufferDevice,
    bool UseDrm = false,
    string? DrmDevice = null,
    bool Headless = false,
    int BenchSeconds = 0,
    string? ReportPath = null,
    string? ScreenshotPath = null);

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
        var drm = false;
        string? drmDevice = null;
        var headless = false;
        var bench = 0;
        string? report = null;
        string? screenshot = null;

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

                case "--drm":
                    drm = true;
                    if (i + 1 < args.Length && args[i + 1].StartsWith("/dev/", StringComparison.Ordinal)) drmDevice = args[++i];
                    break;

                case "--headless":
                    headless = true;
                    break;

                case "--bench" when i + 1 < args.Length && int.TryParse(args[++i], out var seconds):
                    bench = Math.Clamp(seconds, 1, 3600);
                    simulate = true;
                    break;

                case "--report" when i + 1 < args.Length:
                    report = args[++i];
                    break;

                case "--screenshot" when i + 1 < args.Length:
                    screenshot = args[++i];
                    headless = true;
                    simulate = true;
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

        return new RaspiOptions(useSdl, simulate, width, height, interval, device, drm, drmDevice, headless, bench, report, screenshot);
    }

    private static void PrintUsage() => Console.WriteLine("""
        LVGL.Raspi - live sensor dashboard for a Raspberry Pi 4

          dotnet run --project samples/LVGL.Raspi -- [options]

        Options:
              --sdl              Render into a window instead of the framebuffer (for development)
              --simulate         Use fake telemetry instead of the board's real sensors
              --fb <device>      Framebuffer device (default /dev/fb0)
              --drm [device]     Use DRM/KMS with page flipping instead of fbdev - no tearing.
                                 Needs the display to itself (no desktop on it).
              --headless         Render off-screen (for --bench on a machine without a display)
              --bench <seconds>  Measure frame time, allocation and memory; prints JSON
              --report <file>    Also write the --bench JSON to a file
              --screenshot <png> Render the dashboard off-screen with simulated data and save it
          -w, --width <px>       Window width when using --sdl (default 800)
          -h, --height <px>      Window height when using --sdl (default 480)
              --interval <ms>    Sampling interval (default 1000)
          -?, --help             Show this help

        On the Pi, the framebuffer and input devices are group-owned:
          sudo usermod -aG video,input $USER   # then log out and back in
        """);
}
