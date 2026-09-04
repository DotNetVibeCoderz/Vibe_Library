// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.
//
// Run it:
//   dotnet run --project samples/ActorNet.Samples.Avalonia

using Avalonia;

namespace ActorNet.Samples.Avalonia;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .WithInterFont()
        .LogToTrace();
}
