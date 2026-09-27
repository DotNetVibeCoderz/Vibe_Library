using ScrapyGallery.Infrastructure;
using ScrapyNet;

namespace ScrapyGallery.Demos;

public sealed class FeedExportsDemo : Demo
{
    public override DemoGroup Group => DemoGroup.Process;
    public override string Title => "Feed exports";
    public override string TitleId => "Ekspor feed";
    public override string Summary => "One crawl, four files: JSON, JSON Lines, CSV with chosen columns, and gzipped XML — plus batches of 20 items per file. Paths can use placeholders such as %(name)s and %(batch_id)d.";
    public override string SummaryId => "Satu crawl, empat file: JSON, JSON Lines, CSV dengan kolom pilihan, dan XML ber-gzip — ditambah batch 20 item per file. Path bisa memakai placeholder %(name)s dan %(batch_id)d.";
    public override IReadOnlyList<string> Concepts => ["FEEDS", "FeedOptions", "BatchItemCount", "%(name)s"];

    // <demo>
    public static void ConfigureFeeds(Settings settings, string folder)
    {
        settings.Feeds
            .Add(Path.Combine(folder, "%(name)s.json"), new FeedOptions { Overwrite = true, Indent = 2 })
            .Add(Path.Combine(folder, "%(name)s.jsonl"), new FeedOptions { Overwrite = true })
            .Add(Path.Combine(folder, "%(name)s.csv"), new FeedOptions { Overwrite = true, Fields = ["Author", "Text"] })
            .Add(Path.Combine(folder, "%(name)s.xml.gz"), new FeedOptions { Overwrite = true })
            .Add(Path.Combine(folder, "batches", "part-%(batch_id)03d.jsonl"), new FeedOptions { Overwrite = true, BatchItemCount = 20 });
    }
    // </demo>

    public override async Task RunAsync(DemoContext ctx)
    {
        var folder = Path.Combine(Path.GetTempPath(), "scrapy-gallery-feeds", Guid.NewGuid().ToString("N")[..6]);
        var settings = ctx.NewSettings();
        ConfigureFeeds(settings, folder);
        var result = await ctx.CrawlAsync(new FirstSpiderDemo.QuotesSpider(ctx.Site.Url("/quotes/")), settings);
        ctx.Print(result.ToString());
        foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Order())
            ctx.Print($"{Path.GetRelativePath(folder, file),-26} {new FileInfo(file).Length,8:N0} bytes");
        ctx.Print("\n— quotes.csv (first lines) —");
        foreach (var line in File.ReadLines(Path.Combine(folder, "quotes.csv")).Take(4)) ctx.Print(line);
        ctx.Print("\n— quotes.jsonl (first line) —");
        ctx.Print(File.ReadLines(Path.Combine(folder, "quotes.jsonl")).First());
    }
}
