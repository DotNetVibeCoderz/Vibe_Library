using System.Collections;
using System.Data.Common;
using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ScrapyNet.Exporters;
using ScrapyNet.Loaders;

namespace ScrapyNet.Pipelines;

/// <summary>
/// Drops items that break rules: required fields, regex patterns, numeric ranges, or any custom check.
/// </summary>
/// <example>
/// <code>
/// settings.ItemPipelines.Add(new ValidationPipeline()
///     .Require("Title", "Price")
///     .Match("Url", "^https://")
///     .Range("Price", min: 0)
///     .Rule("in stock", item => ItemAdapter.For(item).Get&lt;bool&gt;("InStock")), 100);
/// </code>
/// </example>
public sealed class ValidationPipeline : ItemPipeline
{
    private readonly List<(string Name, Func<object, bool> Check)> _rules = [];

    /// <summary>Fields that must be present and non-empty.</summary>
    public ValidationPipeline Require(params string[] fields)
    {
        foreach (var field in fields)
            _rules.Add(($"missing {field}", item => ItemAdapter.For(item).Get(field) switch
            {
                null => false,
                string s => !string.IsNullOrWhiteSpace(s),
                ICollection c => c.Count > 0,
                _ => true,
            }));
        return this;
    }

    public ValidationPipeline Match(string field, string pattern)
    {
        var regex = RegexExtractor.GetRegex(pattern);
        _rules.Add(($"{field} does not match {pattern}", item => ItemAdapter.For(item).Get(field) is { } v && regex.IsMatch(Convert.ToString(v, CultureInfo.InvariantCulture)!)));
        return this;
    }

    public ValidationPipeline Range(string field, double? min = null, double? max = null)
    {
        _rules.Add(($"{field} out of range", item =>
        {
            var v = ItemAdapter.For(item).Get(field);
            if (v is null) return false;
            double d;
            try
            {
                d = Convert.ToDouble(v, CultureInfo.InvariantCulture);
            }
            catch (Exception ex) when (ex is FormatException or InvalidCastException)
            {
                return false;
            }
            return (min is null || d >= min) && (max is null || d <= max);
        }));
        return this;
    }

    public ValidationPipeline Rule(string name, Func<object, bool> check)
    {
        _rules.Add((name, check));
        return this;
    }

    public override ValueTask<object> ProcessItemAsync(object item, Spider spider)
    {
        foreach (var (name, check) in _rules)
            if (!check(item)) throw new DropItemException($"Validation failed: {name}");
        return ValueTask.FromResult(item);
    }
}

/// <summary>
/// Normalizes every string field (whitespace collapsed, entities decoded, tags stripped) and applies
/// per-field transforms such as <see cref="Transforms.Price"/> or type conversion.
/// </summary>
public sealed class CleaningPipeline : ItemPipeline
{
    private readonly Dictionary<string, Func<object?, object?>> _fieldTransforms = new(StringComparer.Ordinal);

    public bool NormalizeStrings { get; init; } = true;

    public bool StripTags { get; init; }

    /// <summary>Adds a transform for one field (applied after string normalization).</summary>
    public CleaningPipeline Field(string name, Func<object?, object?> transform)
    {
        _fieldTransforms[name] = transform;
        return this;
    }

    public override ValueTask<object> ProcessItemAsync(object item, Spider spider)
    {
        var adapter = ItemAdapter.For(item);
        foreach (var name in adapter.FieldNames.ToList())
        {
            if (!adapter.TryGet(name, out var value)) continue;
            var original = value;
            if (NormalizeStrings) value = Normalize(value);
            if (_fieldTransforms.TryGetValue(name, out var transform)) value = transform(value);
            if (!Equals(value, original))
            {
                try
                {
                    adapter.Set(name, value);
                }
                catch (InvalidOperationException)
                {
                    // Read-only property (e.g. an anonymous type): leave it.
                }
            }
        }
        return ValueTask.FromResult(item);
    }

    private object? Normalize(object? value) => value switch
    {
        string s => TextUtils.NormalizeWhitespace(TextUtils.DecodeEntities(StripTags ? TextUtils.RemoveTags(s) : s)),
        List<string> list => list.Select(x => (string)Normalize(x)!).ToList(),
        string[] array => array.Select(x => (string)Normalize(x)!).ToArray(),
        _ => value,
    };
}

/// <summary>
/// Drops items already seen, by the given key fields (e.g. a URL or SKU) or by a hash of all fields.
/// Keys are kept as 128-bit hashes so millions of items stay cheap.
/// </summary>
public sealed class DuplicatesPipeline(params string[] keyFields) : ItemPipeline
{
    private readonly HashSet<UInt128> _seen = [];

