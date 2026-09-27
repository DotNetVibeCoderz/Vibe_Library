using Microsoft.Data.Sqlite;
using ScrapyNet.Loaders;
using ScrapyNet.Pipelines;
using ScrapyNet.Sandbox;

namespace ScrapyNet.Samples;

// ---- media: files and images -------------------------------------------------------------------------

public sealed class Report
{
    public string? Title { get; set; }
    public List<string> FileUrls { get; set; } = [];
    public List<MediaFile> Files { get; set; } = [];
}

public sealed class Cover
{
    public string? Title { get; set; }
    public List<string> ImageUrls { get; set; } = [];
    public List<MediaFile> Images { get; set; } = [];
}

public sealed class MediaSpider(string baseUrl) : Spider
{
    public override string Name => "media";
    public override IReadOnlyList<string> StartUrls => [baseUrl + "/files/", baseUrl + "/books/"];

    public override async IAsyncEnumerable<object> Parse(Response response)
    {
        if (response.Url.Contains("/files/"))
        {
            // The FilesPipeline downloads every URL in FileUrls and fills Files with the results.
            yield return new Report
            {
                Title = "Quarterly reports",
                FileUrls = [.. response.Css("a.report::attr(href)").GetAll().Select(response.UrlJoin)],
            };
        }
        else
        {
            foreach (var book in response.Css("article.product_pod").Take(6))
                yield return new Cover
                {
                    Title = book.Css("h3 a::attr(title)").Get(),
                    ImageUrls = [response.UrlJoin(book.Css("img::attr(src)").Get()!)],
                };
        }
        await Task.CompletedTask;
    }
}

public static class MediaSample
{
    public static async Task RunAsync(SandboxSite site)
    {
        var store = SampleRegistry.OutputDir("media");
        var settings = SampleRegistry.Defaults("WARNING");
        settings.Set(SettingKeys.FilesStore, Path.Combine(store, "files"));
        settings.Set(SettingKeys.ImagesStore, Path.Combine(store, "images"));
        settings.Set(SettingKeys.ImagesMinWidth, 100);
        settings.ItemPipelines.Add<FilesPipeline>(1).Add<ImagesPipeline>(2);

        var (result, items) = await Scrapy.CrawlAndCollectAsync(new MediaSpider(site.BaseUrl), settings);
        foreach (var report in items.OfType<Report>())
            foreach (var f in report.Files) Console.WriteLine($"file  {f.Path}  md5={f.Checksum[..8]}  ({f.Status})");
        foreach (var cover in items.OfType<Cover>())
            foreach (var i in cover.Images) Console.WriteLine($"image {i.Path}  {i.Width}x{i.Height}  {cover.Title}");
        Console.WriteLine($"{result.Stats.GetValueOrDefault("file_count")} files, {result.Stats.GetValueOrDefault("image_count")} images stored under {store}");
    }
}

// ---- pipelines: clean, validate, dedupe, store ---------------------------------------------------------

public static class PipelinesSample
{
    public static async Task RunAsync(SandboxSite site)
    {
        var db = Path.Combine(SampleRegistry.OutputDir("pipelines"), "books.db");
        if (File.Exists(db)) File.Delete(db);

        var settings = SampleRegistry.Defaults("ERROR");
        // Order matters: clean first, then validate, then drop duplicates, then store.
        settings.ItemPipelines
            .Add(new CleaningPipeline().Field("Title", Transforms.Upper), 100)
            .Add(new ValidationPipeline().Require("Title", "Price").Range("Price", min: 20).Rule("in stock", i => ((Book)i).Stock > 0), 200)
            .Add(new DuplicatesPipeline("Upc"), 300)
            .Add(new AdoNetPipeline(() => new SqliteConnection($"Data Source={db}"), "books")
            {
                CreateTableSql = "CREATE TABLE IF NOT EXISTS books (Title TEXT, Price REAL, Rating INTEGER, Stock INTEGER, Upc TEXT PRIMARY KEY, Url TEXT)",
                InsertVerb = "INSERT OR REPLACE INTO",
            }, 800);

        var result = await Scrapy.CrawlAsync(new BooksSpider(site.Url("/books/")), settings);
        Console.WriteLine($"{result.ItemCount} books stored, {result.Stats.GetValueOrDefault("item_dropped_count")} dropped by validation");

        await using var conn = new SqliteConnection($"Data Source={db}");
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Rating, COUNT(*), ROUND(AVG(Price), 2) FROM books GROUP BY Rating ORDER BY Rating";
        await using var reader = await cmd.ExecuteReaderAsync();
        Console.WriteLine("rating  books  avg price");
        while (await reader.ReadAsync()) Console.WriteLine($"{reader.GetInt32(0),6}  {reader.GetInt32(1),5}  {reader.GetDouble(2),9:0.00}");
    }
}
