using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ScrapyNet.Platform;
using ScrapyNet.Sandbox;
using ScrapyNet.Server;
using Xunit;

namespace ScrapyNet.Tests;

public class ServerTests(SandboxFixture sandbox)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    private static async Task<(WebApplication App, HttpClient Admin, HttpClient Viewer, HttpClient Anonymous)> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddScrapyNetPlatform(o =>
        {
            o.AddApiKey("admin-key", "admin", PlatformRole.Admin).AddApiKey("viewer-key", "dashboard", PlatformRole.Viewer);
            o.BaseSettings = () =>
            {
                var s = new Settings();
                TestSettings.Quiet(s);
                return s;
            };
            o.DataDirectory = TestSettings.TempDir();
        });
        var app = builder.Build();
        app.MapScrapyNetApi();
        await app.StartAsync(TestContext.Current.CancellationToken);
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        HttpClient Client(string? key)
        {
            var c = new HttpClient { BaseAddress = new Uri(address) };
            if (key is not null) c.DefaultRequestHeaders.Add("X-Api-Key", key);
            return c;
        }
        return (app, Client("admin-key"), Client("viewer-key"), Client(null));
    }

    [Fact]
    public async Task Api_manages_spiders_and_jobs_with_roles()
    {
        var (app, admin, viewer, anonymous) = await StartAsync();
        await using var _ = app;
        var ct = TestContext.Current.CancellationToken;

        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/api/health", ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/spiders", ct)).StatusCode);

        var definition = new SpiderDefinition
        {
            Id = "quotes",
            Name = "quotes",
            Target = new TargetConfig { StartUrls = [sandbox.Url("/quotes/")] },
            Rules = new ScrapingRules
            {
                ItemSelector = "div.quote",
                Fields = new() { ["text"] = new FieldRule { Css = "span.text::text" }, ["author"] = new FieldRule { Css = "small.author::text" } },
                FollowLinks = ["li.next a"],
            },
        };
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync("/api/spiders", definition, Json, ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync("/api/spiders", definition, Json, ct)).StatusCode);

        var preview = await admin.PostAsJsonAsync("/api/preview", new { url = sandbox.Url("/quotes/"), rules = definition.Rules }, Json, ct);
        var previewItems = (await preview.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("items");
        Assert.Equal(10, previewItems.GetArrayLength());

        var run = await admin.PostAsJsonAsync("/api/spiders/quotes/run", new { }, Json, ct);
        Assert.Equal(HttpStatusCode.Accepted, run.StatusCode);
        var jobId = (await run.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetString()!;

        JsonElement job = default;
        for (var i = 0; i < 100; i++)
        {
            job = await viewer.GetFromJsonAsync<JsonElement>($"/api/jobs/{jobId}", ct);
            if (job.GetProperty("status").GetString() is "Completed" or "Failed") break;
            await Task.Delay(100, ct);
        }
        Assert.Equal("Completed", job.GetProperty("status").GetString());
        Assert.Equal(SandboxData.Quotes.Count, job.GetProperty("itemCount").GetInt32());

        var items = await viewer.GetFromJsonAsync<JsonElement>($"/api/jobs/{jobId}/items?limit=5", ct);
        Assert.Equal(5, items.GetArrayLength());
        var dashboard = await viewer.GetFromJsonAsync<JsonElement>("/api/dashboard", ct);
        Assert.Equal(1, dashboard.GetProperty("completedJobs").GetInt32());

        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync("/api/schedules", new { id = "nightly", spiderId = "quotes", cron = "0 2 * * *" }, Json, ct)).StatusCode);
        var schedules = await viewer.GetFromJsonAsync<JsonElement>("/api/schedules", ct);
        Assert.Equal("nightly", schedules[0].GetProperty("id").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/schedules", new { id = "bad", spiderId = "quotes", cron = "nope" }, Json, ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync("/api/jobs/unknown", ct)).StatusCode);
    }
}
