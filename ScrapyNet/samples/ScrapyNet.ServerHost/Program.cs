using ScrapyNet;
using ScrapyNet.Platform;
using ScrapyNet.Sandbox;
using ScrapyNet.Server;

// A scrapyd-style crawler service: REST API over the Scrapy.Net platform.
//
//   dotnet run --project samples/ScrapyNet.ServerHost
//   curl -H "X-Api-Key: dev-admin" http://localhost:5080/api/spiders
//   curl -X POST -H "X-Api-Key: dev-admin" http://localhost:5080/api/spiders/sandbox-quotes/run
//   curl -H "X-Api-Key: dev-viewer" http://localhost:5080/api/dashboard
//
// API keys come from configuration (ScrapyNet:ApiKeys:<key> = "<name>:<Role>"); the two dev keys
// below exist only in Development.

var sandbox = await SandboxSite.StartAsync();   // demo target, so the service works offline
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(builder.Configuration["Urls"] ?? "http://localhost:5080");

builder.Services.AddScrapyNetPlatform(options =>
{
    options.DataDirectory = Path.Combine(builder.Environment.ContentRootPath, "platform-data");
    options.MaxConcurrentJobs = 2;
    foreach (var entry in builder.Configuration.GetSection("ScrapyNet:ApiKeys").GetChildren())
    {
        var parts = entry.Value!.Split(':');
        options.AddApiKey(entry.Key, parts[0], Enum.Parse<PlatformRole>(parts[1]));
    }
    if (builder.Environment.IsDevelopment())
        options.AddApiKey("dev-admin", "developer", PlatformRole.Admin).AddApiKey("dev-viewer", "dashboard", PlatformRole.Viewer);
});

var app = builder.Build();
app.MapScrapyNetApi();
app.MapGet("/", () => Results.Text("Scrapy.Net server — see /api/health. Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil."));

// Seed one declarative spider targeting the sandbox so there is something to run.
var catalog = app.Services.GetRequiredService<SpiderCatalog>();
if (!catalog.TryGet("sandbox-quotes", out _))
{
    catalog.Create(new SpiderDefinition
    {
        Id = "sandbox-quotes",
        Name = "sandbox-quotes",
        Description = "Quotes from the local practice site",
        Target = new TargetConfig { StartUrls = [sandbox.Url("/quotes/")] },
        Rules = new ScrapingRules
        {
            ItemSelector = "div.quote",
            Fields = new() { ["text"] = new FieldRule { Css = "span.text::text" }, ["author"] = new FieldRule { Css = "small.author::text" } },
            FollowLinks = ["li.next a"],
        },
    });
}

app.Lifetime.ApplicationStopping.Register(() => sandbox.DisposeAsync().AsTask().GetAwaiter().GetResult());
app.Run();
