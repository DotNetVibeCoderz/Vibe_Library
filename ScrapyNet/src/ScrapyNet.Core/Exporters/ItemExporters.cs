using System.Collections;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Xml;

namespace ScrapyNet.Exporters;

/// <summary>
/// Writes items to a stream in one format. Port of Scrapy's <c>BaseItemExporter</c>:
/// <see cref="StartExporting"/>, then <see cref="ExportItem"/> per item, then <see cref="FinishExporting"/>.
/// </summary>
public abstract class ItemExporter
{
    protected ItemExporter(Stream stream, FeedOptions? options = null)
    {
        Stream = stream;
        Options = options ?? new FeedOptions();
        Encoding = Options.Encoding ?? new UTF8Encoding(false);
    }

    protected Stream Stream { get; }

    protected FeedOptions Options { get; }

    protected Encoding Encoding { get; }

    public virtual void StartExporting() { }

    public abstract void ExportItem(object item);

    public virtual void FinishExporting() { }

    /// <summary>The (field, value) pairs to export: <see cref="FeedOptions.Fields"/> order when set, with field serializers applied.</summary>
    protected IEnumerable<KeyValuePair<string, object?>> GetFields(object item)
    {
        var adapter = ItemAdapter.For(item);
        var names = Options.Fields ?? adapter.FieldNames;
        foreach (var name in names)
        {
            if (!adapter.TryGet(name, out var value) && Options.Fields is null) continue;
            var serializer = adapter.GetFieldMeta(name)?.Serializer;
            yield return new KeyValuePair<string, object?>(name, serializer is null ? value : serializer(value));
        }
    }

    /// <summary>Creates the exporter for a format name.</summary>
    public static ItemExporter Create(string format, Stream stream, FeedOptions options) => format.ToLowerInvariant() switch
    {
        "json" => new JsonItemExporter(stream, options),
        "jsonlines" or "jsonl" or "jl" or "ndjson" => new JsonLinesItemExporter(stream, options),
        "csv" => new CsvItemExporter(stream, options),
        "tsv" => new CsvItemExporter(stream, options with { CsvDelimiter = '\t' }),
        "xml" => new XmlItemExporter(stream, options),
        _ => throw new NotSupportedException($"Unknown feed format '{format}'. Use json, jsonlines, csv or xml."),
    };

    /// <summary>Guesses a format from a file extension (<c>.json</c>, <c>.jsonl</c>, <c>.jl</c>, <c>.csv</c>, <c>.xml</c>).</summary>
    public static string? FormatFromUri(string uri)
    {
        var path = uri;
        if (path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)) path = path[..^3];
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".json" => "json",
            ".jsonl" or ".jl" or ".ndjson" => "jsonlines",
            ".csv" => "csv",
            ".tsv" => "tsv",
            ".xml" => "xml",
            _ => null,
        };
    }

    internal static readonly JsonSerializerOptions JsonOptions = ScrapyJson.Default;

    /// <summary>Writes a value as JSON, recursing into items so nested items serialize by their fields.</summary>
    internal static void WriteJsonValue(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                break;
            case string s:
                writer.WriteStringValue(s);
                break;
            // Primitives are written directly rather than through JsonSerializer: faster, and it keeps
            // exports working where reflection-based serialization is disabled (AOT, trimmed and
            // .NET 10 file-based apps).
            case bool b:
                writer.WriteBooleanValue(b);
                break;
            case int i:
                writer.WriteNumberValue(i);
                break;
            case long l:
                writer.WriteNumberValue(l);
                break;
            case decimal m:
                writer.WriteNumberValue(m);
                break;
            case double d when double.IsFinite(d):
                writer.WriteNumberValue(d);
                break;
            case float f when float.IsFinite(f):
                writer.WriteNumberValue(f);
                break;
            case double or float:
                writer.WriteNullValue();
                break;
            case short or byte or sbyte or ushort or uint:
                writer.WriteNumberValue(Convert.ToInt64(value, CultureInfo.InvariantCulture));
                break;
            case ulong ul:
                writer.WriteNumberValue(ul);
                break;
            case DateTime dt:
                writer.WriteStringValue(dt);
                break;
            case DateTimeOffset dto:
                writer.WriteStringValue(dto);
                break;
            case Guid g:
                writer.WriteStringValue(g);
                break;
            case DateOnly date:
                writer.WriteStringValue(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                break;
            case TimeOnly time:
                writer.WriteStringValue(time.ToString("HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture));
                break;
            case TimeSpan span:
                writer.WriteStringValue(span.ToString("c", CultureInfo.InvariantCulture));
                break;
            case Uri uri:
                writer.WriteStringValue(uri.OriginalString);
                break;
            case Enum e:
                writer.WriteStringValue(e.ToString());
                break;
            case char c:
                writer.WriteStringValue(c.ToString());
                break;
            case JsonElement element:
                element.WriteTo(writer);
                break;
            case System.Text.Json.Nodes.JsonNode node:
                node.WriteTo(writer);
                break;
            case JsonDocument document:
                document.RootElement.WriteTo(writer);
                break;
            case byte[] bytes:
                writer.WriteBase64StringValue(bytes);
                break;
            case IDictionary dict:
                writer.WriteStartObject();
                foreach (DictionaryEntry e in dict)
                {
                    writer.WritePropertyName(Convert.ToString(e.Key, CultureInfo.InvariantCulture)!);
                    WriteJsonValue(writer, e.Value);
                }
                writer.WriteEndObject();
                break;
            case IEnumerable enumerable:
                writer.WriteStartArray();
                foreach (var v in enumerable) WriteJsonValue(writer, v);
                writer.WriteEndArray();
                break;
            default:
                writer.WriteStartObject();
                foreach (var (k, v) in ItemAdapter.For(value).AsDictionary())
                {
                    writer.WritePropertyName(k);
                    WriteJsonValue(writer, v);
                }
                writer.WriteEndObject();
                break;
        }
    }

    internal static string ToText(object? value) => value switch
    {
        null => "",
        string s => s,
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        IEnumerable e => string.Join(",", e.Cast<object?>().Select(ToText)),
        _ => value.ToString() ?? "",
    };
}

/// <summary>A JSON array of items. With <c>indent</c> 0 each item sits on its own line (as Scrapy writes it).</summary>
public sealed class JsonItemExporter(Stream stream, FeedOptions? options = null) : ItemExporter(stream, options)
{
    private bool _first = true;
    private readonly int _indent = options?.Indent ?? 0;

    public override void StartExporting() => Write("[");

    public override void ExportItem(object item)
    {
        Write(_first ? "\n" : ",\n");
        _first = false;
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = _indent > 0, Encoder = JsonOptions.Encoder }))
        {
            writer.WriteStartObject();
            foreach (var (k, v) in GetFields(item))
            {
                writer.WritePropertyName(k);
                WriteJsonValue(writer, v);
            }
            writer.WriteEndObject();
        }
        buffer.Position = 0;
        buffer.CopyTo(Stream);
    }

    public override void FinishExporting() => Write(_first ? "]" : "\n]");

    private void Write(string s) => Stream.Write(Encoding.UTF8.GetBytes(s));
}

