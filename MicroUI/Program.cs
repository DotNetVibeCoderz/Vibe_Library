using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using MicroUI.Core;
using MicroUI.Samples;
using MicroUI.Simulator;
using System;

namespace MicroUI
{
    class Program
    {
        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        public static void Main(string[] args)
        {
            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToTrace();
    }

    public class App : Application
    {
        public override void Initialize()
        {
            Styles.Add(new FluentTheme());
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // Create Main Menu as Root
                var root = SampleScreens.CreateMainMenu();
                
                var window = new SimulatorWindow(root);
                
                // Wire up navigation logic
                SampleScreens.Navigate = (newScreen) => {
                    // We need to run this on UI Thread
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                        window.SwitchScreen(newScreen);
                    });
                };

                desktop.MainWindow = window;
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
