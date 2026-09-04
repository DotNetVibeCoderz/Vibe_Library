// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using ActorNet.Cluster;
using ActorNet.Demo;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ActorNet.Cli.Commands;

/// <summary>Runs a node and keeps it up until Ctrl+C.</summary>
public sealed class RunCommand : AsyncCommand<NodeSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, NodeSettings settings, CancellationToken cancellationToken)
    {
        Theme.Banner();

        await using var system = await NodeFactory.StartAsync(settings);
        NodeFactory.Describe(system);

        if (settings.Port == 0)
            Theme.Info($"Bound to an ephemeral port. Other nodes join with [{Theme.Accent}]--seed {settings.Host}:{system.BoundPort}[/]");

        Theme.Success("Node is up. Press Ctrl+C to stop.");
        AnsiConsole.WriteLine();

        using var stopping = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            stopping.Cancel();
        };

        if (system.Options.Cluster.Enabled) _ = WatchMembershipAsync(system, stopping.Token);

        try { await Task.Delay(Timeout.Infinite, stopping.Token); }
        catch (OperationCanceledException) { /* Ctrl+C */ }

        Theme.Info("Leaving the cluster and deactivating actors.");
        return 0;
    }

    /// <summary>Prints membership changes as they happen, so a cluster demo is legible.</summary>
    private static async Task WatchMembershipAsync(ActorSystem system, CancellationToken cancellationToken)
    {
        var previous = string.Empty;

        while (!cancellationToken.IsCancellationRequested)
        {
            var current = string.Join(", ", system.Cluster.Members.Select(m => $"{m.NodeId}:{m.Status}"));
            if (current != previous)
            {
                previous = current;
                AnsiConsole.MarkupLine($"[{Theme.Muted}]{DateTime.Now:HH:mm:ss}[/] cluster: {current.Safe()}");
            }

            try { await Task.Delay(500, cancellationToken); }
            catch (OperationCanceledException) { return; }
        }
    }
}

public sealed class ClusterSettings : NodeSettings
{
    [CommandOption("--watch")]
    [System.ComponentModel.Description("Keep printing the member table as it changes.")]
    public bool Watch { get; init; }
}

/// <summary>Joins a cluster and reports what it can see.</summary>
public sealed class ClusterCommand : AsyncCommand<ClusterSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, ClusterSettings settings, CancellationToken cancellationToken)
    {
        Theme.Banner();

        if (settings.Seeds.Length == 0)
        {
            Theme.Fail("No seeds given. Pass --seed host:port at least once, pointing at a node that is already up.");
            return 1;
        }

        await using var system = await NodeFactory.StartAsync(settings);
        NodeFactory.Describe(system);

        Theme.Info("Waiting for the join handshake to converge...");
        var converged = await WaitForPeersAsync(system, TimeSpan.FromSeconds(15));

        if (!converged)
            Theme.Caution("No peers answered. Check that a seed node is running and reachable.");

        Render(system);

        if (!settings.Watch) return converged ? 0 : 1;

        using var stopping = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            stopping.Cancel();
        };

        AnsiConsole.WriteLine();
        Theme.Info("Watching. Press Ctrl+C to stop.");

        while (!stopping.IsCancellationRequested)
        {
            try { await Task.Delay(2000, stopping.Token); }
            catch (OperationCanceledException) { break; }

            AnsiConsole.WriteLine();
            Render(system);
        }

        return 0;
    }

    private static async Task<bool> WaitForPeersAsync(ActorSystem system, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (system.Cluster.Members.Count > 1) return true;
            await Task.Delay(200);
        }

        return system.Cluster.Members.Count > 1;
    }

    private static void Render(ActorSystem system)
    {
        var table = Theme.Grid("Node", "Address", "Status", "Last seen", "Incarnation");

        foreach (var member in system.Cluster.Members)
        {
            var status = member.Status switch
            {
                MemberStatus.Up => $"[{Theme.Good}]{member.Status}[/]",
                MemberStatus.Unreachable => $"[{Theme.Warn}]{member.Status}[/]",
                MemberStatus.Down => $"[{Theme.Bad}]{member.Status}[/]",
                _ => $"[{Theme.Muted}]{member.Status}[/]",
            };

            var self = member.NodeId == system.NodeId ? $"  [{Theme.Muted}](this node)[/]" : string.Empty;

            table.AddRow(
                member.NodeId.Safe() + self,
                member.Address.Safe(),
                status,
                $"[{Theme.Muted}]{(DateTimeOffset.UtcNow - member.LastSeen).TotalSeconds:N1}s ago[/]",
                $"[{Theme.Muted}]{member.Incarnation}[/]");
        }

        AnsiConsole.Write(table);

        // Placement is the thing a cluster demo actually needs to show: the same key maps to the
        // same owner on every node, and moving a node moves a slice of the keyspace.
        var sample = Theme.Grid("Sample key", "Owner", "Local here");
        foreach (var i in Enumerable.Range(1, 6))
        {
            var id = new ActorId("BankAccountActor", $"acct-{i:D3}");
            var owner = system.Cluster.OwnerOf(id);
            sample.AddRow(
                id.ToString().Safe(),
                owner.Safe(),
                system.Cluster.IsLocal(id) ? $"[{Theme.Good}]yes[/]" : $"[{Theme.Muted}]no[/]");
        }

        AnsiConsole.WriteLine();
        AnsiConsole.Write(sample);
    }
}

/// <summary>Prints the demo scenarios and what each one demonstrates.</summary>
public sealed class ScenariosCommand : Command
{
    protected override int Execute(CommandContext context, CancellationToken cancellationToken)
    {
        Theme.Banner();
        Theme.Rule("Scenarios");

        foreach (var scenario in DemoCatalog.Scenarios)
        {
            AnsiConsole.MarkupLine($"[{Theme.Accent}]{scenario.Name.Safe()}[/]");
            AnsiConsole.MarkupLine($"  {scenario.Summary.Safe()}");
            AnsiConsole.MarkupLine($"  [{Theme.Muted}]{scenario.WhyItMatters.Safe()}[/]");
            AnsiConsole.WriteLine();
        }

        AnsiConsole.MarkupLine($"[{Theme.Muted}]Run one with[/] [{Theme.Accent}]actornet demo <name>[/][{Theme.Muted}], or omit the name for a menu.[/]");
        return 0;
    }
}
