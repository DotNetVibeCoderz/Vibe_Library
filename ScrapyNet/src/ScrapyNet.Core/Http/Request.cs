using System.Runtime.CompilerServices;
using System.Text;

namespace ScrapyNet;

/// <summary>
/// An HTTP request that a spider wants downloaded. Mirrors Scrapy's <c>scrapy.Request</c>.
/// </summary>
/// <remarks>
/// <para>
/// Requests are records so that <c>with</c> expressions give Scrapy's <c>request.replace(...)</c> for
/// free: <c>request with { Priority = 10, DontFilter = true }</c>. The copy constructor clones the
/// mutable parts (headers, meta, cb_kwargs, cookies, flags), so a copy never aliases its original.
/// </para>
/// <para>
/// Equality is reference equality. Two requests for the same URL are still two requests; duplicate
/// detection is the dupe filter's job and is done on fingerprints, not on <see cref="object.Equals(object)"/>.
/// </para>
/// </remarks>
public record Request
{
    private string _url = "";
    private Uri _uri = null!;
    private string _method = "GET";
    private Dictionary<string, object?>? _meta;
    private Dictionary<string, object?>? _cbKwargs;
    private List<string>? _flags;
    private Dictionary<string, string>? _cookies;
    private Headers _headers = new();

    /// <summary>Creates a GET request. The spider's default <c>Parse</c> handles the response.</summary>
    public Request(string url) => Url = url;

    /// <summary>Creates a request whose response is handed to an async callback.</summary>
    public Request(string url, Func<Response, IAsyncEnumerable<object>>? callback) : this(url) => Callback = callback;

    /// <summary>Creates a request whose response is handed to a synchronous (iterator) callback.</summary>
    public Request(string url, Func<Response, IEnumerable<object>> callback) : this(url) => Callback = Callbacks.FromSync(callback);

    /// <summary>Copy constructor used by <c>with</c> expressions; deep-copies the mutable collections.</summary>
    protected Request(Request original)
    {
        _url = original._url;
        _uri = original._uri;
        _method = original._method;
        _headers = original._headers.Copy();
        Body = original.Body;
        _meta = original._meta is null ? null : new Dictionary<string, object?>(original._meta);
        _cbKwargs = original._cbKwargs is null ? null : new Dictionary<string, object?>(original._cbKwargs);
        _flags = original._flags is null ? null : [.. original._flags];
        _cookies = original._cookies is null ? null : new Dictionary<string, string>(original._cookies);
        Priority = original.Priority;
        DontFilter = original.DontFilter;
        Callback = original.Callback;
        Errback = original.Errback;
        Encoding = original.Encoding;
        // The cached fingerprint is deliberately not copied: a copy made with a different URL or
        // body would otherwise inherit its original's identity.
    }

