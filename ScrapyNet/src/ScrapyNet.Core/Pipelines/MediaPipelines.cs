using System.Buffers.Binary;
using System.Collections;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace ScrapyNet.Pipelines;

/// <summary>Result of one media download, stored in the item's result field (<c>files</c> / <c>images</c>).</summary>
public sealed record MediaFile(string Url, string Path, string Checksum, string Status)
{
    /// <summary>Image width and height when known (images pipeline only).</summary>
    public int? Width { get; init; }

    public int? Height { get; init; }
}

/// <summary>Where downloaded media is kept. The default is the local file system under <c>FILES_STORE</c>/<c>IMAGES_STORE</c>.</summary>
public interface IFilesStore
{
    /// <summary>Last modification time of a stored file, or <c>null</c> if absent.</summary>
    Task<DateTimeOffset?> StatAsync(string path, CancellationToken cancellationToken = default);

    Task PersistAsync(string path, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);

    string Describe(string path);
}

public sealed class FileSystemFilesStore(string root) : IFilesStore
{
    public string Root { get; } = System.IO.Path.GetFullPath(root);

    public Task<DateTimeOffset?> StatAsync(string path, CancellationToken cancellationToken = default)
    {
        var full = System.IO.Path.Combine(Root, path);
        return Task.FromResult(File.Exists(full) ? new DateTimeOffset(File.GetLastWriteTimeUtc(full), TimeSpan.Zero) : (DateTimeOffset?)null);
    }

    public async Task PersistAsync(string path, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        var full = System.IO.Path.Combine(Root, path);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        await File.WriteAllBytesAsync(full, data.ToArray(), cancellationToken).ConfigureAwait(false);
    }

    public string Describe(string path) => System.IO.Path.Combine(Root, path);
}

/// <summary>
/// Downloads the files listed in an item's <c>file_urls</c> field (or <c>FileUrls</c> property) and records
/// the results in <c>files</c> (<c>Files</c>). Files are stored as <c>full/&lt;sha1(url)&gt;.&lt;ext&gt;</c>
/// and re-used for <c>FILES_EXPIRES</c> days. Downloads go through the engine, so they get the same
/// middlewares, throttling and proxies as pages. Port of Scrapy's <c>FilesPipeline</c>.
/// </summary>
public class FilesPipeline : ItemPipeline
{
    private readonly Crawler _crawler;
    private readonly TimeSpan _expires;
    private readonly bool _allowRedirects;

    public FilesPipeline(Crawler crawler, ILogger logger) : this(crawler, logger, SettingKeys.FilesStore, SettingKeys.FilesExpires,
        crawler.Settings.GetString(SettingKeys.FilesUrlsField) ?? "file_urls", crawler.Settings.GetString(SettingKeys.FilesResultField) ?? "files")
    {
    }

    protected FilesPipeline(Crawler crawler, ILogger logger, string storeKey, string expiresKey, string urlsField, string resultField)
    {
        var store = crawler.Settings.GetString(storeKey);
        if (string.IsNullOrEmpty(store)) throw new NotConfiguredException($"{storeKey} is not set");
        _crawler = crawler;
        Logger = logger;
        Store = new FileSystemFilesStore(store);
        _expires = TimeSpan.FromDays(crawler.Settings.GetDouble(expiresKey, 90));
        _allowRedirects = crawler.Settings.GetBool(SettingKeys.MediaAllowRedirects);
        UrlsField = urlsField;
        ResultField = resultField;
    }

    public IFilesStore Store { get; set; }

    protected ILogger Logger { get; }

    public string UrlsField { get; }

    public string ResultField { get; }

    /// <summary>Stat prefix, e.g. <c>file</c> → <c>file_count</c>, <c>file_status_count/downloaded</c>.</summary>
    protected virtual string StatPrefix => "file";

    public override async ValueTask<object> ProcessItemAsync(object item, Spider spider)
    {
        var adapter = ItemAdapter.For(item);
        var urls = GetMediaUrls(adapter);
        if (urls.Count == 0) return item;

        var results = await Task.WhenAll(urls.Distinct().Select(url => DownloadOneAsync(url, adapter, spider))).ConfigureAwait(false);
        var ok = results.Where(r => r is not null).Cast<MediaFile>().ToList();
        var resultName = adapter.ResolveFieldName(ResultField);
        if (resultName is not null) adapter.Set(resultName, ok);
        return ItemCompleted(ok, item, spider);
    }

    /// <summary>Hook after all downloads; return the item or throw <see cref="DropItemException"/> (e.g. if nothing downloaded).</summary>
    protected virtual object ItemCompleted(IReadOnlyList<MediaFile> results, object item, Spider spider) => item;

