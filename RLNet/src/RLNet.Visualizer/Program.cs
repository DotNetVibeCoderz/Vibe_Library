// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using Avalonia;

namespace RLNet.Visualizer;

/// <summary>Entry point for the RLNet console.</summary>
public static class Program
{
    /// <summary>What the console was asked to open with. Read by <see cref="App"/>.</summary>
    public static StartupOptions Options { get; private set; } = new();

    // Avalonia must be configured before anything touches a UI type, so nothing here may run
    // before BuildAvaloniaApp.
    [STAThread]
    public static void Main(string[] args)
    {
        Options = StartupOptions.Parse(args);

        // --help, --list and a bad argument print and exit without ever opening a window. A GUI
        // that pops up to say "unknown environment" is worse than a line of text.
        if (Options.ShouldExit)
        {
            Console.WriteLine(Options.Message);
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>Avalonia configuration. Also used by the visual designer, so it must stay public.</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
