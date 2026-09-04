// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.
//
// The ActorNet console. This process is itself a node - it hosts an actor system rather than
// scraping one over an API, which is why the numbers it shows are the runtime's own counters and
// not a sampled approximation of them.
//
// Run it:
//   dotnet run --project src/ActorNet.Dashboard
//   dotnet run --project src/ActorNet.Dashboard -- --ActorNet:Port=9100 --ActorNet:Seeds:0=127.0.0.1:9000

using ActorNet;
using ActorNet.Dashboard;
using ActorNet.Dashboard.Components;
using ActorNet.Demo;
using ActorNet.Hosting;

var builder = WebApplication.CreateBuilder(args);

var settings = builder.Configuration.GetSection("ActorNet").Get<DashboardOptions>() ?? new DashboardOptions();

builder.Services.AddRazorComponents().AddInteractiveServerComponents();

builder.Services.AddActorNet(actors =>
{
    actors.Options.NodeId = settings.NodeId ?? $"console-{Environment.MachineName.ToLowerInvariant()}";
    actors.Options.Host = settings.Host;
    actors.Options.Port = settings.Port;
    actors.Options.IdleTimeout = TimeSpan.FromSeconds(settings.IdleTimeoutSeconds);
    actors.Options.SweepInterval = TimeSpan.FromSeconds(Math.Clamp(settings.IdleTimeoutSeconds / 4.0, 1, 30));

    // Seeds, or an explicit Cluster flag for the node others join - which has no seeds of its own.
    if (settings.Seeds.Count > 0 || settings.Cluster)
    {
        actors.Options.Cluster.Enabled = true;
        actors.Options.Cluster.Seeds = settings.Seeds;
    }

    actors.AddDemoDomain();
});

// Traffic is opt-in. A console that manufactures its own load is a demo; one that shows a quiet
// node honestly is a tool. The switch exists so the demo is available without being the default.
if (settings.GenerateLoad) builder.Services.AddHostedService<LoadGeneratorService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseAntiforgery();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

// A small read-only API alongside the UI, so the same numbers are available to a scrape or a
// script without screen-scraping the console.
app.MapGet("/api/metrics", (ActorSystem system) => Results.Ok(system.Metrics.Snapshot()));
app.MapGet("/api/cluster", (ActorSystem system) => Results.Ok(new
{
    system.NodeId,
    Members = system.Cluster.Members,
    Ownership = system.Cluster.Ring.OwnershipShare(),
}));

app.Run();

namespace ActorNet.Dashboard
{
    /// <summary>Console settings, bound from the <c>ActorNet</c> configuration section.</summary>
    public sealed class DashboardOptions
    {
        /// <summary>This node's cluster identity. Defaults to a name derived from the machine.</summary>
        public string? NodeId { get; set; }

        /// <summary>Address the actor transport binds to. The web server's port is separate.</summary>
        public string Host { get; set; } = "127.0.0.1";

        /// <summary>Port the actor transport binds to.</summary>
        public int Port { get; set; } = 9100;

        /// <summary>Cluster seeds as <c>host:port</c>. Any seed turns clustering on.</summary>
        public List<string> Seeds { get; set; } = [];

        /// <summary>Join a cluster with no seeds - what the first node of a cluster needs.</summary>
        public bool Cluster { get; set; }

        /// <summary>How long an actor may idle before the sweeper deactivates it.</summary>
        public int IdleTimeoutSeconds { get; set; } = 300;

        /// <summary>Generate synthetic traffic, so the console has something to show.</summary>
        public bool GenerateLoad { get; set; }
    }
}
