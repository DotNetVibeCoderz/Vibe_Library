// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Collections.ObjectModel;
using ActorNet.Demo;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ActorNet.Samples.Avalonia.ViewModels;

/// <summary>
/// Owns the node and the scenario list.
/// </summary>
/// <remarks>
/// One actor system for the whole application, started once and shared by every scenario. That is
/// the point being demonstrated as much as anything in the scenarios themselves: a desktop app
/// hosts a node the same way a server does, and switching views does not restart anything.
/// </remarks>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly DispatcherTimer _vitals;

    [ObservableProperty]
    private ScenarioViewModel? _current;

    [ObservableProperty]
    private string _nodeId = "starting...";

    [ObservableProperty]
    private string _throughput = "0";

    [ObservableProperty]
    private string _activeActors = "0";

    [ObservableProperty]
    private string _processed = "0";

    [ObservableProperty]
    private string _restarts = "0";

    /// <summary>The node every scenario sends into.</summary>
    public ActorSystem System { get; }

    /// <summary>The scenarios, in the order they build on each other.</summary>
    public ObservableCollection<ScenarioViewModel> Scenarios { get; }

    public ShellViewModel()
    {
        System = new ActorSystem(new ActorSystemOptions
        {
            NodeId = $"desktop-{Environment.MachineName.ToLowerInvariant()}",

            // No socket. A desktop sample that opens a listening port on launch is a surprise,
            // and nothing here needs one - the actors are all local.
            EnableNetworking = false,
            IdleTimeout = TimeSpan.FromMinutes(2),
            SweepInterval = TimeSpan.FromSeconds(10),
        });

        System.RegisterDemoDomain();

        Scenarios =
        [
            new BankingViewModel(System),
            new TelemetryViewModel(System),
            new OrderingViewModel(System),
            new SupervisionViewModel(System),
        ];

        Current = Scenarios[0];

        _vitals = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _vitals.Tick += (_, _) => RefreshVitals();
    }

    public async Task StartAsync()
    {
        await System.StartAsync();
        NodeId = System.NodeId;
        _vitals.Start();
    }

    public async Task ShutdownAsync()
    {
        _vitals.Stop();
        await System.DisposeAsync();
    }

    private void RefreshVitals()
    {
        var snapshot = System.Metrics.Snapshot(includeActors: false);
        Throughput = snapshot.MessagesPerSecond.ToString("N0");
        ActiveActors = snapshot.ActiveActors.ToString("N0");
        Processed = snapshot.MessagesProcessed.ToString("N0");
        Restarts = snapshot.Restarts.ToString("N0");
    }

    partial void OnCurrentChanged(ScenarioViewModel? oldValue, ScenarioViewModel? newValue)
    {
        // Scenarios with a running stream keep going while they are off screen otherwise, which
        // makes the node's counters lie about what the visible page is doing.
        oldValue?.Suspend();
        newValue?.Resume();
    }
}
