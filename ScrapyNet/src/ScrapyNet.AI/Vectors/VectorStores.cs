using System.Net.Http.Headers;
using System.Numerics.Tensors;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ScrapyNet.AI;

/// <summary>A stored vector with its text and metadata.</summary>
public sealed record VectorRecord(string Id, float[] Vector, string Text, IReadOnlyDictionary<string, string> Metadata);

/// <summary>A search hit.</summary>
public sealed record VectorMatch(VectorRecord Record, float Score);

/// <summary>Stores embeddings and finds the nearest ones.</summary>
public interface IVectorStore
{
    Task UpsertAsync(IReadOnlyList<VectorRecord> records, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<VectorMatch>> SearchAsync(float[] query, int top = 5, Func<VectorRecord, bool>? filter = null, CancellationToken cancellationToken = default);

    Task<int> CountAsync(CancellationToken cancellationToken = default);

    Task DeleteAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default);
}

/// <summary>
/// In-memory vector store with SIMD cosine similarity (<see cref="TensorPrimitives"/>) and a bounded
/// min-heap for top-k, so a search costs one pass and O(k) memory. Persist with
/// <see cref="SaveAsync"/>/<see cref="LoadAsync"/>. Suits up to a few hundred thousand chunks.
/// </summary>
public sealed class InMemoryVectorStore : IVectorStore
{
    private readonly Dictionary<string, VectorRecord> _records = new(StringComparer.Ordinal);
    private readonly ReaderWriterLockSlim _lock = new();

    public IReadOnlyCollection<VectorRecord> Records
    {
        get
        {
            _lock.EnterReadLock();
            try
            {
                return [.. _records.Values];
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }
    }

    public Task UpsertAsync(IReadOnlyList<VectorRecord> records, CancellationToken cancellationToken = default)
    {
        _lock.EnterWriteLock();
        try
        {
            foreach (var r in records) _records[r.Id] = r;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<VectorMatch>> SearchAsync(float[] query, int top = 5, Func<VectorRecord, bool>? filter = null, CancellationToken cancellationToken = default)
    {
        var heap = new PriorityQueue<VectorRecord, float>(top + 1);
        _lock.EnterReadLock();
        try
        {
            foreach (var record in _records.Values)
            {
                if (filter is not null && !filter(record)) continue;
                if (record.Vector.Length != query.Length) continue;
                var score = TensorPrimitives.CosineSimilarity(query, record.Vector);
                if (heap.Count < top) heap.Enqueue(record, score);
                else if (heap.TryPeek(out _, out var min) && score > min)
                {
                    heap.Dequeue();
                    heap.Enqueue(record, score);
                }
            }
        }
        finally
        {
            _lock.ExitReadLock();
        }
        var results = new List<VectorMatch>(heap.Count);
        while (heap.TryDequeue(out var r, out var s)) results.Add(new VectorMatch(r, s));
        results.Reverse();
        return Task.FromResult<IReadOnlyList<VectorMatch>>(results);
    }

    public Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        _lock.EnterReadLock();
        try
        {
            return Task.FromResult(_records.Count);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public Task DeleteAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default)
    {
        _lock.EnterWriteLock();
        try
        {
            foreach (var id in ids) _records.Remove(id);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
        return Task.CompletedTask;
    }

    /// <summary>Writes the store as JSON Lines (vectors base64-encoded as little-endian floats).</summary>
    public async Task SaveAsync(string path, CancellationToken cancellationToken = default)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        await using var writer = new StreamWriter(path, append: false, Encoding.UTF8);
        foreach (var r in Records)
        {
            var bytes = new byte[r.Vector.Length * sizeof(float)];
            Buffer.BlockCopy(r.Vector, 0, bytes, 0, bytes.Length);
            var line = JsonSerializer.Serialize(new { id = r.Id, text = r.Text, metadata = r.Metadata, vector = Convert.ToBase64String(bytes) }, ScrapyJson.Default);
            await writer.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
        }
    }

    public static async Task<InMemoryVectorStore> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        var store = new InMemoryVectorStore();
        var records = new List<VectorRecord>();
        await foreach (var line in File.ReadLinesAsync(path, cancellationToken).ConfigureAwait(false))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var node = JsonNode.Parse(line)!;
            var bytes = Convert.FromBase64String((string)node["vector"]!);
            var vector = new float[bytes.Length / sizeof(float)];
            Buffer.BlockCopy(bytes, 0, vector, 0, bytes.Length);
            var metadata = node["metadata"]!.AsObject().ToDictionary(kv => kv.Key, kv => (string?)kv.Value ?? "");
            records.Add(new VectorRecord((string)node["id"]!, vector, (string)node["text"]!, metadata));
        }
        await store.UpsertAsync(records, cancellationToken).ConfigureAwait(false);
        return store;
    }
}

/// <summary>
/// <see cref="IVectorStore"/> on a Qdrant server through its REST API (no client library). The
/// collection is created on first use with cosine distance.
/// </summary>
public sealed class QdrantVectorStore : IVectorStore, IDisposable
{
    private readonly HttpClient _http;
    private readonly string _collection;
    private readonly int _dimensions;
    private bool _ensured;

