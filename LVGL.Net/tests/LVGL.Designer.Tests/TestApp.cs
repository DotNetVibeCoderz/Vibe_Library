using Avalonia;
using Avalonia.Headless;
using Lvgl.Designer.Tests;

[assembly: AvaloniaTestApplication(typeof(TestApp))]
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Lvgl.Designer.Tests;

/// <summary>
/// Boots the real designer <see cref="Lvgl.Designer.App"/> on Avalonia's headless platform. Skia
/// does the drawing (not the null renderer), so frames can be captured and checked pixel for pixel.
/// </summary>
public static class TestApp
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<Lvgl.Designer.App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