    protected virtual IReadOnlyList<string> GetMediaUrls(ItemAdapter adapter)
    {
        var name = adapter.ResolveFieldName(UrlsField);
        if (name is null) return [];
        return adapter.Get(name) switch
        {
            null => [],
            string s => [s],
            IEnumerable e => [.. e.Cast<object?>().Where(x => x is not null).Select(x => x!.ToString()!)],
            var other => [other.ToString()!],
        };
    }

    /// <summary>The storage path of a URL: <c>full/&lt;sha1&gt;&lt;ext&gt;</c>.</summary>
    public virtual string FilePath(string url, Response? response = null)
    {
        var hash = Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes(url)));
        var ext = System.IO.Path.GetExtension(new Uri(url).AbsolutePath);
        if (ext.Length > 6 || ext.Length == 0) ext = GuessExtension(response?.Headers.Get("Content-Type"));
        return $"full/{hash}{ext}";
    }

    private static string GuessExtension(string? contentType) => contentType?.Split(';')[0].Trim().ToLowerInvariant() switch
    {
        "application/pdf" => ".pdf",
        "image/png" => ".png",
        "image/jpeg" => ".jpg",
        "image/gif" => ".gif",
        "image/webp" => ".webp",
        "text/csv" => ".csv",
        "application/zip" => ".zip",
        "application/json" => ".json",
        _ => "",
    };

    private async Task<MediaFile?> DownloadOneAsync(string url, ItemAdapter item, Spider spider)
    {
        var path = FilePath(url);
        var modified = await Store.StatAsync(path).ConfigureAwait(false);
        if (modified is not null && DateTimeOffset.UtcNow - modified.Value < _expires)
        {
            _crawler.Stats.Inc($"{StatPrefix}_count");
            _crawler.Stats.Inc($"{StatPrefix}_status_count/uptodate");
            var existing = await File.ReadAllBytesAsync(Store.Describe(path)).ConfigureAwait(false);
            return await BuildResultAsync(url, path, existing, null, "uptodate").ConfigureAwait(false);
        }

        var request = new Request(url) { Meta = { ["media_download"] = true } };
        if (!_allowRedirects) request.Meta[MetaKeys.DontRedirect] = true;
        Response response;
        try
        {
            response = await _crawler.Engine.DownloadAsync(request).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.LogWarning("File (error): Error downloading {Kind} from {Request} referred in <{Item}>: {Error}", StatPrefix, request, item.Item.GetType().Name, ex.Message);
            return null;
        }
        if (response.Status != 200 || response.Body.Length == 0)
        {
            Logger.LogWarning("File (code: {Status}): Error downloading {Kind} from {Request}", response.Status, StatPrefix, request);
            return null;
        }
        path = FilePath(url, response);
        try
        {
            var result = await BuildResultAsync(url, path, response.Body, response, "downloaded").ConfigureAwait(false);
            if (result is null) return null;
            await PersistAsync(result.Path, response).ConfigureAwait(false);
            _crawler.Stats.Inc($"{StatPrefix}_count");
            _crawler.Stats.Inc($"{StatPrefix}_status_count/downloaded");
            Logger.LogDebug("File (downloaded): Downloaded {Kind} from {Request}", StatPrefix, request);
            return result;
        }
        catch (DropItemException ex)
        {
            Logger.LogDebug("{Kind} rejected from {Url}: {Reason}", StatPrefix, url, ex.Message);
            return null;
        }
    }

    /// <summary>Builds the result record; images override this to validate and measure.</summary>
    protected virtual Task<MediaFile?> BuildResultAsync(string url, string path, byte[] body, Response? response, string status) =>
        Task.FromResult<MediaFile?>(new MediaFile(url, path, Convert.ToHexStringLower(MD5.HashData(body)), status));

    /// <summary>Stores the downloaded file; images override this to also write thumbnails.</summary>
    protected virtual Task PersistAsync(string path, Response response) => Store.PersistAsync(path, response.Body);
}

/// <summary>Creates thumbnails or converts images. Plug one in with <c>IMAGES_PROCESSOR</c> (a type name).</summary>
public interface IImageProcessor
{
    /// <summary>Resizes to fit within the box, returning encoded bytes (and the extension, e.g. ".jpg").</summary>
    (byte[] Data, string Extension) Thumbnail(byte[] image, int maxWidth, int maxHeight);
}

/// <summary>
/// A <see cref="FilesPipeline"/> for images (<c>image_urls</c> → <c>images</c>): rejects non-images and
/// images smaller than <c>IMAGES_MIN_WIDTH</c>/<c>IMAGES_MIN_HEIGHT</c>, and records dimensions. Dimensions
/// are read from the PNG, JPEG, GIF, WebP or BMP header, so no image library is needed; thumbnails
/// (<c>IMAGES_THUMBS</c>) are produced when an <see cref="IImageProcessor"/> is configured.
/// </summary>
public class ImagesPipeline : FilesPipeline
{
    private readonly int _minWidth;
    private readonly int _minHeight;
    private readonly IReadOnlyDictionary<string, object?> _thumbs;
    private readonly IImageProcessor? _processor;

