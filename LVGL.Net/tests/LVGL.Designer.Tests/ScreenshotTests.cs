using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

namespace Lvgl.Designer.Tests;

/// <summary>
/// Renders the designer and saves the frame, for reviewing the look. Writes only when
/// LVGLNET_SCREENSHOTS names a folder, so normal runs leave no files behind.
/// </summary>
public class ScreenshotTests
{
    [AvaloniaFact]
    public void The_main_window_renders()
    {
        var window = new MainWindow { Width = 1480, Height = 900 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);

        if (Environment.GetEnvironmentVariable("LVGLNET_SCREENSHOTS") is { Length: > 0 } folder)
        {
            Directory.CreateDirectory(folder);
            frame!.Save(Path.Combine(folder, "main-window.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }

        window.Close();
    }
}
