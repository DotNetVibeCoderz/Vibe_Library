using System.Globalization;
using System.Text.Json;

namespace ScrapyNet.Platform;

/// <summary>Metadata saved next to each raw body.</summary>
public sealed record RawContentEntry(string Url, int Status, string Spider, DateTimeOffset CrawledAt, string ContentType, long Size, string File, Dictionary<string, string> Headers);

/// <summary>
/// Archives every downloaded response body with its metadata (URL, status, headers, crawl time) under
/// <c>RAW_CONTENT_DIR/spider/yyyy-MM-dd/</c>, so data can be re-extracted later without re-crawling and
/// audits can show exactly what a page looked like. Enable with
/// <c>settings.Extensions.Add&lt;RawContentExtension&gt;(0)</c> and <c>RAW_CONTENT_DIR</c>.
/// </summary>
public sealed class RawContentExtension
{
    public const string SettingKey = "RAW_CONTENT_DIR";
    public const string OnlyHtmlKey = "RAW_CONTENT_ONLY_HTML";

    private readonly string _root;
    private readonly bool _onlyHtml;
    private readonly Crawler _crawler;

    public RawContentExtension(Crawler crawler)
    {
        _root = crawler.Settings.GetString(SettingKey) ?? throw new NotConfiguredException($"{SettingKey} is not set");
        _onlyHtml = crawler.Settings.GetBool(OnlyHtmlKey, true);
        _crawler = crawler;
        crawler.Signals.Connect(Signals.ResponseReceived, OnResponseAsync);
    }

    private async ValueTask OnResponseAsync(ResponseEventArgs e)
    {
        if (_onlyHtml && e.Response is not HtmlResponse) return;
        var now = DateTimeOffset.UtcNow;
        var fp = _crawler.Fingerprinter.Fingerprint(e.Request).ToHex();
        var dir = Path.Combine(_root, e.Spider.Name, now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(dir);
        var ext = e.Response switch { HtmlResponse => ".html", XmlResponse => ".xml", JsonResponse => ".json", TextResponse => ".txt", _ => ".bin" };
        var file = Path.Combine(dir, fp + ext);
        await File.WriteAllBytesAsync(file, e.Response.Body).ConfigureAwait(false);
        var entry = new RawContentEntry(e.Response.Url, e.Response.Status, e.Spider.Name, now, e.Response.Headers.Get("Content-Type") ?? "",
            e.Response.Body.Length, Path.GetFileName(file), e.Response.Headers.ToDictionary());
        await File.WriteAllTextAsync(Path.Combine(dir, fp + ".meta.json"), JsonSerializer.Serialize(entry, ScrapyJson.Default)).ConfigureAwait(false);
        _crawler.Stats.Inc("raw_content/saved");
        _crawler.Stats.Inc("raw_content/bytes", e.Response.Body.Length);
    }
}

/// <summary>Reads what <see cref="RawContentExtension"/> archived.</summary>
public sealed class RawContentArchive(string root)
{
    public IEnumerable<RawContentEntry> Entries(string? spider = null)
    {
        if (!Directory.Exists(root)) yield break;
        var dirs = spider is null ? Directory.EnumerateDirectories(root) : [Path.Combine(root, spider)];
        foreach (var d in dirs.Where(Directory.Exists))
            foreach (var meta in Directory.EnumerateFiles(d, "*.meta.json", SearchOption.AllDirectories))
                if (JsonSerializer.Deserialize<RawContentEntry>(File.ReadAllText(meta), ScrapyJson.Default) is { } entry)
                    yield return entry with { File = Path.Combine(Path.GetDirectoryName(meta)!, entry.File) };
    }

    /// <summary>Rebuilds a response from the archive, ready for selectors and re-extraction.</summary>
    public static Response Load(RawContentEntry entry)
    {
        var headers = new Headers();
        foreach (var (k, v) in entry.Headers) headers.Set(k, v);
        return ResponseTypes.Create(entry.Url, entry.Status, headers, File.ReadAllBytes(entry.File));
    }
}
