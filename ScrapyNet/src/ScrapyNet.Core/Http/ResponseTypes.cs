using System.Net;
using System.Text;

namespace ScrapyNet;

/// <summary>Picks the <see cref="Response"/> subclass for a download, like Scrapy's <c>responsetypes</c>.</summary>
public static class ResponseTypes
{
    /// <summary>Chooses the class from Content-Type, then the URL's extension, then by sniffing the body.</summary>
    public static Type FromArgs(string? contentType, string url, ReadOnlySpan<byte> body)
    {
        var fromHeader = FromContentType(contentType);
        if (fromHeader is not null && fromHeader != typeof(Response)) return fromHeader;
        var fromUrl = FromUrl(url);
        if (fromUrl is not null) return fromUrl;
        return FromBody(body) ?? fromHeader ?? typeof(Response);
    }

    public static Type? FromContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType)) return null;
        var mime = contentType.Split(';', 2)[0].Trim().ToLowerInvariant();
        return mime switch
        {
            "text/html" or "application/xhtml+xml" => typeof(HtmlResponse),
            "application/xml" or "text/xml" or "application/rss+xml" or "application/atom+xml" or "application/rdf+xml" => typeof(XmlResponse),
            "application/json" or "application/ld+json" or "text/json" or "application/x-json" or "application/problem+json" => typeof(JsonResponse),
            "application/javascript" or "application/x-javascript" or "text/javascript" or "application/ecmascript" => typeof(TextResponse),
            _ when mime.EndsWith("+xml", StringComparison.Ordinal) => typeof(XmlResponse),
            _ when mime.EndsWith("+json", StringComparison.Ordinal) => typeof(JsonResponse),
            _ when mime.StartsWith("text/", StringComparison.Ordinal) => typeof(TextResponse),
            // Servers send application/octet-stream for anything they don't recognise; let the URL
            // and body decide instead.
            "application/octet-stream" or "binary/octet-stream" => null,
            _ => typeof(Response),
        };
    }

    public static Type? FromUrl(string url)
    {
        var path = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.AbsolutePath : url;
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".html" or ".htm" or ".xhtml" or ".shtml" or ".php" or ".asp" or ".aspx" or ".jsp" => typeof(HtmlResponse),
            ".xml" or ".rss" or ".atom" or ".rdf" => typeof(XmlResponse),
            ".json" or ".jsonl" or ".geojson" => typeof(JsonResponse),
            ".txt" or ".csv" or ".tsv" or ".md" or ".js" or ".css" => typeof(TextResponse),
            _ => null,
        };
    }

    public static Type? FromBody(ReadOnlySpan<byte> body)
    {
        if (body.IsEmpty) return null;
        var head = body[..Math.Min(body.Length, 1024)];
        var start = 0;
        if (head.StartsWith(EncodingDetector.Utf8Bom)) start = 3;
        while (start < head.Length && head[start] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n') start++;
        var s = head[start..];
        if (s.StartsWith("<?xml"u8)) return LooksLikeHtml(head) ? typeof(HtmlResponse) : typeof(XmlResponse);
        if (s.Length > 0 && s[0] == (byte)'<') return LooksLikeHtml(head) ? typeof(HtmlResponse) : typeof(XmlResponse);
        if (s.Length > 0 && (s[0] == (byte)'{' || s[0] == (byte)'[')) return typeof(JsonResponse);
        // Text if there are no control bytes other than whitespace in the first KB.
        foreach (var b in head)
            if (b < 0x09 || (b > 0x0D && b < 0x20)) return typeof(Response);
        return typeof(TextResponse);
    }

    private static bool LooksLikeHtml(ReadOnlySpan<byte> head)
    {
        Span<byte> lower = stackalloc byte[head.Length];
        for (var i = 0; i < head.Length; i++) lower[i] = head[i] is >= (byte)'A' and <= (byte)'Z' ? (byte)(head[i] + 32) : head[i];
        return lower.IndexOf("<html"u8) >= 0 || lower.IndexOf("<!doctype html"u8) >= 0 || lower.IndexOf("<head"u8) >= 0 || lower.IndexOf("<body"u8) >= 0;
    }

    /// <summary>Constructs a response of the chosen type.</summary>
    public static Response Create(string url, int status, Headers headers, byte[] body, Request? request = null,
        IEnumerable<string>? flags = null, string? protocol = null, IPAddress? ipAddress = null)
    {
        var type = FromArgs(headers.Get("Content-Type"), url, body);
        return Create(type, url, status, headers, body, request, flags, protocol, ipAddress, null);
    }

    internal static Response Create(Type type, string url, int status, Headers headers, byte[] body, Request? request,
        IEnumerable<string>? flags, string? protocol, IPAddress? ipAddress, Encoding? encoding)
    {
        if (type == typeof(HtmlResponse)) return new HtmlResponse(url, status, headers, body, request, flags, protocol, ipAddress, encoding);
        if (type == typeof(XmlResponse)) return new XmlResponse(url, status, headers, body, request, flags, protocol, ipAddress, encoding);
        if (type == typeof(JsonResponse)) return new JsonResponse(url, status, headers, body, request, flags, protocol, ipAddress, encoding);
        if (type == typeof(TextResponse)) return new TextResponse(url, status, headers, body, request, flags, protocol, ipAddress, encoding);
        if (type == typeof(Response)) return new Response(url, status, headers, body, request, flags, protocol, ipAddress);
        // A user subclass: fall back on its constructor with the common signature.
        return (Response)Activator.CreateInstance(type, url, status, headers, body, request, flags, protocol, ipAddress, encoding)!;
    }
}
