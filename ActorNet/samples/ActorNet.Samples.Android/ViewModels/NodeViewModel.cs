// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Collections.ObjectModel;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using ActorNet.Cluster;
using ActorNet.Demo;
using ActorNet.Demo.Telemetry;
using ActorNet.Streams;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ActorNet.Samples.Android.ViewModels;

/// <summary>One member of the cluster as this phone sees it.</summary>
public sealed partial class MemberRow(string nodeId, bool isSelf) : ObservableObject
{
    public string NodeId { get; } = nodeId;
    public bool IsSelf { get; } = isSelf;

    [ObservableProperty] private string _address = "";
    [ObservableProperty] private MemberStatus _status;
    [ObservableProperty] private double _share;
}

/// <summary>One device actor: where it lives, and what it last reported.</summary>
public sealed partial class DeviceRow(string id) : ObservableObject
{
    public string Id { get; } = id;

    [ObservableProperty] private string _owner = "";
    [ObservableProperty] private bool _isLocal;
    [ObservableProperty] private bool _unanswered;
    [ObservableProperty] private double _latest;
    [ObservableProperty] private long _readings;
    [ObservableProperty] private bool _inAlarm;
}

/// <summary>
/// Owns this phone's node: starting it, joining it to a cluster, and showing what the cluster
/// looks like from here.
/// </summary>
/// <remarks>
/// <para>
/// The phone is a full member, not a client. It takes a slice of the hash ring like any other
/// node, so some of the device actors below are activated on this phone and the rest on whichever
/// peer owns their key - and the code that sends to them cannot tell which, which is the point.
/// </para>
/// <para>
/// Two ways to reach a PC. Over USB, <c>adb reverse</c> and <c>adb forward</c> tunnel one port
/// each way, so both sides see each other at 127.0.0.1 and no firewall is involved. Over Wi-Fi,
/// the phone advertises its LAN address and the PC must accept inbound connections on its port.
/// </para>
/// </remarks>
public sealed partial class NodeViewModel : ObservableObject
{
    private const int DeviceCount = 8;
    private const string UsbSeed = "127.0.0.1:9000";

    /// <summary>
    /// Short, because a peer that does not answer should cost one refresh a second or two, not
    /// freeze the screen for the default ask timeout.
    /// </summary>
    private static readonly TimeSpan AskTimeout = TimeSpan.FromSeconds(2);

    private readonly DispatcherTimer _poll;
    private ActorSystem? _system;
    private CancellationTokenSource? _streaming;
    private bool _refreshing;
    private string _wifiSeed = "";

    [ObservableProperty] private bool _usb = true;
    [ObservableProperty] private string _seed = UsbSeed;
    [ObservableProperty] private string _advertisedHost = "127.0.0.1";
    [ObservableProperty] private string _port = "9100";

    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _busy;
    [ObservableProperty] private string _status = "stopped";
    [ObservableProperty] private string _peersDial = "-";
    [ObservableProperty] private bool _isStreaming;

    [ObservableProperty] private string _throughput = "0";
    [ObservableProperty] private string _localActors = "0";
    [ObservableProperty] private string _handled = "0";
    [ObservableProperty] private string _alarmDesk = "-";

    /// <summary>Stable across launches, so the phone takes the same slice of the ring each time.</summary>
    public string NodeId { get; } = $"android-{Sanitise(global::Android.OS.Build.Model)}";

    /// <summary>The address a PC on the same Wi-Fi would dial, or a dash if there is none.</summary>
    public string WifiAddress { get; } = FindWifiAddress() ?? "-";

    public ObservableCollection<MemberRow> Members { get; } = [];
    public ObservableCollection<DeviceRow> Devices { get; } = [];

    /// <summary>Newest first, so the interesting line is always at the top.</summary>
    public ObservableCollection<string> Log { get; } = [];

    public NodeViewModel()
    {
        for (var i = 0; i < DeviceCount; i++) Devices.Add(new DeviceRow($"sensor-{i:D3}"));

        _poll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _poll.Tick += (_, _) => _ = RefreshAsync();

        Say($"This phone is {NodeId}. Wi-Fi address: {WifiAddress}.");
        Say("USB: on the PC run 'adb reverse tcp:9000 tcp:9000' and 'adb forward tcp:9100 tcp:9100', then start a node on 127.0.0.1:9000 with --cluster.");
    }

    partial void OnUsbChanged(bool value)
    {
        // Each mode keeps its own seed, so flipping back and forth does not lose a typed address.
        if (value)
        {
            _wifiSeed = Seed;
            Seed = UsbSeed;
            AdvertisedHost = "127.0.0.1";
        }
        else
        {
            Seed = _wifiSeed;
            AdvertisedHost = WifiAddress == "-" ? "" : WifiAddress;
        }
    }

