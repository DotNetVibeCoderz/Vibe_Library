// APL.Net - Another Parallel Library for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using AplNet.Gallery.ViewModels;

namespace AplNet.Gallery.Infrastructure;

/// <summary>
/// <c>--screenshot &lt;dir&gt;</c>: walks the gallery like a user would - open a case, press Run, wait
/// for the bars and lanes - and saves what the window shows. The images in docs/images come from here,
/// so they are the real app on real hardware, and anyone can regenerate them.
/// </summary>
public static class ScreenshotTour
{
    private static readonly string[] s_cases =
    [
        "trivial", "overhead", "particles", "image", "matrix", "axpy", "sum", "dot", "montecarlo", "mandelbrot", "zipf", "unsafe", "sourcegen", "medium",
    ];

    public static async Task RunAsync(Window window, MainViewModel main, string directory, IClassicDesktopStyleApplicationLifetime desktop)
    {
        try
        {
            Directory.CreateDirectory(directory);
            await Task.Delay(1500);
            await CaptureWindowAsync(window, Path.Combine(directory, "gallery-overview.png"));

            foreach (string id in s_cases)
            {
                CaseViewModel? target = main.Find(id);
                if (target is null)
                    continue;

                main.Select(target);
                await Task.Delay(500);
                await target.RunAsync();
                await Task.Delay(1200);
                await CaptureWindowAsync(window, Path.Combine(directory, $"gallery-{id}.png"));
                await CapturePageAsync(window, Path.Combine(directory, $"gallery-{id}-full.png"));
            }

            main.ShowOverview();
            await Task.Delay(800);
            await CaptureNamedAsync(window, "SummaryPanel", Path.Combine(directory, "gallery-summary.png"));

            main.ToggleLanguage();
            main.Select(main.Find("mandelbrot")!);
            await Task.Delay(1200);
            await CaptureWindowAsync(window, Path.Combine(directory, "gallery-mandelbrot-id.png"));
            main.ShowOverview();
            await Task.Delay(800);
            await CaptureWindowAsync(window, Path.Combine(directory, "gallery-overview-id.png"));
        }
        catch (Exception ex)
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "screenshot-error.txt"), ex.ToString());
        }
        finally
        {
            desktop.Shutdown();
        }
    }

    private static Task CaptureWindowAsync(Window window, string path) => CaptureAsync(window, window.Bounds.Size, path);

    /// <summary>The whole scrollable page of the current view, not only the part in the viewport.</summary>
    private static Task CapturePageAsync(Window window, string path)
    {
        Control? page = window.GetVisualDescendants().OfType<StackPanel>().FirstOrDefault(p => p.Name == "PageRoot");
        return page is null ? Task.CompletedTask : CaptureAsync(page, page.Bounds.Size, path);
    }

    private static Task CaptureNamedAsync(Window window, string name, string path)
    {
        Control? control = window.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c.Name == name);
        return control is null ? Task.CompletedTask : CaptureAsync(control, control.Bounds.Size, path);
    }

    private static async Task CaptureAsync(Visual visual, Size size, string path)
    {
        await Task.Delay(100);
        double scale = (visual.GetVisualRoot() as TopLevel)?.RenderScaling ?? 1;
        var pixels = new PixelSize((int)Math.Ceiling(size.Width * scale), (int)Math.Ceiling(size.Height * scale));
        using var bitmap = new RenderTargetBitmap(pixels, new Vector(96 * scale, 96 * scale));
        bitmap.Render(visual);
        bitmap.Save(path);
    }
}
