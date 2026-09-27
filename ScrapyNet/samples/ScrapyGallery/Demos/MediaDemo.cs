using ScrapyGallery.Infrastructure;
using ScrapyNet;
using ScrapyNet.Pipelines;

namespace ScrapyGallery.Demos;

public sealed class MediaDemo : Demo
{
    public override DemoGroup Group => DemoGroup.Process;
    public override string Title => "Files and images";
    public override string TitleId => "File dan gambar";
    public override string Summary => "Put URLs in FileUrls or ImageUrls and the media pipelines download them through the engine, store them by content hash, skip fresh copies, and record path, checksum and — for images — dimensions.";
    public override string SummaryId => "Isi URL di FileUrls atau ImageUrls dan pipeline media mengunduhnya lewat engine, menyimpannya berdasarkan hash, melewati salinan yang masih baru, dan mencatat path, checksum, serta ukuran gambar.";
    public override IReadOnlyList<string> Concepts => ["FilesPipeline", "ImagesPipeline", "MediaFile", "FILES_STORE"];

    // <demo>
    public sealed class Cover
    {
        public string? Title { get; set; }
        public List<string> ImageUrls { get; set; } = [];
        public List<MediaFile> Images { get; set; } = [];
    }

    public sealed class Report
    {
        public string? Name { get; set; }
        public List<string> FileUrls { get; set; } = [];
        public List<MediaFile> Files { get; set; } = [];
    }

    public sealed class MediaSpider(string books, string files) : Spider
    {
        public override IReadOnlyList<string> StartUrls => [books, files];

        public override async IAsyncEnumerable<object> Parse(Response response)
        {
            foreach (var book in response.Css("article.product_pod").Take(8))
                yield return new Cover { Title = book.Css("h3 a::attr(title)").Get(), ImageUrls = [response.UrlJoin(book.Css("img::attr(src)").Get()!)] };
            foreach (var link in response.Css("a.report"))
                yield return new Report { Name = link.Css("::text").Get(), FileUrls = [response.UrlJoin(link.Attrib["href"])] };
            await Task.CompletedTask;
        }
    }

    public static void ConfigureMedia(Settings settings, string store)
    {
        settings.Set(SettingKeys.FilesStore, Path.Combine(store, "files"));
        settings.Set(SettingKeys.ImagesStore, Path.Combine(store, "images"));
        settings.Set(SettingKeys.ImagesMinWidth, 100);
        settings.ItemPipelines.Add<FilesPipeline>(1).Add<ImagesPipeline>(2);
    }
    // </demo>

    public override async Task RunAsync(DemoContext ctx)
    {
        var store = Path.Combine(Path.GetTempPath(), "scrapy-gallery-media");
        var settings = ctx.NewSettings();
        ConfigureMedia(settings, store);
        var crawler = ctx.CreateCrawler(new MediaSpider(ctx.Site.Url("/books/"), ctx.Site.Url("/files/")), settings);
        crawler.Signals.Connect(Signals.ItemScraped, e =>
        {
            if (e.Item is Cover { Images: [var img, ..] }) ctx.Observer.AddImage(Path.Combine(store, "images", img.Path));
        });
        var result = await crawler.CrawlAsync(cancellationToken: ctx.CancellationToken);
        ctx.Print(result.ToString());
        ctx.Print($"files: {result.Stats.GetValueOrDefault("file_count")} · images: {result.Stats.GetValueOrDefault("image_count")} · stored under {store}");
    }
}
