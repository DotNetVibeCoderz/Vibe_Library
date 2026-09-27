using System.Globalization;
using System.IO.Compression;
using Microsoft.Extensions.Logging;
using ScrapyNet.Exporters;

namespace ScrapyNet.Extensions;

/// <summary>Where a feed's bytes end up (Scrapy's feed storage backends).</summary>
public interface IFeedStorage
{
    /// <summary>Opens the stream items are written to.</summary>
    Stream Open(Spider spider);

    /// <summary>Called after the exporter finished writing to the stream; uploads, closes, etc.</summary>
    Task StoreAsync(Stream stream, CancellationToken cancellationToken = default);

    /// <summary>Human-readable location for logs.</summary>
    string Location { get; }
}

/// <summary>Local file (plain path or <c>file://</c>). Appends unless <see cref="FeedOptions.Overwrite"/>.</summary>
public sealed class FileFeedStorage(string path, bool overwrite) : IFeedStorage
{
    public string Location => Path.GetFullPath(path);

    public Stream Open(Spider spider)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        return new FileStream(path, overwrite ? FileMode.Create : FileMode.Append, FileAccess.Write, FileShare.Read, 65536);
    }

    public Task StoreAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        stream.Dispose();
        return Task.CompletedTask;
    }
}

/// <summary>Standard output (<c>stdout:</c> or <c>-</c>).</summary>
public sealed class StdoutFeedStorage : IFeedStorage
{
    public string Location => "stdout";

    public Stream Open(Spider spider) => Console.OpenStandardOutput();

    public Task StoreAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        stream.Flush();
        return Task.CompletedTask;
    }
}

/// <summary>
/// HTTP(S) upload: the feed is buffered to a temp file and PUT to the URL when it closes. Works with any
/// endpoint that accepts PUT, including presigned object-storage URLs (S3, GCS, Azure Blob SAS, MinIO).
/// </summary>
public sealed class HttpPutFeedStorage(string url) : IFeedStorage
{
    private static readonly HttpClient Client = new();
    private string? _tempPath;

    public string Location => url.Split('?')[0];

    public Stream Open(Spider spider)
    {
        _tempPath = Path.GetTempFileName();
        return new FileStream(_tempPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 65536);
    }

    public async Task StoreAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        stream.Position = 0;
        using var content = new StreamContent(stream);
        using var response = await Client.PutAsync(url, content, cancellationToken).ConfigureAwait(false);
        await stream.DisposeAsync().ConfigureAwait(false);
        if (_tempPath is not null) File.Delete(_tempPath);
        response.EnsureSuccessStatusCode();
    }
}

/// <summary>Feeds written to memory, for tests and embedding (<c>memory:name</c>); read them with <see cref="Get"/>.</summary>
public sealed class MemoryFeedStorage(string name) : IFeedStorage
{
    private static readonly Dictionary<string, byte[]> Stored = new(StringComparer.Ordinal);

    public string Location => "memory:" + name;

    public Stream Open(Spider spider) => new MemoryStream();

    public Task StoreAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        lock (Stored) Stored[name] = ((MemoryStream)stream).ToArray();
        return Task.CompletedTask;
    }

    public static byte[]? Get(string name)
    {
        lock (Stored) return Stored.GetValueOrDefault(name);
    }
}

/// <summary>
/// Exports scraped items to every feed in <c>FEEDS</c> (Scrapy's <c>FeedExporter</c> extension):
/// JSON, JSON Lines, CSV or XML; to files, stdout, memory or HTTP PUT; optionally gzipped, filtered by
/// item type, and split into batches of <c>batch_item_count</c> items.
/// </summary>
public sealed class FeedExporter
{
    private readonly Crawler _crawler;
    private readonly ILogger _logger;
    private readonly List<FeedSlot> _slots = [];
    private readonly object _gate = new();
    private DateTimeOffset _startTime;