    public ImagesPipeline(Crawler crawler, ILogger logger) : base(crawler, logger, SettingKeys.ImagesStore, SettingKeys.ImagesExpires,
        crawler.Settings.GetString(SettingKeys.ImagesUrlsField) ?? "image_urls", crawler.Settings.GetString(SettingKeys.ImagesResultField) ?? "images")
    {
        _minWidth = crawler.Settings.GetInt(SettingKeys.ImagesMinWidth);
        _minHeight = crawler.Settings.GetInt(SettingKeys.ImagesMinHeight);
        _thumbs = crawler.Settings.GetDict(SettingKeys.ImagesThumbs);
        var processorType = crawler.Settings.GetTypeSetting(SettingKeys.ImagesProcessor);
        if (processorType is not null) _processor = (IImageProcessor?)ComponentFactory.Create(processorType, crawler);
    }

    protected override string StatPrefix => "image";

    protected override Task<MediaFile?> BuildResultAsync(string url, string path, byte[] body, Response? response, string status)
    {
        var size = ImageInfo.GetSize(body) ?? throw new DropItemException($"Not an image: {url}");
        if (size.Width < _minWidth || size.Height < _minHeight)
            throw new DropItemException($"Image too small ({size.Width}x{size.Height} < {_minWidth}x{_minHeight})");
        return Task.FromResult<MediaFile?>(new MediaFile(url, path, Convert.ToHexStringLower(MD5.HashData(body)), status)
        {
            Width = size.Width,
            Height = size.Height,
        });
    }

    protected override async Task PersistAsync(string path, Response response)
    {
        await base.PersistAsync(path, response).ConfigureAwait(false);
        if (_processor is null) return;
        foreach (var (name, box) in _thumbs)
        {
            var (w, h) = box switch
            {
                IList l when l.Count >= 2 => (Convert.ToInt32(l[0]), Convert.ToInt32(l[1])),
                _ => (100, 100),
            };
            var (data, ext) = _processor.Thumbnail(response.Body, w, h);
            var file = System.IO.Path.GetFileNameWithoutExtension(path);
            await Store.PersistAsync($"thumbs/{name}/{file}{ext}", data).ConfigureAwait(false);
        }
    }
}

/// <summary>Reads image dimensions from file headers without decoding pixels.</summary>
public static class ImageInfo
{
    public static (int Width, int Height)? GetSize(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 24 && data[..8].SequenceEqual((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
            return ((int)BinaryPrimitives.ReadUInt32BigEndian(data[16..]), (int)BinaryPrimitives.ReadUInt32BigEndian(data[20..]));
        if (data.Length >= 10 && (data[..6].SequenceEqual("GIF87a"u8) || data[..6].SequenceEqual("GIF89a"u8)))
            return (BinaryPrimitives.ReadUInt16LittleEndian(data[6..]), BinaryPrimitives.ReadUInt16LittleEndian(data[8..]));
        if (data.Length >= 26 && data[0] == 'B' && data[1] == 'M')
            return (BinaryPrimitives.ReadInt32LittleEndian(data[18..]), Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(data[22..])));
        if (data.Length >= 30 && data[..4].SequenceEqual("RIFF"u8) && data[8..12].SequenceEqual("WEBP"u8))
        {
            var chunk = data[12..16];
            if (chunk.SequenceEqual("VP8X"u8)) return (1 + (data[24] | data[25] << 8 | data[26] << 16), 1 + (data[27] | data[28] << 8 | data[29] << 16));
            if (chunk.SequenceEqual("VP8 "u8)) return (BinaryPrimitives.ReadUInt16LittleEndian(data[26..]) & 0x3FFF, BinaryPrimitives.ReadUInt16LittleEndian(data[28..]) & 0x3FFF);
            if (chunk.SequenceEqual("VP8L"u8))
            {
                var bits = BinaryPrimitives.ReadUInt32LittleEndian(data[21..]);
                return ((int)(bits & 0x3FFF) + 1, (int)((bits >> 14) & 0x3FFF) + 1);
            }
        }
        if (data.Length >= 4 && data[0] == 0xFF && data[1] == 0xD8)
        {
            var i = 2;
            while (i + 9 < data.Length)
            {
                if (data[i] != 0xFF) { i++; continue; }
                var marker = data[i + 1];
                if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
                    return (BinaryPrimitives.ReadUInt16BigEndian(data[(i + 7)..]), BinaryPrimitives.ReadUInt16BigEndian(data[(i + 5)..]));
                var length = BinaryPrimitives.ReadUInt16BigEndian(data[(i + 2)..]);
                i += 2 + length;
            }
        }
        return null;
    }
}