    public QdrantVectorStore(string baseUrl, string collection, int dimensions, string? apiKey = null, HttpMessageHandler? handler = null)
    {
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        if (apiKey is not null) _http.DefaultRequestHeaders.Add("api-key", apiKey);
        _collection = collection;
        _dimensions = dimensions;
    }

    private async Task EnsureCollectionAsync(CancellationToken ct)
    {
        if (_ensured) return;
        using var check = await _http.GetAsync($"collections/{_collection}", ct).ConfigureAwait(false);
        if (!check.IsSuccessStatusCode)
        {
            var body = JsonSerializer.Serialize(new { vectors = new { size = _dimensions, distance = "Cosine" } }, ScrapyJson.Default);
            using var create = await _http.PutAsync($"collections/{_collection}", new StringContent(body, Encoding.UTF8, "application/json"), ct).ConfigureAwait(false);
            create.EnsureSuccessStatusCode();
        }
        _ensured = true;
    }

    public async Task UpsertAsync(IReadOnlyList<VectorRecord> records, CancellationToken cancellationToken = default)
    {
        await EnsureCollectionAsync(cancellationToken).ConfigureAwait(false);
        var points = records.Select(r => new
        {
            id = ToUuid(r.Id),
            vector = r.Vector,
            payload = new Dictionary<string, object>(r.Metadata.ToDictionary(kv => kv.Key, kv => (object)kv.Value)) { ["_id"] = r.Id, ["_text"] = r.Text },
        });
        var body = JsonSerializer.Serialize(new { points }, ScrapyJson.Default);
        using var response = await _http.PutAsync($"collections/{_collection}/points?wait=true", new StringContent(body, Encoding.UTF8, "application/json"), cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<VectorMatch>> SearchAsync(float[] query, int top = 5, Func<VectorRecord, bool>? filter = null, CancellationToken cancellationToken = default)
    {
        await EnsureCollectionAsync(cancellationToken).ConfigureAwait(false);
        var limit = filter is null ? top : top * 4;
        var body = JsonSerializer.Serialize(new { vector = query, limit, with_payload = true }, ScrapyJson.Default);
        using var response = await _http.PostAsync($"collections/{_collection}/points/search", new StringContent(body, Encoding.UTF8, "application/json"), cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false))!;
        var matches = new List<VectorMatch>();
        foreach (var hit in json["result"]!.AsArray())
        {
            var payload = hit!["payload"]!.AsObject();
            var metadata = payload.Where(kv => !kv.Key.StartsWith('_')).ToDictionary(kv => kv.Key, kv => kv.Value?.ToString() ?? "");
            var record = new VectorRecord((string)payload["_id"]!, [], (string)payload["_text"]!, metadata);
            if (filter is null || filter(record)) matches.Add(new VectorMatch(record, (float)hit["score"]!));
            if (matches.Count == top) break;
        }
        return matches;
    }

    public async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        await EnsureCollectionAsync(cancellationToken).ConfigureAwait(false);
        using var response = await _http.PostAsync($"collections/{_collection}/points/count", new StringContent("{\"exact\":true}", Encoding.UTF8, "application/json"), cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return (int)JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false))!["result"]!["count"]!;
    }

    public async Task DeleteAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default)
    {
        var body = JsonSerializer.Serialize(new { points = ids.Select(ToUuid) }, ScrapyJson.Default);
        using var response = await _http.PostAsync($"collections/{_collection}/points/delete?wait=true", new StringContent(body, Encoding.UTF8, "application/json"), cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>Qdrant ids must be integers or UUIDs; string ids map to a deterministic UUID.</summary>
    internal static string ToUuid(string id)
    {
        var hash = System.Security.Cryptography.MD5.HashData(Encoding.UTF8.GetBytes(id));
        return new Guid(hash).ToString();
    }

    public void Dispose() => _http.Dispose();
}
