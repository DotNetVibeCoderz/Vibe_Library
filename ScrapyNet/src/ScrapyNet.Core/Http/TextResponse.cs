using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ScrapyNet;

/// <summary>A response with a textual body: decoding, selectors and JSON on demand.</summary>
/// <remarks>
/// Decoding, selector parsing and JSON parsing are all lazy and cached. A callback that only reads
/// <c>response.Json()</c> never builds a DOM, and one that runs ten CSS queries parses the document once.
/// </remarks>
public class TextResponse : Response
{
    private Encoding? _encoding;
    private string? _text;
    private Selector? _selector;
    private JsonNode? _json;
    private bool _jsonParsed;

    public TextResponse(string url, int status = 200, Headers? headers = null, byte[]? body = null, Request? request = null,
        IEnumerable<string>? flags = null, string? protocol = null, IPAddress? ipAddress = null, Encoding? encoding = null)
        : base(url, status, headers, body, request, flags, protocol, ipAddress)
    {
        _encoding = encoding;
    }

    /// <summary>Builds a response straight from a string, mainly for tests and the shell.</summary>
    public TextResponse(string url, string text, int status = 200, Headers? headers = null, Request? request = null)
        : base(url, status, headers, Encoding.UTF8.GetBytes(text), request)
    {
        _encoding = Encoding.UTF8;
        _text = text;
    }

    /// <summary>Body encoding: BOM, then Content-Type charset, then in-document declaration, then UTF-8.</summary>
    public Encoding Encoding => _encoding ??= EncodingDetector.Detect(Headers.Get("Content-Type"), Body, DeclarationKind);

    public override string Text => _text ??= EncodingDetector.Decode(Body, Encoding);

    public override Selector Selector => _selector ??= CreateSelector();

    /// <summary>Which in-document encoding declaration to look for (<c>&lt;meta charset&gt;</c> or <c>&lt;?xml encoding?&gt;</c>).</summary>
    protected virtual DeclarationKind DeclarationKind => DeclarationKind.Html;

    protected virtual Selector CreateSelector() => Selector.FromHtml(Text, Url);

    /// <summary>Parses the body as JSON (cached). Returns <c>null</c> for an empty body.</summary>
    public JsonNode? Json()
    {
        if (_jsonParsed) return _json;
        _json = Body.Length == 0 ? null : JsonNode.Parse(Text);
        _jsonParsed = true;
        return _json;
    }

    /// <summary>Deserializes the body with web defaults (camelCase-insensitive property names).</summary>
    public T? Json<T>() => Body.Length == 0 ? default : JsonSerializer.Deserialize<T>(Text, JsonDefaults.Web);

    public override string UrlJoin(string url)
    {
        var baseUrl = BaseUrl;
        return UrlUtils.Join(baseUrl, url);
    }

    /// <summary>The URL relative links resolve against: the <c>&lt;base href&gt;</c> when present, else <see cref="Response.Url"/>.</summary>
    public virtual string BaseUrl => Url;

    public override Response Replace(string? url = null, int? status = null, Headers? headers = null, byte[]? body = null,
        Request? request = null, IEnumerable<string>? flags = null) =>
        ResponseTypes.Create(GetType(), url ?? Url, status ?? Status, headers ?? Headers, body ?? Body,
            request ?? (HasRequest ? Request : null), flags ?? Flags, Protocol, IpAddress, body is null ? _encoding : null);
}

/// <summary>An HTML response. <see cref="Selector"/> is an HTML5 DOM.</summary>
public class HtmlResponse : TextResponse
{
    private string? _baseUrl;

    public HtmlResponse(string url, int status = 200, Headers? headers = null, byte[]? body = null, Request? request = null,
        IEnumerable<string>? flags = null, string? protocol = null, IPAddress? ipAddress = null, Encoding? encoding = null)
        : base(url, status, headers, body, request, flags, protocol, ipAddress, encoding) { }

    public HtmlResponse(string url, string html, int status = 200, Headers? headers = null, Request? request = null)
        : base(url, html, status, headers, request) { }

    public override string BaseUrl => _baseUrl ??= ResolveBaseUrl();

    private string ResolveBaseUrl()
    {
        var href = Selector.Css("base::attr(href)").Get();
        return string.IsNullOrWhiteSpace(href) ? Url : UrlUtils.Join(Url, href);
    }
}

/// <summary>An XML response (feeds, sitemaps, SOAP). <see cref="Selector"/> is an XML DOM.</summary>
public class XmlResponse : TextResponse
{
    public XmlResponse(string url, int status = 200, Headers? headers = null, byte[]? body = null, Request? request = null,
        IEnumerable<string>? flags = null, string? protocol = null, IPAddress? ipAddress = null, Encoding? encoding = null)
        : base(url, status, headers, body, request, flags, protocol, ipAddress, encoding) { }

    public XmlResponse(string url, string xml, int status = 200, Headers? headers = null, Request? request = null)
        : base(url, xml, status, headers, request) { }

    protected override DeclarationKind DeclarationKind => DeclarationKind.Xml;

    protected override Selector CreateSelector() => Selector.FromXml(Text, Url);
}

/// <summary>A JSON response. Use <see cref="TextResponse.Json()"/> or <see cref="TextResponse.Json{T}()"/>.</summary>
public class JsonResponse : TextResponse
{
    public JsonResponse(string url, int status = 200, Headers? headers = null, byte[]? body = null, Request? request = null,
        IEnumerable<string>? flags = null, string? protocol = null, IPAddress? ipAddress = null, Encoding? encoding = null)
        : base(url, status, headers, body, request, flags, protocol, ipAddress, encoding) { }

    public JsonResponse(string url, string json, int status = 200, Headers? headers = null, Request? request = null)
        : base(url, json, status, headers, request) { }

    protected override DeclarationKind DeclarationKind => DeclarationKind.None;
}

internal static class JsonDefaults
{
    public static readonly JsonSerializerOptions Web = ScrapyJson.Web;
}
