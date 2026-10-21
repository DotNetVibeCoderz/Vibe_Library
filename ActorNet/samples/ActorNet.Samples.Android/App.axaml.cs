// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using ActorNet.Samples.Android.ViewModels;
using ActorNet.Samples.Android.Views;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace ActorNet.Samples.Android;

public partial class App : Avalonia.Application
{
    /// <summary>
    /// The node's owner. One per process, not per view: Android may tear the view down and build
    /// it again, and a node that restarted with it would leave and rejoin the cluster each time.
    /// </summary>
    public static NodeViewModel? Node { get; private set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            Node ??= new NodeViewModel();
            singleView.MainView = new MainView { DataContext = Node };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
