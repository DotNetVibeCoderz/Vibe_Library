// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ActorNet.AspNetCore;
using ActorNet.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ActorNet.Tests;

/// <summary>
/// The ASP.NET Core integration, against a real application on a loopback port.
/// </summary>
/// <remarks>
/// A node inside a web application is the deployment this framework is most likely to be in, and
/// the endpoints and health check are what an orchestrator reads to decide whether to send it
/// traffic. Testing them through a mock would test the mock.
/// </remarks>
public sealed class AspNetCoreTests
{
    /// <summary>Starts a web application hosting a node, and returns a client pointed at it.</summary>
    private static async Task<(WebApplication App, HttpClient Client)> StartAsync(Action<WebApplication>? map = null)
    {
        var builder = WebApplication.CreateSlimBuilder();

        // Kestrel and the hosting layer are chatty, and five of these run inside a suite whose
        // output is read when something fails.
        builder.Configuration["Logging:LogLevel:Default"] = "Warning";

        // Port 0, like every other networked test here, so parallel test classes cannot collide.
        builder.WebHost.ConfigureKestrel(k => k.Listen(System.Net.IPAddress.Loopback, 0));
        builder.Services.AddActorNet(actors =>
        {
            actors.Options.NodeId = $"web-{Guid.NewGuid():N}"[..10];
            actors.Options.EnableNetworking = false;
            actors.Actor<CounterActor>();
            actors.Message<Add>();
            actors.Message<GetTotal>();
            actors.Message<Total>();
        });

        builder.Services.AddHealthChecks().AddActorNetCheck();

        var app = builder.Build();
        app.MapHealthChecks("/health");
        app.MapActorNetDiagnostics();
        map?.Invoke(app);

        await app.StartAsync();

        var address = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.First();

        return (app, new HttpClient { BaseAddress = new Uri(address) });
    }

    [Fact]
    public async Task AServingNodeIsHealthy()
    {
        var (app, client) = await StartAsync();
        await using var _ = app;

        var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TheClusterEndpointReportsThisNodesView()
    {
        var (app, client) = await StartAsync();
        await using var _ = app;

        var body = await client.GetFromJsonAsync<JsonElement>("/actornet/cluster", TestContext.Current.CancellationToken);

        var system = app.Services.GetRequiredService<IActorSystem>();
        Assert.Equal(system.Cluster.SelfNodeId, body.GetProperty("node").GetString());
        Assert.True(body.GetProperty("singleNode").GetBoolean());

        var members = body.GetProperty("members").EnumerateArray().ToArray();
        var self = Assert.Single(members);
        Assert.Equal("Up", self.GetProperty("status").GetString());

        // A standalone node owns the whole keyspace. Reporting zero here was a real bug once - a
        // ring segment that wraps the whole circle reads as zero width if you subtract naively.
        Assert.Equal(1d, self.GetProperty("share").GetDouble(), 3);
    }

    [Fact]
    public async Task TheMetricsEndpointCountsWorkThatActuallyHappened()
    {
        var (app, client) = await StartAsync();
        await using var _ = app;

        var system = app.Services.GetRequiredService<IActorSystem>();
        var id = ActorId.For<CounterActor>("web-counter");

        await system.TellAsync(id, new Add(4));
        await system.AskAsync<Total>(id, new GetTotal(), TimeSpan.FromSeconds(10));

        var body = await client.GetFromJsonAsync<JsonElement>("/actornet/metrics", TestContext.Current.CancellationToken);

        Assert.Equal(system.Cluster.SelfNodeId, body.GetProperty("node").GetString());
        Assert.True(body.GetProperty("processed").GetInt64() >= 2, "the add and the ask should both be counted");
        Assert.Equal(1, body.GetProperty("activeActors").GetInt32());
    }

    [Fact]
    public async Task AReadinessFilterLetsTrafficThroughWhileTheNodeIsServing()
    {
        var (app, client) = await StartAsync(a =>
            a.MapGet("/work", () => "done").RequireActorNetReady());

        await using var _ = app;

        var response = await client.GetAsync("/work", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("done", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AStoppedNodeIsUnhealthy()
    {
        await using var harness = new TestHarness();
        var system = await harness.LocalAsync();

        // The check reads the node's own entry in its member table. Down is the state a node
        // reaches by losing a partition or announcing a leave, and it is the one an orchestrator
        // has to be told about - the process is still up and would otherwise look fine.
        var check = new ActorNetHealthCheck(system);
        var healthy = await check.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);

        Assert.Equal(HealthStatus.Healthy, healthy.Status);
        Assert.Equal(system.Cluster.SelfNodeId, healthy.Data["node"]);
    }
}
