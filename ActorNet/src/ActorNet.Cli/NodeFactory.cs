// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using ActorNet.Demo;
using ActorNet.Persistence;
using Microsoft.Extensions.Logging;
using Spectre.Console;

namespace ActorNet.Cli;

/// <summary>Settings shared by every command that starts a node.</summary>
public class NodeSettings : Spectre.Console.Cli.CommandSettings
{
    [Spectre.Console.Cli.CommandOption("--node-id <ID>")]
    [System.ComponentModel.Description("This node's identity in the cluster. Defaults to a name derived from the machine and process.")]
    public string? NodeId { get; init; }

    [Spectre.Console.Cli.CommandOption("--host <HOST>")]
    [System.ComponentModel.Description("Address to bind. Default 127.0.0.1.")]
    public string Host { get; init; } = "127.0.0.1";

    [Spectre.Console.Cli.CommandOption("-p|--port <PORT>")]
    [System.ComponentModel.Description("Port to bind. 0 picks a free one. Default 9000.")]
    public int Port { get; init; } = 9000;

    [Spectre.Console.Cli.CommandOption("--seed <HOST:PORT>")]
    [System.ComponentModel.Description("A cluster seed to join. Repeat for several. Supplying any seed turns clustering on.")]
    public string[] Seeds { get; init; } = [];

    [Spectre.Console.Cli.CommandOption("--data <DIRECTORY>")]
    [System.ComponentModel.Description("Persist state and events under this directory instead of in memory, so they survive a restart.")]
    public string? DataDirectory { get; init; }

    [Spectre.Console.Cli.CommandOption("--idle-timeout <SECONDS>")]
    [System.ComponentModel.Description("Deactivate an actor after this many seconds without messages. Default 300.")]
    public int IdleTimeoutSeconds { get; init; } = 300;

    [Spectre.Console.Cli.CommandOption("-v|--verbose")]
    [System.ComponentModel.Description("Log the runtime's own activity at debug level.")]
    public bool Verbose { get; init; }
}

/// <summary>Builds a node from the shared command-line settings.</summary>
internal static class NodeFactory
{
    /// <summary>Creates and starts a node with the demo domain registered.</summary>
    public static async Task<ActorSystem> StartAsync(NodeSettings settings, bool networking = true, CancellationToken cancellationToken = default)
    {
        var options = new ActorSystemOptions
        {
            Host = settings.Host,
            Port = settings.Port,
            EnableNetworking = networking,
            IdleTimeout = TimeSpan.FromSeconds(Math.Max(1, settings.IdleTimeoutSeconds)),
            SweepInterval = TimeSpan.FromSeconds(Math.Clamp(settings.IdleTimeoutSeconds / 4.0, 1, 30)),
        };

        if (settings.NodeId is { Length: > 0 } id) options.NodeId = id;

        if (settings.Seeds.Length > 0)
        {
            options.Cluster.Enabled = true;
            options.Cluster.Seeds = settings.Seeds.ToList();
        }

        if (settings.DataDirectory is { Length: > 0 } directory)
        {
            // A file-backed node is what makes "stop it, start it again, the balance is still
            // there" demonstrable rather than merely claimed.
            var journalTypes = new Serialization.MessageTypeRegistry();
            journalTypes.RegisterFromAssembly(typeof(DemoCatalog).Assembly);

            options.StateStore = new FileStateStore(Path.Combine(directory, "state"));
            options.EventJournal = new FileEventJournal(Path.Combine(directory, "journal"), journalTypes);
            options.SnapshotStore = new FileSnapshotStore(Path.Combine(directory, "snapshots"));
        }

        var system = new ActorSystem(options, BuildLoggerFactory(settings.Verbose));
        system.RegisterDemoDomain();

        await system.StartAsync(cancellationToken);
        return system;
    }

    /// <summary>
    /// Logs to the console, quietly by default.
    /// </summary>
    /// <remarks>
    /// The runtime logs an activation and a deactivation for every actor at debug level. In a live
    /// dashboard that is thousands of lines a second scrolling over the thing the user is trying to
    /// read, so it stays behind <c>--verbose</c>.
    /// </remarks>
    private static ILoggerFactory BuildLoggerFactory(bool verbose) => LoggerFactory.Create(builder =>
    {
        builder.AddSimpleConsole(console =>
        {
            console.SingleLine = true;
            console.TimestampFormat = "HH:mm:ss ";
        });

        builder.SetMinimumLevel(verbose ? LogLevel.Debug : LogLevel.Warning);
    });

    /// <summary>Prints what a node came up as, including the port when the caller asked for any port.</summary>
    public static void Describe(ActorSystem system)
    {
        var table = Theme.Facts()
            .Fact("Node", system.NodeId)
            .Fact("Listening", system.Options.EnableNetworking ? $"{system.Options.Host}:{system.BoundPort}" : "in-process only")
            .Fact("Clustering", system.Options.Cluster.Enabled
                ? $"on, {system.Cluster.Members.Count} member(s)"
                : "off, standalone")
            .Fact("Idle timeout", $"{system.Options.IdleTimeout.TotalSeconds:N0}s")
            .Fact("Persistence", system.Options.StateStore is FileStateStore ? "files on disk" : "in memory");

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }
}
