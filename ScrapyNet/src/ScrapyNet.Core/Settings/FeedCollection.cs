using System.Collections;
using System.Globalization;
using System.Text;

namespace ScrapyNet;

/// <summary>Options for one feed export (an entry of Scrapy's <c>FEEDS</c>).</summary>
public sealed record FeedOptions
{
    /// <summary><c>json</c>, <c>jsonlines</c> (<c>jsonl</c>, <c>jl</c>), <c>csv</c> or <c>xml</c>. Guessed from the URI extension when null.</summary>
    public string? Format { get; init; }

    /// <summary>Overwrite the target instead of appending. Scrapy's <c>-O</c> vs <c>-o</c>.</summary>
    public bool Overwrite { get; init; }

    public Encoding? Encoding { get; init; }

    /// <summary>Fields to export, in order. <c>null</c> = all fields of each item.</summary>
    public IReadOnlyList<string>? Fields { get; init; }

    /// <summary>JSON/XML indentation (0 = compact, one item per line for JSON).</summary>
    public int? Indent { get; init; }

    /// <summary>Create the file even when no items were scraped.</summary>
    public bool? StoreEmpty { get; init; }

    /// <summary>Start a new file every N items; the URI must contain <c>%(batch_id)d</c> or <c>%(batch_time)s</c>.</summary>
    public int BatchItemCount { get; init; }

    /// <summary>Only export items of these types.</summary>
    public IReadOnlyList<Type>? ItemTypes { get; init; }

    /// <summary>Only export items for which this returns true.</summary>
    public Func<object, bool>? ItemFilter { get; init; }

    /// <summary>Gzip the output.</summary>
    public bool Gzip { get; init; }

    /// <summary>CSV delimiter.</summary>
    public char CsvDelimiter { get; init; } = ',';

    /// <summary>CSV header row.</summary>
    public bool IncludeHeadersLine { get; init; } = true;

    /// <summary>XML root and item element names.</summary>
    public string XmlRootElement { get; init; } = "items";

    public string XmlItemElement { get; init; } = "item";

    internal static FeedOptions FromDictionary(IReadOnlyDictionary<string, object?> d)
    {
        var options = new FeedOptions();
        foreach (var (key, value) in d)
        {
            switch (key.ToLowerInvariant())
            {
                case "format": options = options with { Format = value?.ToString() }; break;
                case "overwrite": options = options with { Overwrite = Convert.ToBoolean(value, CultureInfo.InvariantCulture) }; break;
                case "encoding": options = options with { Encoding = value is null ? null : EncodingDetector.GetEncoding(value.ToString()!) }; break;
                case "fields": options = options with { Fields = value is IEnumerable e and not string ? e.Cast<object?>().Select(x => x!.ToString()!).ToList() : value?.ToString()?.Split(',', StringSplitOptions.TrimEntries) }; break;
                case "indent": options = options with { Indent = value is null ? null : Convert.ToInt32(value, CultureInfo.InvariantCulture) }; break;
                case "store_empty": options = options with { StoreEmpty = Convert.ToBoolean(value, CultureInfo.InvariantCulture) }; break;
                case "batch_item_count": options = options with { BatchItemCount = Convert.ToInt32(value, CultureInfo.InvariantCulture) }; break;
                case "gzip": options = options with { Gzip = Convert.ToBoolean(value, CultureInfo.InvariantCulture) }; break;
                case "item_classes":
                    var types = (value is IEnumerable e2 and not string ? e2.Cast<object?>().Select(x => x!.ToString()!) : [value!.ToString()!])
                        .Select(n => TypeResolver.Resolve(n) ?? throw new ArgumentException($"Unknown item class {n}")).ToList();
                    options = options with { ItemTypes = types };
                    break;
            }
        }
        return options;
    }
}

/// <summary>
/// Feed exports configured for a crawl: output URI → <see cref="FeedOptions"/>. URIs may be file paths,
/// <c>file://</c> URIs, <c>stdout:</c>, or <c>http(s)://</c> endpoints (uploaded with PUT when the feed
/// closes), and may contain the placeholders <c>%(name)s</c> (spider name), <c>%(time)s</c> (start time),
/// <c>%(batch_id)d</c> and <c>%(batch_time)s</c>.
/// </summary>
public sealed class FeedCollection : IEnumerable<KeyValuePair<string, FeedOptions>>
{
    private readonly Dictionary<string, FeedOptions> _feeds = new(StringComparer.Ordinal);
    private Settings _owner;

    internal FeedCollection(Settings owner) => _owner = owner;

    public int Count => _feeds.Count;

    public FeedCollection Add(string uri, FeedOptions? options = null)
    {
        _owner.EnsureMutable();
        _feeds[uri] = options ?? new FeedOptions();
        return this;
    }

    /// <summary>Adds a feed with just a format (<c>"json"</c>, <c>"csv"</c>, ...).</summary>
    public FeedCollection Add(string uri, string format, bool overwrite = false) => Add(uri, new FeedOptions { Format = format, Overwrite = overwrite });

    public bool Remove(string uri)
    {
        _owner.EnsureMutable();
        return _feeds.Remove(uri);
    }

    public void Clear()
    {
        _owner.EnsureMutable();
        _feeds.Clear();
    }

    internal void Merge(object raw)
    {
        switch (raw)
        {
            case FeedCollection other:
                foreach (var (k, v) in other._feeds) _feeds[k] = v;
                break;
            case IDictionary<string, FeedOptions> typed:
                foreach (var (k, v) in typed) _feeds[k] = v;
                break;
            case IDictionary dict:
                foreach (DictionaryEntry de in dict)
                {
                    var options = de.Value switch
                    {
                        FeedOptions fo => fo,
                        IReadOnlyDictionary<string, object?> d => FeedOptions.FromDictionary(d),
                        IDictionary<string, object?> d2 => FeedOptions.FromDictionary(d2.AsReadOnly()),
                        string format => new FeedOptions { Format = format },
                        null => new FeedOptions(),
                        _ => throw new ArgumentException($"Invalid feed options for {de.Key}"),
                    };
                    _feeds[de.Key.ToString()!] = options;
                }
                break;
            default:
                throw new ArgumentException($"Cannot merge {raw.GetType().Name} into FEEDS.");
        }
    }

    internal FeedCollection Clone(Settings owner)
    {
        var copy = new FeedCollection(owner);
        foreach (var (k, v) in _feeds) copy._feeds[k] = v;
        return copy;
    }

    public IEnumerator<KeyValuePair<string, FeedOptions>> GetEnumerator() => _feeds.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