    public int SeenCount
    {
        get
        {
            lock (_seen) return _seen.Count;
        }
    }

    public override ValueTask<object> ProcessItemAsync(object item, Spider spider)
    {
        var adapter = ItemAdapter.For(item);
        var sb = new StringBuilder();
        IEnumerable<string> fields = keyFields.Length > 0 ? keyFields : adapter.FieldNames;
        foreach (var f in fields) sb.Append(f).Append('=').Append(ItemAdapter.FormatValue(adapter.Get(f))).Append('\u001f');
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()), hash);
        var key = new UInt128(BitConverter.ToUInt64(hash), BitConverter.ToUInt64(hash[8..]));
        lock (_seen)
        {
            if (!_seen.Add(key))
                throw new DropItemException($"Duplicate item found: {(keyFields.Length > 0 ? string.Join(", ", keyFields.Select(k => $"{k}={adapter.Get(k)}")) : "all fields")}");
        }
        return ValueTask.FromResult(item);
    }
}

/// <summary>
/// Inserts items into any ADO.NET database — SQL Server, PostgreSQL (Npgsql), MySQL (MySqlConnector),
/// SQLite (Microsoft.Data.Sqlite), Oracle — in batched transactions. Columns are the item's fields.
/// </summary>
/// <example>
/// <code>
/// settings.ItemPipelines.Add(new AdoNetPipeline(() => new SqliteConnection("Data Source=books.db"), "books")
/// {
///     CreateTableSql = "CREATE TABLE IF NOT EXISTS books (Title TEXT, Price REAL, Url TEXT PRIMARY KEY)",
/// }, 800);
/// </code>
/// </example>
public sealed class AdoNetPipeline(Func<DbConnection> connectionFactory, string table) : ItemPipeline
{
    private readonly List<Dictionary<string, object?>> _buffer = [];
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DbConnection? _connection;

    /// <summary>Rows per transaction.</summary>
    public int BatchSize { get; init; } = 100;

    /// <summary>Optional DDL run once when the spider opens.</summary>
    public string? CreateTableSql { get; init; }

    /// <summary>Only these columns (default: every field of each item).</summary>
    public IReadOnlyList<string>? Columns { get; init; }

    /// <summary>Statement prefix, e.g. <c>INSERT OR REPLACE INTO</c> for SQLite upserts.</summary>
    public string InsertVerb { get; init; } = "INSERT INTO";

    /// <summary>Rows written so far.</summary>
    public long Written { get; private set; }

    public override async ValueTask OpenSpiderAsync(Spider spider)
    {
        _connection = connectionFactory();
        await _connection.OpenAsync().ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(CreateTableSql))
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = CreateTableSql;
            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
    }

    public override async ValueTask<object> ProcessItemAsync(object item, Spider spider)
    {
        var row = ItemAdapter.For(item).AsDictionary();
        if (Columns is not null) row = Columns.ToDictionary(c => c, c => row.GetValueOrDefault(c), StringComparer.Ordinal);
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _buffer.Add(row);
            if (_buffer.Count >= BatchSize) await FlushAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
        return item;
    }

    public override async ValueTask CloseSpiderAsync(Spider spider)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await FlushAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
        if (_connection is not null) await _connection.DisposeAsync().ConfigureAwait(false);
    }

    private async Task FlushAsync()
    {
        if (_buffer.Count == 0 || _connection is null) return;
        await using var tx = await _connection.BeginTransactionAsync().ConfigureAwait(false);
        foreach (var row in _buffer)
        {
            await using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            var cols = row.Keys.ToList();
            cmd.CommandText = $"{InsertVerb} {table} ({string.Join(", ", cols)}) VALUES ({string.Join(", ", cols.Select((_, i) => "@p" + i))})";
            for (var i = 0; i < cols.Count; i++)
            {
                var p = cmd.CreateParameter();
                p.ParameterName = "@p" + i;
                p.Value = ToDbValue(row[cols[i]]);
                cmd.Parameters.Add(p);
            }
            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
        await tx.CommitAsync().ConfigureAwait(false);
        Written += _buffer.Count;
        _buffer.Clear();
    }

    private static object ToDbValue(object? value) => value switch
    {
        null => DBNull.Value,
        string or int or long or double or decimal or bool or DateTime or DateTimeOffset or Guid or byte[] or float or short => value,
        _ => JsonSerializer.Serialize(value, value.GetType(), ItemExporter.JsonOptions),
    };
}