    /// <summary>Absolute URL. Relative URLs are rejected; use <see cref="Response.UrlJoin"/> or <see cref="Response.Follow(string)"/>.</summary>
    public string Url
    {
        get => _url;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            if (!UrlUtils.HasScheme(value.Trim()) || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri))
                throw new ArgumentException($"Missing scheme in request url: {value}", nameof(value));
            _uri = uri;
            _url = uri.IsFile || uri.Scheme == "data" ? value.Trim() : uri.AbsoluteUri;
            CachedFingerprint = null;
        }
    }

    /// <summary>The parsed <see cref="Url"/>.</summary>
    public Uri Uri => _uri;

    /// <summary>HTTP method, always upper case. Defaults to GET.</summary>
    public string Method
    {
        get => _method;
        init => _method = value.ToUpperInvariant();
    }

    public Headers Headers
    {
        get => _headers;
        init => _headers = value ?? new Headers();
    }

    /// <summary>Request body. Empty for GET.</summary>
    public byte[] Body { get; init; } = [];

    /// <summary>Cookies to send with this request, merged into the cookie jar by the cookies middleware.</summary>
    public Dictionary<string, string> Cookies
    {
        get => _cookies ??= new Dictionary<string, string>(StringComparer.Ordinal);
        init => _cookies = value;
    }

    internal bool HasCookies => _cookies is { Count: > 0 };

    /// <summary>
    /// Free-form data travelling with the request to its response (<c>response.Meta</c>). Components use
    /// well-known keys such as <c>depth</c>, <c>proxy</c>, <c>download_timeout</c>, <c>dont_retry</c>,
    /// <c>cookiejar</c>, <c>handle_httpstatus_list</c>; see <see cref="MetaKeys"/>.
    /// </summary>
    public Dictionary<string, object?> Meta
    {
        get => _meta ??= new Dictionary<string, object?>(StringComparer.Ordinal);
        init => _meta = value;
    }

    internal bool HasMeta => _meta is { Count: > 0 };

    /// <summary>Keyword arguments for the callback, available as <c>response.CbKwargs</c>.</summary>
    public Dictionary<string, object?> CbKwargs
    {
        get => _cbKwargs ??= new Dictionary<string, object?>(StringComparer.Ordinal);
        init => _cbKwargs = value;
    }

    internal bool HasCbKwargs => _cbKwargs is { Count: > 0 };

    /// <summary>Scheduling priority; higher runs earlier. May be negative.</summary>
    public int Priority { get; init; }

    /// <summary>Bypass the duplicate filter (Scrapy's <c>dont_filter</c>).</summary>
    public bool DontFilter { get; init; }

    /// <summary>Called with the response. <c>null</c> means the spider's <see cref="Spider.Parse"/>.</summary>
    public Func<Response, IAsyncEnumerable<object>>? Callback { get; init; }

    /// <summary>Called when downloading or processing the request fails.</summary>
    public Func<Failure, IAsyncEnumerable<object>>? Errback { get; init; }

    public List<string> Flags
    {
        get => _flags ??= [];
        init => _flags = value;
    }

    /// <summary>Encoding used to encode form data and to guess the response encoding. Defaults to UTF-8.</summary>
    public Encoding Encoding { get; init; } = Encoding.UTF8;

    /// <summary>Cached request fingerprint, computed once by the dupe filter.</summary>
    internal RequestFingerprint? CachedFingerprint { get; set; }

    /// <summary>Crawl depth recorded by the depth middleware (0 for start requests).</summary>
    public int Depth => Meta.TryGetValue(MetaKeys.Depth, out var d) && d is not null ? Convert.ToInt32(d) : 0;

    /// <summary>
    /// The name of the spider method used as callback, or <c>null</c> when the callback is a lambda or a
    /// method on some other object. Only named callbacks survive a pause/resume through <c>JOBDIR</c>.
    /// </summary>
    public string? CallbackName => Callbacks.GetName(Callback);

    public string? ErrbackName => Callbacks.GetName(Errback);

    /// <summary>Convenience for sync errbacks: <c>request.WithErrback(f => Logger.LogWarning(...))</c>.</summary>
    public Request WithErrback(Action<Failure> errback) => this with { Errback = Callbacks.FromAction(errback) };

    /// <summary>Convenience for errbacks that yield follow-up requests or items synchronously.</summary>
    public Request WithErrback(Func<Failure, IEnumerable<object>> errback) => this with { Errback = Callbacks.FromSync(errback) };

    public Request WithErrback(Func<Failure, IAsyncEnumerable<object>> errback) => this with { Errback = errback };

    /// <summary>Same request with a different callback (sync).</summary>
    public Request WithCallback(Func<Response, IEnumerable<object>> callback) => this with { Callback = Callbacks.FromSync(callback) };

    public Request WithCallback(Func<Response, IAsyncEnumerable<object>> callback) => this with { Callback = callback };

    /// <summary>Same request with extra meta entries.</summary>
    public Request WithMeta(string key, object? value)
    {
        var copy = this with { };
        copy.Meta[key] = value;
        return copy;
    }

    /// <summary>Gets a typed meta value, or <paramref name="defaultValue"/>.</summary>
    public T? GetMeta<T>(string key, T? defaultValue = default)
    {
        if (_meta is null || !_meta.TryGetValue(key, out var value) || value is null) return defaultValue;
        if (value is T typed) return typed;
        try { return (T)Convert.ChangeType(value, typeof(T), System.Globalization.CultureInfo.InvariantCulture); }
        catch { return defaultValue; }
    }

    public virtual bool Equals(Request? other) => ReferenceEquals(this, other);

    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);

    public override string ToString() => $"<{Method} {Url}>";

    /// <summary>Renders the request as a curl command line, for reproducing it outside the crawler.</summary>
    public string ToCurl()
    {
        var sb = new StringBuilder("curl");
        if (Method != "GET") sb.Append(" -X ").Append(Method);
        sb.Append(' ').Append(Quote(Url));
        foreach (var (name, values) in Headers)
            foreach (var value in values) sb.Append(" -H ").Append(Quote($"{name}: {value}"));
        if (HasCookies) sb.Append(" --cookie ").Append(Quote(string.Join("; ", Cookies.Select(c => $"{c.Key}={c.Value}"))));
        if (Body.Length > 0) sb.Append(" --data-raw ").Append(Quote(Encoding.GetString(Body)));
        return sb.ToString();

        static string Quote(string s) => "'" + s.Replace("'", "'\\''") + "'";
    }
}
