// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using ActorNet.Cluster;
using ActorNet.Metrics;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ActorNet.Cli.Commands;

/// <summary>Settings for the live monitor.</summary>
public sealed class MonitorSettings : NodeSettings
{
    [CommandOption("--refresh <MILLISECONDS>")]
    [System.ComponentModel.Description("How often to redraw. Default 500.")]
    public int RefreshMilliseconds { get; init; } = 500;

    [CommandOption("--top <COUNT>")]
    [System.ComponentModel.Description("How many of the busiest actors to list. Default 12.")]
    public int Top { get; init; } = 12;

    [CommandOption("--load")]
    [System.ComponentModel.Description("Generate synthetic traffic, so the monitor has something to show.")]
    public bool GenerateLoad { get; init; }
}

/// <summary>
/// A live view of a running node.
/// </summary>
/// <remarks>
/// Redraws a fixed layout in place rather than scrolling. A monitor that scrolls is unreadable at
/// anything above a few messages a second, which is well below what this framework does.
/// </remarks>
public sealed class MonitorCommand : AsyncCommand<MonitorSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, MonitorSettings settings, CancellationToken cancellationToken)
    {
        Theme.Banner();

        await using var system = await NodeFactory.StartAsync(settings);
        NodeFactory.Describe(system);

        using var stopping = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            stopping.Cancel();
        };

        var load = settings.GenerateLoad ? LoadGenerator.Run(system, stopping.Token) : Task.CompletedTask;

        AnsiConsole.MarkupLine($"[{Theme.Muted}]Press Ctrl+C to stop.[/]");
        AnsiConsole.WriteLine();

        // The member table only earns its width when there is a cluster to show. On a standalone
        // node it would be one row saying what the header already says.
        var clustered = system.Options.Cluster.Enabled;

        var body = new Layout("body");
        if (clustered)
            body.SplitColumns(new Layout("actors"), new Layout("members").Size(66));
        else
            body.SplitColumns(new Layout("actors"));

        var layout = new Layout("root")
            .SplitRows(new Layout("summary").Size(9), body);

        try
        {
            await AnsiConsole.Live(layout)
                .AutoClear(false)
                .StartAsync(async live =>
                {
                    while (!stopping.IsCancellationRequested)
                    {
                        var snapshot = system.Metrics.Snapshot();
                        layout["summary"].Update(SummaryPanel(system, snapshot));
                        layout["actors"].Update(ActorsPanel(snapshot, settings.Top));
                        if (clustered) layout["members"].Update(MembersPanel(system));
                        live.Refresh();

                        try { await Task.Delay(Math.Max(100, settings.RefreshMilliseconds), stopping.Token); }
                        catch (OperationCanceledException) { break; }
                    }
                });
        }
        finally
        {
            await stopping.CancelAsync();
            try { await load; } catch (OperationCanceledException) { /* expected */ }
        }

        AnsiConsole.WriteLine();
        Theme.Info("Stopping the node.");
        return 0;
    }

    private static Panel SummaryPanel(ActorSystem system, ActorSystemSnapshot snapshot)
    {
        var left = new Grid().AddColumn().AddColumn();
        left.AddRow($"[{Theme.Muted}]Uptime[/]", $"{snapshot.Uptime:hh\\:mm\\:ss}");
        left.AddRow($"[{Theme.Muted}]Processed[/]", $"[{Theme.Text}]{snapshot.MessagesProcessed:N0}[/]");
        left.AddRow($"[{Theme.Muted}]Throughput[/]", $"[{Theme.Accent}]{snapshot.MessagesPerSecond:N0}[/] [{Theme.Muted}]msg/s[/]");
        left.AddRow($"[{Theme.Muted}]In flight[/]", Emphasise(snapshot.InFlight, warnAbove: 1000));

        var middle = new Grid().AddColumn().AddColumn();
        middle.AddRow($"[{Theme.Muted}]Active actors[/]", $"[{Theme.Text}]{snapshot.ActiveActors:N0}[/]");
        middle.AddRow($"[{Theme.Muted}]Activations[/]", $"{snapshot.Activations:N0}");
        middle.AddRow($"[{Theme.Muted}]Deactivations[/]", $"{snapshot.Deactivations:N0}");
        middle.AddRow($"[{Theme.Muted}]Restarts[/]", Emphasise(snapshot.Restarts, warnAbove: 0));

        var right = new Grid().AddColumn().AddColumn();
        right.AddRow($"[{Theme.Muted}]Failures[/]", Emphasise(snapshot.MessagesFailed, warnAbove: 0));
        right.AddRow($"[{Theme.Muted}]Ask timeouts[/]", Emphasise(snapshot.AsksTimedOut, warnAbove: 0));
        right.AddRow($"[{Theme.Muted}]Handling[/]", $"{snapshot.AverageProcessingMicroseconds:N1} [{Theme.Muted}]us[/]");
        right.AddRow($"[{Theme.Muted}]Queue wait[/]", $"{snapshot.AverageQueueLatencyMicroseconds:N1} [{Theme.Muted}]us[/]");

        var columns = new Grid().AddColumn().AddColumn().AddColumn();
        columns.AddRow(left, middle, right);

        var members = system.Cluster.Members.Count;

        // Only the node id is escaped: the rest of the header is markup this method wrote, and
        // escaping the whole string printed the colour tags instead of applying them.
        var name = system.NodeId.Safe();
        var header = system.Options.Cluster.Enabled
            ? $"{name} [{Theme.Muted}]|[/] {members} member(s)"
            : $"{name} [{Theme.Muted}]| standalone[/]";

        return new Panel(columns)
            .Header($"[{Theme.Accent}]{header}[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.FromHex(Theme.Muted))
            .Expand();
    }

    /// <summary>
    /// This node's view of the membership.
    /// </summary>
    /// <remarks>
    /// Every node keeps its own table, so what is shown here is one opinion rather than the truth -
    /// a peer that reads Unreachable here may be perfectly healthy from somewhere else.
    /// </remarks>
    private static Panel MembersPanel(ActorSystem system)
    {
        var table = Theme.Grid("Node", "Address", "Status");

        foreach (var member in system.Cluster.Members.OrderBy(m => m.NodeId, StringComparer.Ordinal))
        {
            var self = member.NodeId == system.NodeId ? $" [{Theme.Muted}](this node)[/]" : string.Empty;
            table.AddRow(
                $"[{Theme.Text}]{member.NodeId.Safe()}[/]{self}",
                $"[{Theme.Muted}]{member.Address.Safe()}[/]",
                Status(member.Status));
        }

        return new Panel(table)
            .Header($"[{Theme.Accent}]Cluster[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.FromHex(Theme.Muted))
            .Expand();
    }

    private static string Status(MemberStatus status) => status switch
    {
        MemberStatus.Up => $"[{Theme.Good}]Up[/]",
        MemberStatus.Unreachable => $"[{Theme.Warn}]Unreachable[/]",
        MemberStatus.Down => $"[{Theme.Bad}]Down[/]",
        _ => $"[{Theme.Muted}]{status}[/]",
    };

    private static string Emphasise(long value, long warnAbove) =>
        value > warnAbove ? $"[{Theme.Warn}]{value:N0}[/]" : $"[{Theme.Muted}]{value:N0}[/]";

    private static Panel ActorsPanel(ActorSystemSnapshot snapshot, int top)
    {
        var table = Theme.Grid("Actor", "Processed", "Failed", "Mailbox", "Avg us", "Idle");

        // Busiest first: on a node with thousands of actors, the interesting ones are the ones
        // doing work, not the alphabetically first ones.
        foreach (var actor in snapshot.Actors.OrderByDescending(a => a.MessagesProcessed).Take(Math.Max(1, top)))
        {
            table.AddRow(
                actor.Id.Safe(),
                $"{actor.MessagesProcessed:N0}",
                actor.MessagesFailed > 0 ? $"[{Theme.Bad}]{actor.MessagesFailed:N0}[/]" : $"[{Theme.Muted}]0[/]",
                actor.MailboxDepth > 0 ? $"[{Theme.Warn}]{actor.MailboxDepth:N0}[/]" : $"[{Theme.Muted}]0[/]",
                $"{actor.AverageProcessingMicroseconds:N1}",
                $"[{Theme.Muted}]{actor.Idle.TotalSeconds:N0}s[/]");
        }

        if (snapshot.Actors.Count == 0)
            table.AddRow($"[{Theme.Muted}]no actors are activated yet[/]", "", "", "", "", "");

        var hidden = Math.Max(0, snapshot.Actors.Count - top);
        var header = hidden > 0
            ? $"Busiest actors [{Theme.Muted}]({hidden:N0} more not shown)[/]"
            : "Actors";

        return new Panel(table)
            .Header($"[{Theme.Accent}]{header}[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.FromHex(Theme.Muted))
            .Expand();
    }
}
