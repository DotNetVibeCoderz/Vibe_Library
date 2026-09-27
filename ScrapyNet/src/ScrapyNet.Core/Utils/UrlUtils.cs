using System.Text;

namespace ScrapyNet;

/// <summary>URL helpers ported from w3lib/Scrapy: joining, canonicalisation, domain matching.</summary>
public static class UrlUtils
{
    /// <summary>Extensions the link extractor skips by default (Scrapy's <c>IGNORED_EXTENSIONS</c>).</summary>
    public static readonly IReadOnlySet<string> IgnoredExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        // archives
        "7z", "7zip", "bz2", "rar", "tar", "tar.gz", "xz", "zip", "gz", "tgz",
        // images
        "mng", "pct", "bmp", "gif", "jpg", "jpeg", "png", "pst", "psp", "tif", "tiff", "ai", "drw", "dxf", "eps", "ps", "svg", "cdr", "ico", "webp", "avif", "heic",
        // audio
        "mp3", "wma", "ogg", "wav", "ra", "aac", "mid", "au", "aiff", "flac", "m4a",
        // video
        "3gp", "asf", "asx", "avi", "mov", "mp4", "mpg", "qt", "rm", "swf", "wmv", "m4v", "webm", "mkv",
        // office
        "xls", "xlsm", "xlsx", "xltm", "xltx", "potm", "potx", "ppt", "pptm", "pptx", "pps", "doc", "docb", "docm", "docx", "dotm", "dotx", "odt", "ods", "odg", "odp",
        // other
        "css", "pdf", "exe", "bin", "rss", "dmg", "iso", "apk", "jar", "sh", "rpm", "deb", "msi", "woff", "woff2", "ttf", "otf", "eot",
    };

    /// <summary>Resolves <paramref name="relative"/> against <paramref name="baseUrl"/> like a browser would.</summary>
    public static string Join(string baseUrl, string relative)
    {
        ArgumentNullException.ThrowIfNull(relative);
        var rel = relative.Trim();
        if (rel.Length == 0) return baseUrl;
        // On Unix, Uri treats "/path" as an absolute file URI, so only strings with a scheme count as absolute.
        if (HasScheme(rel) && Uri.TryCreate(rel, UriKind.Absolute, out var absolute))
            return absolute.Scheme is "http" or "https" ? absolute.AbsoluteUri : rel;
        if (Uri.TryCreate(new Uri(baseUrl), rel, out var joined)) return joined.AbsoluteUri;
        return rel;
    }

    /// <summary>True when the string starts with a URL scheme (<c>http:</c>, <c>data:</c>, ...).</summary>
    public static bool HasScheme(string url)
    {
        var colon = url.IndexOf(':');
        if (colon <= 0 || !char.IsAsciiLetter(url[0])) return false;
        for (var i = 1; i < colon; i++)
            if (!(char.IsAsciiLetterOrDigit(url[i]) || url[i] is '+' or '-' or '.')) return false;
        return true;
    }

    /// <summary>
    /// Canonical form used for fingerprinting: lower-case scheme and host, default port dropped, query
    /// arguments sorted, fragment removed. Two URLs that fetch the same resource map to one string.
    /// </summary>
    public static string Canonicalize(string url, bool keepFragments = false)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.IsFile || uri.Scheme == "data") return url;

        var sb = new StringBuilder(url.Length);
        sb.Append(uri.Scheme).Append("://");
        if (!string.IsNullOrEmpty(uri.UserInfo)) sb.Append(uri.UserInfo).Append('@');
        sb.Append(uri.IdnHost.ToLowerInvariant());
        if (!uri.IsDefaultPort) sb.Append(':').Append(uri.Port);
        sb.Append(string.IsNullOrEmpty(uri.AbsolutePath) ? "/" : uri.AbsolutePath);

        var query = uri.Query;
        if (query.Length > 1)
        {
            var parts = query[1..].Split('&', StringSplitOptions.RemoveEmptyEntries);
            // Ordinal sort by key, then value: '?b=2&a=1' and '?a=1&b=2' collapse together. Most URLs
            // are already in order, so check first; the comparison works on spans (no per-compare allocations).
            var sorted = true;
            for (var i = 1; i < parts.Length && sorted; i++) sorted = CompareParameters(parts[i - 1], parts[i]) <= 0;
            if (!sorted) Array.Sort(parts, CompareParameters);
            sb.Append('?').AppendJoin('&', parts);
        }

        if (keepFragments && uri.Fragment.Length > 1) sb.Append(uri.Fragment);
        return sb.ToString();
    }

    private static int CompareParameters(string a, string b)
    {
        var ea = a.IndexOf('=');
        var eb = b.IndexOf('=');
        var c = a.AsSpan(0, ea < 0 ? a.Length : ea).SequenceCompareTo(b.AsSpan(0, eb < 0 ? b.Length : eb));
        return c != 0 ? c : string.CompareOrdinal(a, b);
    }

    /// <summary>Host name in lower case, or empty for URLs without one.</summary>
    public static string GetHost(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host.ToLowerInvariant() : "";

    /// <summary>True when the URL's host is one of <paramref name="domains"/> or a subdomain of one.</summary>
    public static bool IsFromAnyDomain(string url, IEnumerable<string> domains) => HostMatchesAny(GetHost(url), domains);

    public static bool HostMatchesAny(string host, IEnumerable<string> domains)
    {
        foreach (var raw in domains)
        {
            var d = raw.Trim().TrimStart('.').ToLowerInvariant();
            // Allowed domains may carry a port ("example.com:8080"); compare hosts only.
            var colon = d.IndexOf(':');
            if (colon >= 0) d = d[..colon];
            if (d.Length == 0) continue;
            if (host == d || host.EndsWith("." + d, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    /// <summary>True when the URL path ends in one of the extensions (without dots).</summary>
    public static bool HasAnyExtension(string url, IReadOnlySet<string> extensions)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        var path = uri.AbsolutePath;
        var slash = path.LastIndexOf('/');
        var file = slash >= 0 ? path[(slash + 1)..] : path;
        var dot = file.IndexOf('.');
        while (dot >= 0)
        {
            if (extensions.Contains(file[(dot + 1)..])) return true;
            dot = file.IndexOf('.', dot + 1);
        }
        return false;
    }

    /// <summary>Returns the value of a query parameter, or <c>null</c>.</summary>
    public static string? GetQueryParameter(string url, string name)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2);
            if (Uri.UnescapeDataString(kv[0]) == name) return kv.Length > 1 ? Uri.UnescapeDataString(kv[1].Replace('+', ' ')) : "";
        }
        return null;
    }

    /// <summary>Adds or replaces a query parameter.</summary>
    public static string SetQueryParameter(string url, string name, string value)
    {
        var uri = new Uri(url);
        var parts = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => Uri.UnescapeDataString(p.Split('=', 2)[0]) != name)
            .Append($"{Uri.EscapeDataString(name)}={Uri.EscapeDataString(value)}");
        var builder = new UriBuilder(uri) { Query = string.Join('&', parts) };
        return builder.Uri.AbsoluteUri;
    }

    /// <summary>Removes the fragment.</summary>
    public static string StripFragment(string url)
    {
        var hash = url.IndexOf('#');
        return hash < 0 ? url : url[..hash];
    }

    /// <summary>application/x-www-form-urlencoded encoding of key/value pairs.</summary>
    public static string EncodeForm(IEnumerable<KeyValuePair<string, string>> pairs, Encoding? encoding = null)
    {
        encoding ??= Encoding.UTF8;
        var sb = new StringBuilder();
        foreach (var (key, value) in pairs)
        {
            if (sb.Length > 0) sb.Append('&');
            sb.Append(FormEscape(key, encoding)).Append('=').Append(FormEscape(value, encoding));
        }
        return sb.ToString();
    }

    private static string FormEscape(string value, Encoding encoding)
    {
        if (encoding.CodePage == Encoding.UTF8.CodePage) return Uri.EscapeDataString(value).Replace("%20", "+");
        var bytes = encoding.GetBytes(value);
        var sb = new StringBuilder(bytes.Length * 3);
        foreach (var b in bytes)
        {
            var c = (char)b;
            if (char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or '~') sb.Append(c);
            else if (c == ' ') sb.Append('+');
            else sb.Append('%').Append(b.ToString("X2"));
        }
        return sb.ToString();
    }
}
