// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using ActorNet.Samples.Avalonia.ViewModels;
using ActorNet.Samples.Avalonia.Views;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace ActorNet.Samples.Avalonia;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var shell = new ShellViewModel();
            desktop.MainWindow = new MainWindow { DataContext = shell };

            // The node outlives every view, so it is started once here and shut down with the
            // window rather than per scenario.
            desktop.ShutdownRequested += (_, _) => shell.ShutdownAsync().GetAwaiter().GetResult();
            _ = shell.StartAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
