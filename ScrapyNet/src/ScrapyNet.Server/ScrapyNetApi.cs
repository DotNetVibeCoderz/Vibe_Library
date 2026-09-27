using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using ScrapyNet.Cmdline;
using ScrapyNet.Platform;

namespace ScrapyNet.Server;

/// <summary>Access levels: viewers read, operators run and schedule, admins change spiders and integrations.</summary>
public enum PlatformRole
{
    Viewer = 0,
    Operator = 1,
    Admin = 2,
}

/// <summary>An API client: name and role, identified by its key.</summary>
public sealed record ApiClient(string Name, PlatformRole Role);

public sealed class ScrapyNetServerOptions
{
    /// <summary>API key → client. Keys are sent as <c>X-Api-Key</c> or <c>Authorization: Bearer</c>.</summary>
    public Dictionary<string, ApiClient> ApiKeys { get; } = new(StringComparer.Ordinal);

    /// <summary>Let requests without a key read (Viewer role). Off by default.</summary>
    public bool AllowAnonymousRead { get; set; }

    /// <summary>Where the catalog, schedules, job history and scraped data live. <c>null</c> = in memory only.</summary>
    public string? DataDirectory { get; set; }

    public int MaxConcurrentJobs { get; set; } = 4;

    /// <summary>Base settings for every job.</summary>
    public Func<Settings>? BaseSettings { get; set; }

    public SecretStore? Secrets { get; set; }

    public ScrapyNetServerOptions AddApiKey(string key, string name, PlatformRole role)
    {
        ApiKeys[key] = new ApiClient(name, role);
        return this;
    }
}

/// <summary>Registration and endpoint mapping for the Scrapy.Net platform API.</summary>
public static class ScrapyNetApi
{
    /// <summary>Registers the catalog, job manager, scheduler, monitor and notifications as singletons.</summary>
    public static IServiceCollection AddScrapyNetPlatform(this IServiceCollection services, Action<ScrapyNetServerOptions>? configure = null)
    {
        var options = new ScrapyNetServerOptions();
        configure?.Invoke(options);
        string? Data(string file) => options.DataDirectory is null ? null : Path.Combine(options.DataDirectory, file);

        services.AddSingleton(options);
        services.AddSingleton(_ => new SpiderCatalog(Data("spiders.json")));
        services.AddSingleton(sp => new JobManager(sp.GetRequiredService<SpiderCatalog>(), new JobManagerOptions
        {
            MaxConcurrentJobs = options.MaxConcurrentJobs,
            BaseSettings = options.BaseSettings,
            HistoryPath = Data("jobs.jsonl"),
            DataDirectory = Data("items"),
            Secrets = options.Secrets,
        }));
        services.AddSingleton(sp =>
        {
            var scheduler = new CrawlScheduler(sp.GetRequiredService<JobManager>(), Data("schedules.json"));
            scheduler.Start();
            return scheduler;
        });
        services.AddSingleton(sp => new CrawlMonitor(sp.GetRequiredService<JobManager>()));
        services.AddSingleton(sp => new NotificationDispatcher(sp.GetRequiredService<JobManager>()));
        services.ConfigureHttpJsonOptions(o =>
        {
            o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
            o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        });
        return services;
    }

