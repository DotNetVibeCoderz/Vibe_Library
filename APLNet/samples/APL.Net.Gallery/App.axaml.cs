// APL.Net - Another Parallel Library for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using AplNet.Gallery.Infrastructure;
using AplNet.Gallery.ViewModels;
using AplNet.Gallery.Views;

namespace AplNet.Gallery;

public sealed partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            StartupOptions options = StartupOptions.Current;
            Loc.Instance.Indonesian = options.Indonesian;

            var main = new MainViewModel();
            if (options.CaseId is { } id && main.Find(id) is { } start)
                main.Select(start);

            var window = new MainWindow { DataContext = main };
            desktop.MainWindow = window;

            if (options.ScreenshotDirectory is { } directory)
                window.Opened += async (_, _) => await ScreenshotTour.RunAsync(window, main, directory, desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