    public FeedExporter(Crawler crawler, ILogger logger)
    {
        if (crawler.Settings.Feeds.Count == 0) throw new NotConfiguredException();
        _crawler = crawler;
        _logger = logger;
        crawler.Signals.Connect(Signals.SpiderOpened, OnOpened);
        crawler.Signals.Connect(Signals.ItemScraped, OnItemScraped);
        crawler.Signals.Connect(Signals.SpiderClosed, OnClosedAsync);
    }

    /// <summary>Items written so far, per feed URI template.</summary>
    public IReadOnlyDictionary<string, int> ItemCounts
    {
        get
        {
            lock (_gate) return _slots.GroupBy(s => s.UriTemplate).ToDictionary(g => g.Key, g => g.Sum(s => s.TotalCount));
        }
    }

    private void OnOpened(SpiderEventArgs e)
    {
        _startTime = DateTimeOffset.UtcNow;
        var settings = _crawler.Settings;
        foreach (var (uri, raw) in settings.Feeds)
        {
            var options = raw with
            {
                Format = raw.Format ?? ItemExporter.FormatFromUri(uri) ?? "jsonlines",
                Fields = raw.Fields ?? (settings.GetList(SettingKeys.FeedExportFields) is { Count: > 0 } f ? f : null),
                Indent = raw.Indent ?? settings.GetInt(SettingKeys.FeedExportIndent),
                StoreEmpty = raw.StoreEmpty ?? settings.GetBool(SettingKeys.FeedStoreEmpty, true),
                Encoding = raw.Encoding ?? EncodingDetector.GetEncoding(settings.GetString(SettingKeys.FeedExportEncoding) ?? "utf-8"),
                BatchItemCount = raw.BatchItemCount > 0 ? raw.BatchItemCount : settings.GetInt(SettingKeys.FeedExportBatchItemCount),
                Gzip = raw.Gzip || uri.EndsWith(".gz", StringComparison.OrdinalIgnoreCase),
            };
            if (options.BatchItemCount > 0 && !uri.Contains("%(batch_id)") && !uri.Contains("%(batch_time)"))
                throw new InvalidOperationException($"Feed '{uri}' has batch_item_count but no %(batch_id)d or %(batch_time)s placeholder.");
            lock (_gate) _slots.Add(new FeedSlot(uri, options, 1));
        }
    }

    private void OnItemScraped(ItemEventArgs e)
    {
        lock (_gate)
        {
            for (var i = 0; i < _slots.Count; i++)
            {
                var slot = _slots[i];
                if (!slot.Accepts(e.Item)) continue;
                slot.EnsureStarted(this, e.Spider);
                slot.Exporter!.ExportItem(e.Item);
                slot.ItemCount++;
                slot.TotalCount++;
                if (slot.Options.BatchItemCount > 0 && slot.ItemCount >= slot.Options.BatchItemCount)
                {
                    CloseSlotAsync(slot, e.Spider).GetAwaiter().GetResult();
                    _slots[i] = new FeedSlot(slot.UriTemplate, slot.Options, slot.BatchId + 1) { TotalCount = slot.TotalCount };
                }
            }
        }
    }

    private async ValueTask OnClosedAsync(SpiderClosedEventArgs e)
    {
        List<FeedSlot> slots;
        lock (_gate) slots = [.. _slots];
        foreach (var slot in slots)
        {
            if (slot.Exporter is null && slot.Options.StoreEmpty == true && slot.BatchId == 1) slot.EnsureStarted(this, e.Spider);
            await CloseSlotAsync(slot, e.Spider).ConfigureAwait(false);
        }
        await _crawler.Signals.SendAsync(Signals.FeedExporterClosed, new SpiderEventArgs(e.Spider)).ConfigureAwait(false);
    }