    /// <summary>Maps the REST API under <paramref name="prefix"/> (default <c>/api</c>).</summary>
    public static RouteGroupBuilder MapScrapyNetApi(this IEndpointRouteBuilder app, string prefix = "/api")
    {
        // Resolve the singletons now so the scheduler and monitor start with the app.
        _ = app.ServiceProvider.GetRequiredService<CrawlScheduler>();
        _ = app.ServiceProvider.GetRequiredService<CrawlMonitor>();
        _ = app.ServiceProvider.GetRequiredService<NotificationDispatcher>();

        var api = app.MapGroup(prefix);
        api.MapGet("/health", () => Results.Ok(new { status = "ok", product = "Scrapy.Net", version = Templates.PackageVersion }));

        // ---- spiders ------------------------------------------------------------------------------
        var viewer = api.MapGroup("").AddEndpointFilter(RequireRole(PlatformRole.Viewer));
        var operatorGroup = api.MapGroup("").AddEndpointFilter(RequireRole(PlatformRole.Operator));
        var admin = api.MapGroup("").AddEndpointFilter(RequireRole(PlatformRole.Admin));

        viewer.MapGet("/spiders", (SpiderCatalog c) => c.List());
        viewer.MapGet("/spiders/{id}", (string id, SpiderCatalog c) => c.TryGet(id, out var d) ? Results.Ok(d) : Results.NotFound());
        viewer.MapGet("/spiders/{id}/versions", (string id, SpiderCatalog c) => Try(() => c.Versions(id)));
        admin.MapPost("/spiders", (SpiderDefinition definition, SpiderCatalog c) => Try(() => Results.Created($"{prefix}/spiders/{definition.Id}", c.Create(definition))));
        admin.MapPut("/spiders/{id}", (string id, SpiderDefinition definition, SpiderCatalog c) => Try(() => c.Update(id, _ => definition)));
        admin.MapDelete("/spiders/{id}", (string id, SpiderCatalog c) => c.Delete(id) ? Results.NoContent() : Results.NotFound());
        admin.MapPost("/spiders/{id}/clone", (string id, CloneRequest body, SpiderCatalog c) => Try(() => c.Clone(id, body.NewId, body.NewName)));
        admin.MapPost("/spiders/{id}/rollback/{version:int}", (string id, int version, SpiderCatalog c) => Try(() => c.Rollback(id, version)));
        operatorGroup.MapPost("/spiders/{id}/enable", (string id, SpiderCatalog c) => Try(() => c.Enable(id)));
        operatorGroup.MapPost("/spiders/{id}/disable", (string id, SpiderCatalog c) => Try(() => c.Disable(id)));
        operatorGroup.MapPost("/spiders/{id}/run", (string id, RunRequest? body, JobManager jobs) =>
            Try(() => Results.Accepted(null, jobs.RunNow(id, body?.Arguments, body?.Settings?.ToDictionary(kv => kv.Key, kv => (object?)kv.Value), "api"))));

        // Selector builder: try rules against a live page before saving them.
        operatorGroup.MapPost("/preview", async (PreviewRequest body, CancellationToken ct) =>
        {
            var response = await Fetcher.FetchAsync(body.Url, cancellationToken: ct).ConfigureAwait(false);
            return Results.Ok(new { status = response.Status, items = RuleEvaluator.Extract(body.Rules, response.Selector, response.Url) });
        });

        // ---- jobs ---------------------------------------------------------------------------------
        viewer.MapGet("/jobs", (string? spider, int? limit, JobManager jobs) => jobs.History(spider, limit ?? 100));
        viewer.MapGet("/jobs/running", (JobManager jobs) => jobs.Running);
        viewer.MapGet("/jobs/{id}", (string id, JobManager jobs) => Try(() => jobs.Get(id)));
        viewer.MapGet("/jobs/{id}/items", (string id, int? limit, JobManager jobs) =>
        {
            var job = jobs.Get(id);
            if (job.OutputPath is null || !File.Exists(job.OutputPath)) return Results.Ok(Array.Empty<object>());
            var items = File.ReadLines(job.OutputPath).Take(limit ?? 100).Select(l => JsonDocument.Parse(l).RootElement.Clone()).ToList();
            return Results.Ok(items);
        });
        viewer.MapGet("/jobs/{id}/metrics", (string id, CrawlMonitor monitor) => monitor.Get(id) is { } m ? Results.Ok(ToDto(m)) : Results.NotFound());
        operatorGroup.MapPost("/jobs/{id}/stop", async (string id, JobManager jobs) => { await jobs.StopAsync(id).ConfigureAwait(false); return Results.Accepted(); });
        operatorGroup.MapPost("/jobs/{id}/cancel", (string id, JobManager jobs) => { jobs.Cancel(id); return Results.Accepted(); });
        operatorGroup.MapPost("/jobs/{id}/pause", (string id, JobManager jobs) => Try(() => { jobs.Pause(id); return Results.Accepted(); }));
        operatorGroup.MapPost("/jobs/{id}/resume", (string id, JobManager jobs) => Try(() => { jobs.Resume(id); return Results.Accepted(); }));
        operatorGroup.MapPost("/jobs/{id}/retry", (string id, JobManager jobs) => Try(() => Results.Accepted(null, jobs.Retry(id))));

        // ---- schedules ----------------------------------------------------------------------------
        viewer.MapGet("/schedules", (CrawlScheduler s) => s.List());
        operatorGroup.MapPost("/schedules", (ScheduleEntry entry, CrawlScheduler s) => Try(() => Results.Created($"{prefix}/schedules/{entry.Id}", s.Add(entry))));
        operatorGroup.MapDelete("/schedules/{id}", (string id, CrawlScheduler s) => s.Remove(id) ? Results.NoContent() : Results.NotFound());
        operatorGroup.MapPost("/schedules/{id}/enable", (string id, CrawlScheduler s) => Try(() => s.SetEnabled(id, true)));
        operatorGroup.MapPost("/schedules/{id}/disable", (string id, CrawlScheduler s) => Try(() => s.SetEnabled(id, false)));

        // ---- monitoring ---------------------------------------------------------------------------
        viewer.MapGet("/dashboard", (CrawlMonitor monitor) =>
        {
            var snap = monitor.Snapshot();
            return new
            {
                snap.Time,
                running = snap.Running.Select(r => new { job = r.Job, metrics = ToDto(r.Metrics) }),
                snap.CompletedJobs,
                snap.FailedJobs,
                snap.ItemsLast24Hours,
                snap.RequestRatePerMinute,
                snap.ErrorRate,
                snap.Spiders,
            };
        });
        viewer.MapGet("/alerts", (NotificationDispatcher n) => n.Alerts.OrderByDescending(a => a.Time));
        admin.MapPost("/webhooks", (WebhookRequest body, NotificationDispatcher n) =>
        {
            INotifier notifier = body.Kind?.ToLowerInvariant() switch
            {
                "slack" => new SlackNotifier(body.Url),
                "teams" => new TeamsNotifier(body.Url),
                _ => new WebhookNotifier(body.Url),
            };
            n.Route(notifier, body.MinSeverity ?? AlertSeverity.Warning);
            return Results.Created($"{prefix}/webhooks", new { body.Url, kind = body.Kind ?? "webhook", minSeverity = body.MinSeverity ?? AlertSeverity.Warning });
        });

        return api;
    }

