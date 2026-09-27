using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ScrapyNet.Engine;

/// <summary>
/// Turns requests into JSON and back, for pausing a crawl to disk (<c>JOBDIR</c>). Callbacks are stored by
/// spider method name, which is why only named spider methods survive a pause.
/// </summary>
public static class RequestSerializer
{
    /// <summary>Serializes a request, or returns <c>null</c> when its callback cannot be named.</summary>
    public static JsonObject? ToJson(Request request)
    {
        if (request.Callback is not null && request.CallbackName is null) return null;
        if (request.Errback is not null && request.ErrbackName is null) return null;

        var obj = new JsonObject
        {
            ["url"] = request.Url,
            ["method"] = request.Method,
            ["priority"] = request.Priority,
            ["dont_filter"] = request.DontFilter,
            ["encoding"] = request.Encoding.WebName,
        };
        if (request.CallbackName is { } cb) obj["callback"] = cb;
        if (request.ErrbackName is { } eb) obj["errback"] = eb;
        if (request.Body.Length > 0) obj["body"] = Convert.ToBase64String(request.Body);
        if (request.Headers.Count > 0)
        {
            var headers = new JsonObject();
            foreach (var (name, values) in request.Headers) headers[name] = new JsonArray([.. values.Select(v => (JsonNode)JsonValue.Create(v)!)]);
            obj["headers"] = headers;
        }
        if (request.HasCookies) obj["cookies"] = JsonSerializer.SerializeToNode(request.Cookies, ScrapyJson.Default);
        if (request.HasMeta && ToNode(request.Meta) is { } meta) obj["meta"] = meta;
        if (request.HasCbKwargs && ToNode(request.CbKwargs) is { } kw) obj["cb_kwargs"] = kw;
        if (request.Flags.Count > 0) obj["flags"] = JsonSerializer.SerializeToNode(request.Flags, ScrapyJson.Default);
        return obj;
    }

    public static Request FromJson(JsonObject obj, Spider spider)
    {
        var request = new Request(obj["url"]!.GetValue<string>())
        {
            Method = obj["method"]?.GetValue<string>() ?? "GET",
            Priority = obj["priority"]?.GetValue<int>() ?? 0,
            DontFilter = obj["dont_filter"]?.GetValue<bool>() ?? false,
            Encoding = EncodingDetector.GetEncoding(obj["encoding"]?.GetValue<string>() ?? "utf-8") ?? Encoding.UTF8,
            Body = obj["body"] is { } b ? Convert.FromBase64String(b.GetValue<string>()) : [],
            Callback = Callbacks.Resolve<Response>(spider, obj["callback"]?.GetValue<string>()),
            Errback = Callbacks.Resolve<Failure>(spider, obj["errback"]?.GetValue<string>()),
        };
        if (obj["headers"] is JsonObject headers)
            foreach (var (name, values) in headers)
                foreach (var v in values!.AsArray()) request.Headers.Add(name, v!.GetValue<string>());
        if (obj["cookies"] is JsonObject cookies)
            foreach (var (k, v) in cookies) request.Cookies[k] = v!.GetValue<string>();
        if (obj["meta"] is JsonObject meta)
            foreach (var (k, v) in meta) request.Meta[k] = FromNode(v);
        if (obj["cb_kwargs"] is JsonObject kw)
            foreach (var (k, v) in kw) request.CbKwargs[k] = FromNode(v);
        if (obj["flags"] is JsonArray flags)
            foreach (var f in flags) request.Flags.Add(f!.GetValue<string>());
        return request;
    }

    /// <summary>Serializes the JSON-representable entries of a dictionary; others are skipped.</summary>
    private static JsonObject? ToNode(Dictionary<string, object?> values)
    {
        var obj = new JsonObject();
        foreach (var (k, v) in values)
        {
            if (k.StartsWith('_')) continue;
            try
            {
                obj[k] = v is null ? null : JsonSerializer.SerializeToNode(v, v.GetType(), ScrapyJson.Default);
            }
            catch (Exception ex) when (ex is NotSupportedException or JsonException or InvalidOperationException)
            {
                // Live objects (sockets, pages, delegates) cannot be persisted; they are dropped.
            }
        }
        return obj.Count == 0 ? null : obj;
    }

    internal static object? FromNode(JsonNode? node) => node is null ? null : Settings.FromJson(JsonDocument.Parse(node.ToJsonString()).RootElement);
}