/// <summary>One JSON object per line — streamable and appendable, the best format for large crawls.</summary>
public sealed class JsonLinesItemExporter(Stream stream, FeedOptions? options = null) : ItemExporter(stream, options)
{
    public override void ExportItem(object item)
    {
        using (var writer = new Utf8JsonWriter(Stream, new JsonWriterOptions { Encoder = JsonOptions.Encoder }))
        {
            writer.WriteStartObject();
            foreach (var (k, v) in GetFields(item))
            {
                writer.WritePropertyName(k);
                WriteJsonValue(writer, v);
            }
            writer.WriteEndObject();
        }
        Stream.WriteByte((byte)'\n');
    }
}

/// <summary>
/// CSV with a header row. The columns are <see cref="FeedOptions.Fields"/>, or the first item's fields.
/// Lists are joined with commas; values containing delimiters, quotes or newlines are quoted.
/// </summary>
public sealed class CsvItemExporter(Stream stream, FeedOptions? options = null) : ItemExporter(stream, options)
{
    private IReadOnlyList<string>? _headers;
    private StreamWriter? _writer;

    public override void StartExporting() => _writer = new StreamWriter(Stream, Encoding, 65536, leaveOpen: true) { NewLine = "\r\n" };

    public override void ExportItem(object item)
    {
        var fields = GetFields(item).ToList();
        if (_headers is null)
        {
            _headers = Options.Fields ?? [.. fields.Select(f => f.Key)];
            if (Options.IncludeHeadersLine) WriteRow(_headers);
        }
        var map = fields.ToDictionary(f => f.Key, f => f.Value, StringComparer.Ordinal);
        WriteRow(_headers.Select(h => ToText(map.GetValueOrDefault(h))));
    }

    public override void FinishExporting()
    {
        _writer?.Flush();
        _writer?.Dispose();
    }

    private void WriteRow(IEnumerable<string> values)
    {
        var first = true;
        foreach (var value in values)
        {
            if (!first) _writer!.Write(Options.CsvDelimiter);
            first = false;
            if (value.IndexOfAny([Options.CsvDelimiter, '"', '\r', '\n']) >= 0)
                _writer!.Write('"' + value.Replace("\"", "\"\"") + '"');
            else
                _writer!.Write(value);
        }
        _writer!.WriteLine();
    }
}

/// <summary>XML: <c>&lt;items&gt;&lt;item&gt;&lt;field&gt;value&lt;/field&gt;...</c>; lists become repeated <c>&lt;value&gt;</c> elements.</summary>
public sealed class XmlItemExporter(Stream stream, FeedOptions? options = null) : ItemExporter(stream, options)
{
    private XmlWriter? _writer;

    public override void StartExporting()
    {
        _writer = XmlWriter.Create(Stream, new XmlWriterSettings
        {
            Encoding = Encoding,
            Indent = (Options.Indent ?? 0) > 0,
            CloseOutput = false,
        });
        _writer.WriteStartDocument();
        _writer.WriteStartElement(Options.XmlRootElement);
    }

    public override void ExportItem(object item)
    {
        _writer!.WriteStartElement(Options.XmlItemElement);
        foreach (var (k, v) in GetFields(item)) WriteField(k, v);
        _writer.WriteEndElement();
    }

    private void WriteField(string name, object? value)
    {
        var element = XmlConvert.EncodeLocalName(name);
        _writer!.WriteStartElement(element);
        switch (value)
        {
            case null:
                break;
            case string s:
                _writer.WriteString(s);
                break;
            case IDictionary dict:
                foreach (DictionaryEntry e in dict) WriteField(e.Key.ToString()!, e.Value);
                break;
            case IEnumerable e:
                foreach (var v in e) WriteField("value", v);
                break;
            default:
                if (ItemAdapter.IsItem(value) && value is not IFormattable)
                    foreach (var (k, v) in ItemAdapter.For(value).AsDictionary()) WriteField(k, v);
                else
                    _writer.WriteString(ToText(value));
                break;
        }
        _writer.WriteEndElement();
    }

    public override void FinishExporting()
    {
        _writer!.WriteEndElement();
        _writer.WriteEndDocument();
        _writer.Flush();
        _writer.Dispose();
    }
}