    public sealed record RunRequest(Dictionary<string, string>? Arguments, Dictionary<string, string>? Settings);

    public sealed record CloneRequest(string NewId, string? NewName);

    public sealed record PreviewRequest(string Url, ScrapingRules Rules);

    public sealed record WebhookRequest(string Url, string? Kind, AlertSeverity? MinSeverity);

    private static object ToDto(LiveMetrics m) => new
    {
        m.JobId,
        m.SpiderName,
        m.StartedAt,
        m.Requests,
        m.Responses,
        m.Items,
        m.DroppedItems,
        m.Errors,
        m.BytesDownloaded,
        m.ResponsesPerMinute,
        m.ErrorRate,
        statusCounts = m.StatusCounts.ToDictionary(kv => kv.Key.ToString(System.Globalization.CultureInfo.InvariantCulture), kv => kv.Value),
        series = m.Series.TakeLast(300),
    };

    private static IResult Try(Func<object?> action)
    {
        try
        {
            var result = action();
            return result as IResult ?? Results.Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return Results.NotFound(new { error = ex.Message });
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or FormatException)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>Endpoint filter: resolves the API key to a client and checks its role.</summary>
    private static Func<EndpointFilterInvocationContext, EndpointFilterDelegate, ValueTask<object?>> RequireRole(PlatformRole role) =>
        async (context, next) =>
        {
            var http = context.HttpContext;
            var options = http.RequestServices.GetRequiredService<ScrapyNetServerOptions>();
            var key = http.Request.Headers["X-Api-Key"].FirstOrDefault();
            var auth = http.Request.Headers.Authorization.FirstOrDefault();
            if (key is null && auth is not null && auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) key = auth[7..].Trim();

            ApiClient? client = null;
            if (key is not null) options.ApiKeys.TryGetValue(key, out client);
            if (client is null && key is null && options.AllowAnonymousRead) client = new ApiClient("anonymous", PlatformRole.Viewer);
            if (client is null) return Results.Unauthorized();
            if (client.Role < role) return Results.Json(new { error = $"{role} role required" }, statusCode: StatusCodes.Status403Forbidden);
            http.Items["scrapynet.client"] = client;
            return await next(context).ConfigureAwait(false);
        };
}
