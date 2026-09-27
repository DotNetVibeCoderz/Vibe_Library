using ScrapyGallery.Infrastructure;
using ScrapyNet;
using ScrapyNet.Sandbox;

namespace ScrapyGallery.Demos;

public sealed class ApiDemo : Demo
{
    public override DemoGroup Group => DemoGroup.Crawl;
    public override string Title => "JSON APIs and API keys";
    public override string TitleId => "API JSON dan API key";
    public override string Summary => "Crawl a paginated JSON API that requires a key. DOMAIN_HEADERS sends the key only to that host, so it can never leak to another site a link points to. Remove the key to see 401s.";
    public override string SummaryId => "Crawl API JSON berhalaman yang butuh kunci. DOMAIN_HEADERS hanya mengirim kunci ke host tersebut sehingga tidak bocor ke situs lain. Hapus kunci untuk melihat 401.";
    public override IReadOnlyList<string> Concepts => ["TextResponse.Json()", "DOMAIN_HEADERS", "cursor pagination"];

    public override IReadOnlyList<DemoInput> Inputs => [new("key", "X-Api-Key", "X-Api-Key", SandboxSite.ApiKey)];

    // <demo>
    public sealed class ProductsApiSpider(string url) : Spider
    {
        public override IReadOnlyList<string> StartUrls => [url];

        public override async IAsyncEnumerable<object> Parse(Response response)
        {
            var json = ((TextResponse)response).Json()!;
            foreach (var p in json["products"]!.AsArray())
                yield return new { Id = (int)p!["id"]!, Title = (string)p["title"]!, Price = (decimal)p["price"]!, Stock = (int)p["stock"]! };

            if (json["next"]?.GetValue<string>() is { } next)
                yield return response.Follow(next, Parse);
            await Task.CompletedTask;
        }
    }

    public static Settings WithApiKey(Settings settings, string host, string key)
    {
        settings.Set(SettingKeys.DomainHeaders, new Dictionary<string, object?>
        {
            [host] = new Dictionary<string, object?> { ["X-Api-Key"] = key },
        });
        return settings;
    }
    // </demo>

    public override async Task RunAsync(DemoContext ctx)
    {
        var settings = ctx.NewSettings();
        if (ctx.Input("key").Length > 0) WithApiKey(settings, "127.0.0.1", ctx.Input("key"));
        var result = await ctx.CrawlAsync(new ProductsApiSpider(ctx.Site.Url("/api/secure/products?page=1")), settings);
        ctx.Print(result.ToString());
    }
}