    [RelayCommand]
    private async Task ToggleNodeAsync()
    {
        if (Busy) return;
        Busy = true;

        try
        {
            if (IsRunning) await StopAsync();
            else await StartAsync();
        }
        catch (Exception ex)
        {
            Say($"{ex.GetType().Name}: {ex.Message}");
            await StopAsync();
        }
        finally
        {
            Busy = false;
        }
    }

    private async Task StartAsync()
    {
        if (!int.TryParse(Port, out var port) || port is < 1 or > 65535)
            throw new ArgumentException($"'{Port}' is not a port.");

        var options = new ActorSystemOptions
        {
            NodeId = NodeId,

            // Every interface: adb forward arrives on loopback and a Wi-Fi peer on wlan0, and the
            // phone does not know in advance which one this session will use.
            Host = "0.0.0.0",
            Port = port,
            AdvertisedHost = AdvertisedHost.Trim(),
            IdleTimeout = TimeSpan.FromMinutes(2),
            SweepInterval = TimeSpan.FromSeconds(10),
        };

        // Always clustered. With no seed the phone is the first node and a PC can join it instead;
        // without Enabled it would answer a join and then never gossip, and the PC would mark it
        // unreachable while it was perfectly healthy.
        options.Cluster.Enabled = true;
        if (Seed.Trim() is { Length: > 0 } seed) options.Cluster.Seeds = [seed];

        var system = new ActorSystem(options);
        system.RegisterDemoDomain();
        await system.StartAsync();

        _system = system;
        IsRunning = true;
        PeersDial = $"{options.EffectiveAdvertisedHost}:{options.AdvertisedPort ?? system.BoundPort}";
        Say(options.Cluster.Seeds.Count == 0
            ? $"Node up on :{system.BoundPort} as the first member. Peers join with --seed {PeersDial}."
            : $"Node up on :{system.BoundPort}, joining {options.Cluster.Seeds[0]}.");

        _poll.Start();
        await RefreshAsync();
    }

    /// <summary>
    /// Starts the node without a tap, for a demo driven from the PC with <c>adb shell am start</c>.
    /// </summary>
    /// <remarks>
    /// Some phones refuse <c>adb shell input</c> outright (MIUI does, unless a separate security
    /// switch is on), so a launch intent is the one way to script this sample that works on all of
    /// them.
    /// </remarks>
    public async Task AutoStartAsync(string? seed, bool stream)
    {
        if (seed is not null) Seed = seed;
        if (!IsRunning) await ToggleNodeAsync();
        if (stream && IsRunning && !IsStreaming) ToggleStream();
    }

    /// <summary>Leaves the cluster and stops the node, if it is running.</summary>
    public async Task StopAsync()
    {
        StopStream();
        _poll.Stop();

        if (_system is { } system)
        {
            _system = null;
            await system.DisposeAsync();
            Say("Left the cluster. Peers hand this phone's keys to whoever owns them next.");
        }

        IsRunning = false;
        Status = "stopped";
        PeersDial = "-";
        Members.Clear();
        foreach (var device in Devices)
        {
            device.Owner = "";
            device.Unanswered = false;
        }
    }

