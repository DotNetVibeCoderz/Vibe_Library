using System.Net;

namespace ScrapyNet;

/// <summary>
/// A downloaded HTTP response. Mirrors Scrapy's <c>scrapy.http.Response</c>.
/// </summary>
/// <remarks>
/// Binary responses (images, PDFs, archives) are plain <see cref="Response"/>s; anything textual is a
/// <see cref="TextResponse"/> or one of its subclasses. The text and selector members live on this base
/// class too, as virtuals that throw for binary content, so a callback typed against
/// <see cref="Response"/> can call <c>response.Css(...)</c> without casting.
/// </remarks>
public class Response
{
    private List<string>? _flags;
    private Request? _request;

    public Response(string url, int status = 200, Headers? headers = null, byte[]? body = null, Request? request = null,
        IEnumerable<string>? flags = null, string? protocol = null, IPAddress? ipAddress = null)
    {
        ArgumentNullException.ThrowIfNull(url);
        Url = url;
        Uri = new Uri(url, UriKind.Absolute);
        Status = status;
        Headers = headers ?? new Headers();
        Body = body ?? [];
        _request = request;
        _flags = flags is null ? null : [.. flags];
        Protocol = protocol;
        IpAddress = ipAddress;
    }

    public string Url { get; }

    public Uri Uri { get; }

    public int Status { get; }

    public Headers Headers { get; }

    public byte[] Body { get; }

    /// <summary>Flags such as <c>cached</c> or <c>redirected</c>, shown in the crawl log.</summary>
    public List<string> Flags => _flags ??= [];

    /// <summary>HTTP protocol, e.g. <c>HTTP/1.1</c>, when the handler knows it.</summary>
    public string? Protocol { get; }

    public IPAddress? IpAddress { get; }

    /// <summary>The request that produced this response. After redirects, the last request in the chain.</summary>
    public Request Request
    {
        get => _request ?? throw new InvalidOperationException("Response has no associated request.");
        internal set => _request = value;
    }

    internal bool HasRequest => _request is not null;

    /// <summary>Shortcut for <c>Request.Meta</c>. Survives redirects and retries.</summary>
    public Dictionary<string, object?> Meta => Request.Meta;

    /// <summary>Shortcut for <c>Request.CbKwargs</c>.</summary>
    public Dictionary<string, object?> CbKwargs => Request.CbKwargs;

    /// <summary>True for 2xx statuses.</summary>
    public bool IsSuccess => Status is >= 200 and < 300;

    /// <summary>Decoded body. Throws for binary responses.</summary>
    public virtual string Text => throw new NotSupportedException($"Response content isn't text ({Url}).");

    /// <summary>Lazily-built selector over the body. Throws for binary responses.</summary>
    public virtual Selector Selector => throw new NotSupportedException($"Response content isn't text ({Url}).");

    public SelectorList Css(string query) => Selector.Css(query);

    public SelectorList Xpath(string query) => Selector.Xpath(query);

    /// <summary>Resolves a possibly relative URL against this response (honouring an HTML <c>&lt;base&gt;</c>).</summary>
    public virtual string UrlJoin(string url) => UrlUtils.Join(Url, url);

    /// <summary>A copy of this response with some parts replaced. The concrete type is kept.</summary>
    public virtual Response Replace(string? url = null, int? status = null, Headers? headers = null, byte[]? body = null,
        Request? request = null, IEnumerable<string>? flags = null) =>
        new Response(url ?? Url, status ?? Status, headers ?? Headers, body ?? Body, request ?? _request, flags ?? _flags, Protocol, IpAddress);

    // ---- follow ---------------------------------------------------------------------------------

    /// <summary>A request for <paramref name="url"/> (relative URLs allowed), parsed by the spider's default <c>Parse</c>.</summary>
    public Request Follow(string url) => new(UrlJoin(url));

    public Request Follow(string url, Func<Response, IAsyncEnumerable<object>> callback) => new(UrlJoin(url), callback);