/// <summary>
/// Forwards items to a REST endpoint as JSON — one POST per batch (a JSON array) or per item. Covers
/// webhooks, ingestion APIs and serverless functions.
/// </summary>
public sealed class HttpApiPipeline(string endpoint) : ItemPipeline
{
    private readonly List<object> _buffer = [];
    private readonly SemaphoreSlim _gate = new(1, 1);
    private HttpClient? _client;

    /// <summary>1 = one request per item; more = JSON arrays of that size.</summary>
    public int BatchSize { get; init; } = 50;

    public IReadOnlyDictionary<string, string>? Headers { get; init; }

    public HttpMethod Method { get; init; } = HttpMethod.Post;

    public ILogger? Logger { get; init; }

    public long Sent { get; private set; }

    public override ValueTask OpenSpiderAsync(Spider spider)
    {
        _client = new HttpClient();
        if (Headers is not null)
            foreach (var (k, v) in Headers) _client.DefaultRequestHeaders.TryAddWithoutValidation(k, v);
        return ValueTask.CompletedTask;
    }

    public override async ValueTask<object> ProcessItemAsync(object item, Spider spider)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _buffer.Add(item);
            if (_buffer.Count >= BatchSize) await FlushAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
        return item;
    }

    public override async ValueTask CloseSpiderAsync(Spider spider)
    {
        await FlushAsync().ConfigureAwait(false);
        _client?.Dispose();
    }

    private async Task FlushAsync()
    {
        if (_buffer.Count == 0 || _client is null) return;
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            if (BatchSize == 1) ItemExporter.WriteJsonValue(writer, _buffer[0]);
            else ItemExporter.WriteJsonValue(writer, _buffer);
        }
        var content = new ByteArrayContent(stream.ToArray());
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using var response = await _client.SendAsync(new HttpRequestMessage(Method, endpoint) { Content = content }).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            Logger?.LogError("API {Endpoint} rejected {Count} items: {Status}", endpoint, _buffer.Count, (int)response.StatusCode);
        else Sent += _buffer.Count;
        _buffer.Clear();
    }
}

/// <summary>
/// Indexes items into Elasticsearch or OpenSearch through the <c>_bulk</c> API (no client library
/// needed). Documents get ids from <see cref="IdField"/> when set, so re-crawls update in place.
/// </summary>
public sealed class ElasticsearchPipeline(string baseUrl, string index) : ItemPipeline
{
    private readonly List<object> _buffer = [];
    private readonly SemaphoreSlim _gate = new(1, 1);
    private HttpClient? _client;

    public int BatchSize { get; init; } = 500;

    public string? IdField { get; init; }

    /// <summary>API key (sent as <c>Authorization: ApiKey ...</c>).</summary>
    public string? ApiKey { get; init; }

    public long Indexed { get; private set; }

    public override ValueTask OpenSpiderAsync(Spider spider)
    {
        _client = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/") };
        if (ApiKey is not null) _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("ApiKey", ApiKey);
        return ValueTask.CompletedTask;
    }

    public override async ValueTask<object> ProcessItemAsync(object item, Spider spider)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _buffer.Add(item);
            if (_buffer.Count >= BatchSize) await FlushAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
        return item;
    }

    public override async ValueTask CloseSpiderAsync(Spider spider)
    {
        await FlushAsync().ConfigureAwait(false);
        _client?.Dispose();
    }

    /// <summary>The NDJSON body of a bulk request (public for testing and inspection).</summary>
    public string BuildBulkBody(IEnumerable<object> items)
    {
        var sb = new StringBuilder();
        foreach (var item in items)
        {
            var id = IdField is null ? null : ItemAdapter.For(item).Get(IdField)?.ToString();
            sb.Append(id is null
                ? $"{{\"index\":{{\"_index\":{JsonSerializer.Serialize(index, ScrapyJson.Default)}}}}}"
                : $"{{\"index\":{{\"_index\":{JsonSerializer.Serialize(index, ScrapyJson.Default)},\"_id\":{JsonSerializer.Serialize(id, ScrapyJson.Default)}}}}}").Append('\n');
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = ItemExporter.JsonOptions.Encoder }))
                ItemExporter.WriteJsonValue(writer, item);
            sb.Append(Encoding.UTF8.GetString(stream.ToArray())).Append('\n');
        }
        return sb.ToString();
    }

    private async Task FlushAsync()
    {
        if (_buffer.Count == 0 || _client is null) return;
        var content = new StringContent(BuildBulkBody(_buffer), Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/x-ndjson");
        using var response = await _client.PostAsync("_bulk", content).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        Indexed += _buffer.Count;
        _buffer.Clear();
    }
}
