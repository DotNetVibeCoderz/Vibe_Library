using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using ScrapyGallery.Controls;

namespace ScrapyGallery;

// Scrapy Gallery — every Scrapy.Net feature as a runnable, visual demo.
// Dibuat oleh Gravicode Studios dipimpin oleh Kang Fadhil.
//
//   dotnet run --project samples/ScrapyGallery                              # the app
//   dotnet run --project samples/ScrapyGallery -- --screenshots docs/images # regenerate doc screenshots
internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var shots = Array.IndexOf(args, "--screenshots");
        if (shots >= 0) return Screenshots.Run(args.Length > shots + 1 ? args[shots + 1] : "screenshots", args.Contains("--quick"));
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}

/// <summary>
/// Renders the gallery without a display (Avalonia.Headless with the real Skia renderer), runs a set
/// of demos and saves each finished frame as PNG — the screenshots in the README and docs.
/// </summary>
internal static class Screenshots
{
    private sealed record Shot(string Demo, int Tab, string File, bool Indonesian = false);

    private static readonly Shot[] Plan =
    [
        new("firstspider", 0, "gallery-first-spider.png"),
        new("crawlrules", 1, "gallery-crawlspider-items.png"),
        new("linkgraph", 3, "gallery-link-graph.png"),
        new("selectors", 1, "gallery-selectors.png"),
        new("retries", 1, "gallery-retries.png"),
        new("throttle", 3, "gallery-autothrottle.png"),
        new("media", 4, "gallery-media.png"),
        new("cache", 4, "gallery-http-cache.png"),
        new("rag", 1, "gallery-rag.png", Indonesian: true),
        new("jobs", 4, "gallery-platform-jobs.png"),
        new("rulebuilder", 1, "gallery-rule-builder.png"),
        new("browser", 4, "gallery-browser.png"),
    ];

    public static int Run(string folder, bool quick)
    {
        Directory.CreateDirectory(folder);
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();

        var exit = 0;
        using var cts = new CancellationTokenSource();
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                IconRenderer.Render(Path.Combine(folder, "icon.png"));
                var window = new MainWindow { Width = 1440, Height = 920 };
                window.Show();
                await window.EnsureSiteAsync();
                foreach (var shot in quick ? Plan.Take(2) : Plan)
                {
                    window.SetIndonesian(shot.Indonesian);
                    window.Select(shot.Demo);
                    await Task.Delay(100);
                    await window.RunAsync();
                    window.SelectTab(shot.Tab);
                    // Let the grow-in animation finish and the layout settle before capturing.
                    for (var i = 0; i < 12; i++)
                    {
                        window.ForceRefresh();
                        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                        await Task.Delay(60);
                    }
                    using var frame = window.CaptureRenderedFrame();
                    var path = Path.Combine(folder, shot.File);
                    frame!.Save(path);
                    Console.WriteLine($"saved {path}");
                }
                window.SetIndonesian(false);
                window.Close();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                exit = 1;
            }
            finally
            {
                cts.Cancel();
            }
        });
        Dispatcher.UIThread.MainLoop(cts.Token);
        return exit;
    }
}