    public Request Follow(string url, Func<Response, IEnumerable<object>> callback) => new(UrlJoin(url), callback);

    public Request Follow(Link link) => new(link.Url);

    public Request Follow(Link link, Func<Response, IAsyncEnumerable<object>> callback) => new(link.Url, callback);

    public Request Follow(Link link, Func<Response, IEnumerable<object>> callback) => new(link.Url, callback);

    /// <summary>Follows an <c>&lt;a&gt;</c>/<c>&lt;link&gt;</c> (href) or <c>&lt;img&gt;</c> (src) element, or an attribute/text selector's value.</summary>
    public Request Follow(Selector selector) => Follow(UrlFromSelector(selector));

    public Request Follow(Selector selector, Func<Response, IAsyncEnumerable<object>> callback) => Follow(UrlFromSelector(selector), callback);

    public Request Follow(Selector selector, Func<Response, IEnumerable<object>> callback) => Follow(UrlFromSelector(selector), callback);

    public IEnumerable<Request> FollowAll(IEnumerable<string> urls) => urls.Select(Follow);

    public IEnumerable<Request> FollowAll(IEnumerable<string> urls, Func<Response, IAsyncEnumerable<object>> callback) => urls.Select(u => Follow(u, callback));

    public IEnumerable<Request> FollowAll(IEnumerable<string> urls, Func<Response, IEnumerable<object>> callback) => urls.Select(u => Follow(u, callback));

    public IEnumerable<Request> FollowAll(IEnumerable<Selector> selectors) => selectors.Select(Follow);

    public IEnumerable<Request> FollowAll(IEnumerable<Selector> selectors, Func<Response, IAsyncEnumerable<object>> callback) => selectors.Select(s => Follow(s, callback));

    public IEnumerable<Request> FollowAll(IEnumerable<Selector> selectors, Func<Response, IEnumerable<object>> callback) => selectors.Select(s => Follow(s, callback));

    public IEnumerable<Request> FollowAll(IEnumerable<Link> links, Func<Response, IAsyncEnumerable<object>> callback) => links.Select(l => Follow(l, callback));

    public IEnumerable<Request> FollowAll(IEnumerable<Link> links, Func<Response, IEnumerable<object>> callback) => links.Select(l => Follow(l, callback));

    /// <summary>Scrapy's <c>response.follow_all(css=...)</c>.</summary>
    public IEnumerable<Request> FollowAllCss(string css, Func<Response, IEnumerable<object>> callback) => FollowAll(Css(css), callback);

    public IEnumerable<Request> FollowAllCss(string css, Func<Response, IAsyncEnumerable<object>> callback) => FollowAll(Css(css), callback);

    public IEnumerable<Request> FollowAllCss(string css) => FollowAll(Css(css));

    /// <summary>Scrapy's <c>response.follow_all(xpath=...)</c>.</summary>
    public IEnumerable<Request> FollowAllXpath(string xpath, Func<Response, IEnumerable<object>> callback) => FollowAll(Xpath(xpath), callback);

    public IEnumerable<Request> FollowAllXpath(string xpath, Func<Response, IAsyncEnumerable<object>> callback) => FollowAll(Xpath(xpath), callback);

    private static string UrlFromSelector(Selector selector)
    {
        var url = selector.Kind == SelectorKind.Element
            ? selector.ElementName switch
            {
                "a" or "link" or "area" => selector.Attrib.GetValueOrDefault("href"),
                "img" or "script" or "iframe" or "source" or "video" or "audio" or "embed" => selector.Attrib.GetValueOrDefault("src"),
                _ => throw new ArgumentException($"Only <a>, <link>, <area> or media elements can be followed, not <{selector.ElementName}>."),
            }
            : selector.Get();
        return url?.Trim() ?? throw new ArgumentException("Selector has no URL to follow.");
    }

    public override string ToString() => $"<{Status} {Url}>";
}
