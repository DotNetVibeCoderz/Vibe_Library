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
//   open http://localhost:5080/admin/ and sign in with dev-admin (or dev-viewer, read-only)
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
app.MapScrapyNetAdmin();                          // the console: http://localhost:5080/admin/
app.MapGet("/", () => Results.Redirect("/admin/"));

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
if (!catalog.TryGet("sandbox-books", out _))
{
    catalog.Create(new SpiderDefinition
    {
        Id = "sandbox-books",
        Name = "sandbox-books",
        Description = "Book detail pages from the local practice site",
        Target = new TargetConfig { StartUrls = [sandbox.Url("/books/")] },
        Rules = new ScrapingRules
        {
            Fields = new()
            {
                ["title"] = new FieldRule { Css = "h1::text", Processors = ["clean"] },
                ["price"] = new FieldRule { Css = "p.price_color::text", Processors = ["price"] },
            },
            FollowLinks = ["li.next a"],
            DetailLinks = ["article.product_pod h3 a"],
            RequiredFields = ["title"],
        },
        Settings = new() { ["DOWNLOAD_DELAY"] = 0.05 },
    });
}
var schedules = app.Services.GetRequiredService<CrawlScheduler>();
if (schedules.List().All(s => s.Id != "books-nightly"))
    schedules.Add(new ScheduleEntry { Id = "books-nightly", SpiderId = "sandbox-books", Cron = "0 2 * * *" });

app.Lifetime.ApplicationStopping.Register(() => sandbox.DisposeAsync().AsTask().GetAwaiter().GetResult());
app.Run();
