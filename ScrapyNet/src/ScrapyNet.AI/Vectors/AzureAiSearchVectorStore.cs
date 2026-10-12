using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ScrapyNet.AI;

/// <summary>
/// <see cref="IVectorStore"/> on Azure AI Search through its REST API (no SDK). The index is created on
/// first use with an HNSW cosine profile. Document keys only allow URL-safe characters, so record ids are
/// stored base64url-encoded as the key and verbatim in <c>recordId</c>.
/// </summary>
public sealed class AzureAiSearchVectorStore : IVectorStore, IDisposable
{
    /// <summary>The REST API version used for every call.</summary>
    public const string ApiVersion = "2024-07-01";

    private readonly HttpClient _http;
    private readonly string _index;
    private readonly int _dimensions;
    private bool _ensured;

    /// <param name="endpoint"><c>https://NAME.search.windows.net</c></param>
    /// <param name="apiKey">An admin key (index creation and writes need one).</param>
    public AzureAiSearchVectorStore(string endpoint, string index, int dimensions, string apiKey, HttpMessageHandler? handler = null)
    {
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.BaseAddress = new Uri(endpoint.TrimEnd('/') + "/");
        _http.DefaultRequestHeaders.Add("api-key", apiKey);
        _index = index;
        _dimensions = dimensions;
    }

    private string Url(string path) => $"indexes/{_index}{path}?api-version={ApiVersion}";

    private async Task EnsureIndexAsync(CancellationToken ct)
    {
        if (_ensured) return;
        using var check = await _http.GetAsync(Url(""), ct).ConfigureAwait(false);
        if (!check.IsSuccessStatusCode)
        {
            var definition = new JsonObject
            {
                ["name"] = _index,
                ["fields"] = new JsonArray(
                    new JsonObject { ["name"] = "id", ["type"] = "Edm.String", ["key"] = true, ["filterable"] = true },
                    new JsonObject { ["name"] = "recordId", ["type"] = "Edm.String", ["filterable"] = true },
                    new JsonObject { ["name"] = "text", ["type"] = "Edm.String", ["searchable"] = true },
                    new JsonObject { ["name"] = "metadata", ["type"] = "Edm.String" },
                    new JsonObject
                    {
                        ["name"] = "embedding", ["type"] = "Collection(Edm.Single)", ["searchable"] = true, ["retrievable"] = false,
                        ["dimensions"] = _dimensions, ["vectorSearchProfile"] = "scrapynet-profile",
                    }),
                ["vectorSearch"] = new JsonObject
                {
                    ["algorithms"] = new JsonArray(new JsonObject { ["name"] = "scrapynet-hnsw", ["kind"] = "hnsw", ["hnswParameters"] = new JsonObject { ["metric"] = "cosine" } }),
                    ["profiles"] = new JsonArray(new JsonObject { ["name"] = "scrapynet-profile", ["algorithm"] = "scrapynet-hnsw" }),
                },
            };
            using var create = await _http.PutAsync(Url(""), Json(definition), ct).ConfigureAwait(false);
            await EnsureSuccessAsync(create, ct).ConfigureAwait(false);
        }
        _ensured = true;
    }

    public async Task UpsertAsync(IReadOnlyList<VectorRecord> records, CancellationToken cancellationToken = default)
    {
        await EnsureIndexAsync(cancellationToken).ConfigureAwait(false);
        // The service accepts up to 1000 actions per batch.
        foreach (var batch in records.Chunk(1000))
        {
            var actions = new JsonArray();
            foreach (var r in batch)
            {
                actions.Add(new JsonObject
                {
                    ["@search.action"] = "mergeOrUpload",
                    ["id"] = ToKey(r.Id),
                    ["recordId"] = r.Id,
                    ["text"] = r.Text,
                    ["metadata"] = JsonSerializer.Serialize(r.Metadata, ScrapyJson.Default),
                    ["embedding"] = new JsonArray([.. r.Vector.Select(v => (JsonNode)JsonValue.Create(v))]),
                });
            }
            using var response = await _http.PostAsync(Url("/docs/index"), Json(new JsonObject { ["value"] = actions }), cancellationToken).ConfigureAwait(false);
            await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<VectorMatch>> SearchAsync(float[] query, int top = 5, Func<VectorRecord, bool>? filter = null, CancellationToken cancellationToken = default)
    {
        await EnsureIndexAsync(cancellationToken).ConfigureAwait(false);
        var k = filter is null ? top : top * 4;
        var body = new JsonObject
        {
            ["select"] = "recordId,text,metadata",
            ["top"] = k,
            ["vectorQueries"] = new JsonArray(new JsonObject
            {
                ["kind"] = "vector",
                ["vector"] = new JsonArray([.. query.Select(v => (JsonNode)JsonValue.Create(v))]),
                ["fields"] = "embedding",
                ["k"] = k,
            }),
        };
        using var response = await _http.PostAsync(Url("/docs/search"), Json(body), cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false))!;
        var matches = new List<VectorMatch>(top);
        foreach (var hit in json["value"]!.AsArray())
        {
            var metadata = hit!["metadata"]?.GetValue<string>() is { Length: > 0 } m
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(m, ScrapyJson.Default) ?? []
                : new Dictionary<string, string>();
            var record = new VectorRecord(hit["recordId"]!.GetValue<string>(), [], hit["text"]?.GetValue<string>() ?? "", metadata);
            if (filter is null || filter(record)) matches.Add(new VectorMatch(record, hit["@search.score"]!.GetValue<float>()));
            if (matches.Count == top) break;
        }
        return matches;
    }

    public async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        await EnsureIndexAsync(cancellationToken).ConfigureAwait(false);
        using var response = await _http.GetAsync(Url("/docs/$count"), cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        var text = (await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)).Trim().TrimStart('﻿');
        return int.Parse(text, CultureInfo.InvariantCulture);
    }

    public async Task DeleteAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default)
    {
        foreach (var batch in ids.Chunk(1000))
        {
            var actions = new JsonArray([.. batch.Select(id => (JsonNode)new JsonObject { ["@search.action"] = "delete", ["id"] = ToKey(id) })]);
            using var response = await _http.PostAsync(Url("/docs/index"), Json(new JsonObject { ["value"] = actions }), cancellationToken).ConfigureAwait(false);
            await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Document keys allow letters, digits, <c>_</c>, <c>-</c> and <c>=</c>: base64url fits.</summary>
    internal static string ToKey(string id) => Convert.ToBase64String(Encoding.UTF8.GetBytes(id)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static StringContent Json(JsonNode node) => new(node.ToJsonString(), Encoding.UTF8, "application/json");

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        var detail = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        throw new HttpRequestException($"Azure AI Search returned {(int)response.StatusCode}: {detail}", null, response.StatusCode);
    }

    public void Dispose() => _http.Dispose();
}