    [RelayCommand]
    private void ToggleStream()
    {
        if (_system is not { } system) return;

        if (IsStreaming)
        {
            StopStream();
            Say("Stream stopped.");
            return;
        }

        _streaming = new CancellationTokenSource();
        IsStreaming = true;
        Say($"Streaming readings into {DeviceCount} device actors. Each goes to whichever node owns its key.");

        _ = ActorStream
            .Interval(TimeSpan.FromMilliseconds(50), tick => tick)
            .Select(tick =>
            {
                var device = (int)(tick % DeviceCount);

                // sensor-003 runs hot, so an alarm crosses the cluster to the desk actor - which
                // may well live on the other machine.
                var baseline = device == 3 ? 76.0 : 40.0;
                return new SensorReading($"sensor-{device:D3}", baseline + Random.Shared.NextDouble() * 8, DateTimeOffset.UtcNow);
            })
            .ToActorsAsync(system, reading => ActorId.For<DeviceActor>(reading.DeviceId), _streaming.Token)
            .ContinueWith(task =>
            {
                // A send to a peer that has gone away fails the stream rather than queueing
                // forever. Say so, instead of leaving a button that claims it is still streaming.
                if (task.IsFaulted)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        StopStream();
                        Say($"Stream stopped: {task.Exception?.GetBaseException().Message}");
                    });
                }
            }, TaskScheduler.Default);
    }

    private void StopStream()
    {
        _streaming?.Cancel();
        _streaming?.Dispose();
        _streaming = null;
        IsStreaming = false;
    }

    private async Task RefreshAsync()
    {
        // A slow peer can make one refresh outlast the timer interval. Overlapping them would only
        // pile asks onto the peer that is already behind.
        if (_refreshing || _system is not { } system) return;
        _refreshing = true;

        try
        {
            var snapshot = system.Metrics.Snapshot(includeActors: false);
            Throughput = snapshot.MessagesPerSecond.ToString("N0");
            LocalActors = snapshot.ActiveActors.ToString("N0");
            Handled = snapshot.MessagesProcessed.ToString("N0");

            RefreshMembers(system);

            var desk = ActorId.For<AlarmDeskActor>("main");
            var asks = Devices.Select(row => RefreshDeviceAsync(system, row)).ToList();
            var alarms = system.AskAsync<ActiveAlarms>(desk, new GetActiveAlarms(), AskTimeout);
            await Task.WhenAll(asks);

            var deskOwner = Describe(system, system.Cluster.OwnerOf(desk));
            try
            {
                var active = await alarms;
                AlarmDesk = active.Devices.Count == 0
                    ? $"on {deskOwner} · no alarms"
                    : $"on {deskOwner} · {string.Join(", ", active.Devices)}";
            }
            catch (Exception)
            {
                AlarmDesk = $"on {deskOwner} · no answer";
            }
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void RefreshMembers(ActorSystem system)
    {
        var members = system.Cluster.Members;
        var shares = system.Cluster.Ring.OwnershipShare();
        var up = members.Count(m => m.Status == MemberStatus.Up);

        Status = members.Count <= 1 ? "clustered · alone" : $"clustered · {up} of {members.Count} up";

        // Rows are updated in place rather than rebuilt, so the list does not flicker every second.
        for (var i = Members.Count - 1; i >= 0; i--)
        {
            if (!members.Any(m => m.NodeId == Members[i].NodeId))
            {
                Say($"{Members[i].NodeId} left the member table.");
                Members.RemoveAt(i);
            }
        }

        foreach (var member in members.OrderBy(m => m.NodeId, StringComparer.Ordinal))
        {
            var row = Members.FirstOrDefault(r => r.NodeId == member.NodeId);
            if (row is null)
            {
                row = new MemberRow(member.NodeId, member.NodeId == system.NodeId);
                Members.Add(row);
                if (!row.IsSelf) Say($"{member.NodeId} joined at {member.Address}.");
            }
            else if (row.Status != member.Status && !row.IsSelf)
            {
                Say($"{member.NodeId} is now {member.Status}.");
            }

            row.Address = member.Address;
            row.Status = member.Status;
            row.Share = shares.TryGetValue(member.NodeId, out var share) ? share : 0;
        }
    }

    private static async Task RefreshDeviceAsync(ActorSystem system, DeviceRow row)
    {
        var id = ActorId.For<DeviceActor>(row.Id);
        row.IsLocal = system.Cluster.IsLocal(id);
        row.Owner = Describe(system, system.Cluster.OwnerOf(id));

        try
        {
            var status = await system.AskAsync<DeviceStatus>(id, new GetDeviceStatus(), AskTimeout);
            row.Latest = status.Latest;
            row.Readings = status.Readings;
            row.InAlarm = status.InAlarm;
            row.Unanswered = false;
        }
        catch (Exception)
        {
            // The owner is unreachable or still starting. The row says so rather than freezing
            // on numbers that are no longer being kept up to date.
            row.Unanswered = true;
        }
    }

    private static string Describe(ActorSystem system, string owner) => owner == system.NodeId ? "this phone" : owner;

    private void Say(string line)
    {
        Log.Insert(0, $"{DateTime.Now:HH:mm:ss}  {line}");
        while (Log.Count > 100) Log.RemoveAt(Log.Count - 1);
    }

    /// <summary>A node id has to survive being a ring position, so keep it to lowercase and dashes.</summary>
    private static string Sanitise(string? model)
    {
        var chars = (model ?? "device").ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray();
        return new string(chars).Trim('-') is { Length: > 0 } clean ? clean : "device";
    }

    /// <summary>The phone's IPv4 address on Wi-Fi, preferring wlan interfaces over anything else.</summary>
    private static string? FindWifiAddress()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .OrderByDescending(n => n.Name.StartsWith("wlan", StringComparison.OrdinalIgnoreCase))
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Select(a => a.Address)
                .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a))
                ?.ToString();
        }
        catch (NetworkInformationException)
        {
            return null;
        }
    }
}