    private async Task CloseSlotAsync(FeedSlot slot, Spider spider)
    {
        if (slot.Exporter is null || slot.Closed) return;
        slot.Closed = true;
        slot.Exporter.FinishExporting();
        if (slot.Compressor is not null) await slot.Compressor.DisposeAsync().ConfigureAwait(false);
        try
        {
            await slot.Storage!.StoreAsync(slot.Stream!).ConfigureAwait(false);
            _logger.LogInformation("Stored {Format} feed ({Count} items) in: {Location}", slot.Options.Format, slot.ItemCount, slot.Storage.Location);
            _crawler.Stats.Inc($"feedexport/success_count/{slot.Storage.GetType().Name}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error storing {Format} feed ({Count} items) in: {Location}", slot.Options.Format, slot.ItemCount, slot.Storage!.Location);
            _crawler.Stats.Inc($"feedexport/failed_count/{slot.Storage.GetType().Name}");
        }
        await _crawler.Signals.SendAsync(Signals.FeedSlotClosed, new FeedSlotClosedEventArgs(slot.Uri!, slot.ItemCount, spider)).ConfigureAwait(false);
    }

    /// <summary>Expands <c>%(name)s</c>, <c>%(time)s</c>, <c>%(batch_id)d</c>, <c>%(batch_time)s</c> and spider arguments.</summary>
    public string ExpandUri(string template, Spider spider, int batchId)
    {
        var time = _startTime.ToString("yyyy-MM-ddTHH-mm-ss", CultureInfo.InvariantCulture);
        var result = template
            .Replace("%(name)s", spider.Name)
            .Replace("%(time)s", time)
            .Replace("%(batch_time)s", DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH-mm-ss.ffffff", CultureInfo.InvariantCulture))
            .Replace("%(batch_id)d", batchId.ToString(CultureInfo.InvariantCulture));
        var padded = System.Text.RegularExpressions.Regex.Match(result, @"%\(batch_id\)0(\d+)d");
        if (padded.Success) result = result.Replace(padded.Value, batchId.ToString(new string('0', int.Parse(padded.Groups[1].Value, CultureInfo.InvariantCulture)), CultureInfo.InvariantCulture));
        foreach (var (k, v) in spider.Arguments) result = result.Replace($"%({k})s", v);
        return result;
    }

    internal static IFeedStorage CreateStorage(string uri, bool overwrite)
    {
        if (uri is "-" or "stdout:" || uri.StartsWith("stdout:", StringComparison.OrdinalIgnoreCase)) return new StdoutFeedStorage();
        if (uri.StartsWith("memory:", StringComparison.OrdinalIgnoreCase)) return new MemoryFeedStorage(uri[7..]);
        if (uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return new HttpPutFeedStorage(uri);
        if (uri.StartsWith("file://", StringComparison.OrdinalIgnoreCase)) return new FileFeedStorage(new Uri(uri).LocalPath, overwrite);
        return new FileFeedStorage(uri, overwrite);
    }

    private sealed class FeedSlot(string uriTemplate, FeedOptions options, int batchId)
    {
        public string UriTemplate { get; } = uriTemplate;
        public FeedOptions Options { get; } = options;
        public int BatchId { get; } = batchId;
        public string? Uri { get; private set; }
        public IFeedStorage? Storage { get; private set; }
        public Stream? Stream { get; private set; }
        public GZipStream? Compressor { get; private set; }
        public ItemExporter? Exporter { get; private set; }
        public int ItemCount { get; set; }
        public int TotalCount { get; set; }
        public bool Closed { get; set; }

        public bool Accepts(object item)
        {
            if (Options.ItemTypes is { Count: > 0 } types && !types.Any(t => t.IsInstanceOfType(item))) return false;
            return Options.ItemFilter?.Invoke(item) ?? true;
        }

        public void EnsureStarted(FeedExporter owner, Spider spider)
        {
            if (Exporter is not null) return;
            Uri = owner.ExpandUri(UriTemplate, spider, BatchId);
            // Batches after the first always start fresh files.
            Storage = CreateStorage(Uri, Options.Overwrite || BatchId > 1);
            Stream = Storage.Open(spider);
            Stream target = Stream;
            if (Options.Gzip)
            {
                Compressor = new GZipStream(Stream, CompressionLevel.Optimal, leaveOpen: true);
                target = Compressor;
            }
            Exporter = ItemExporter.Create(Options.Format!, target, Options);
            Exporter.StartExporting();
        }
    }
}
