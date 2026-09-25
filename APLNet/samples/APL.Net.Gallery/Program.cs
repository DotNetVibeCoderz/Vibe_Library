// APL.Net - Another Parallel Library for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using Avalonia;

namespace AplNet.Gallery;

internal static class Program
{
    /// <summary>
    /// <c>--case &lt;id&gt;</c> opens on a case, <c>--lang id</c> starts in Indonesian, and
    /// <c>--screenshot &lt;dir&gt;</c> drives the app through its cases, runs each benchmark and saves
    /// a capture of the real window per case - how the images in docs/images are made.
    /// </summary>
    [STAThread]
    public static void Main(string[] args)
    {
        StartupOptions.Current = StartupOptions.Parse(args);
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}

internal sealed class StartupOptions
{
    public static StartupOptions Current { get; set; } = new();

    public string? CaseId { get; private set; }

    public string? ScreenshotDirectory { get; private set; }

    public bool Indonesian { get; private set; }

    public static StartupOptions Parse(string[] args)
    {
        var options = new StartupOptions();
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--case" when i + 1 < args.Length:
                    options.CaseId = args[++i];
                    break;
                case "--screenshot" when i + 1 < args.Length:
                    options.ScreenshotDirectory = args[++i];
                    break;
                case "--lang" when i + 1 < args.Length:
                    options.Indonesian = args[++i].Equals("id", StringComparison.OrdinalIgnoreCase);
                    break;
            }
        }

        return options;
    }
}
