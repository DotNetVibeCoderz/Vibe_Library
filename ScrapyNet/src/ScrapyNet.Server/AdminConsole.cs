using System.Collections.Frozen;
using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ScrapyNet.Cmdline;

namespace ScrapyNet.Server;

/// <summary>
/// The Scrapy.Net Console: a browser UI over the platform API (dashboard, spiders and versions, jobs,
/// schedules, selector builder, alerts), served from files embedded in this assembly. It holds no data
/// and no credentials of its own: the user signs in with an API key and every call goes through the
/// same role checks as any other API client.
/// </summary>
public static class AdminConsole
{
    private const string ResourcePrefix = "ScrapyNet.Server.Admin.";

    private static readonly FrozenDictionary<string, string> ContentTypes = new Dictionary<string, string>
    {
        [".html"] = "text/html; charset=utf-8",
        [".css"] = "text/css; charset=utf-8",
        [".js"] = "text/javascript; charset=utf-8",
        [".svg"] = "image/svg+xml",
        [".ttf"] = "font/ttf",
        [".txt"] = "text/plain; charset=utf-8",
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    // Same-origin only: no inline script, no third-party anything. Charts set styles through the CSSOM,
    // which style-src 'self' allows.
    private const string ContentSecurityPolicy =
        "default-src 'none'; script-src 'self'; style-src 'self'; font-src 'self'; img-src 'self' data:; connect-src 'self'; " +
        "base-uri 'none'; form-action 'none'; frame-ancestors 'none'";

    /// <summary>
    /// Serves the console at <paramref name="path"/> (default <c>/admin</c>), talking to the API mapped by
    /// <see cref="ScrapyNetApi.MapScrapyNetApi"/> at <paramref name="apiPrefix"/>.
    /// </summary>
    public static IEndpointConventionBuilder MapScrapyNetAdmin(this IEndpointRouteBuilder app, string path = "/admin", string apiPrefix = "/api")
    {
        path = "/" + path.Trim('/');
        var files = LoadFiles(apiPrefix);
        var group = app.MapGroup(path);
        // Routing matches "/admin" and "/admin/" alike; relative asset URLs need the slash.
        group.MapGet("/{**file}", (string? file, HttpContext http) =>
        {
            if (string.IsNullOrEmpty(file) && http.Request.Path.Value?.EndsWith('/') != true)
                return Results.Redirect(http.Request.PathBase + path + "/", permanent: false);
            var name = string.IsNullOrEmpty(file) ? "index.html" : file;
            if (!files.TryGetValue(name, out var entry)) return Results.NotFound();
            var headers = http.Response.Headers;
            headers.ContentSecurityPolicy = ContentSecurityPolicy;
            headers.XContentTypeOptions = "nosniff";
            headers["Referrer-Policy"] = "no-referrer";
            headers.XFrameOptions = "DENY";
            // The page itself is never cached (it carries the version); versioned assets are.
            headers.CacheControl = name == "index.html" ? "no-store" : "public, max-age=86400";
            return Results.Bytes(entry.Body, entry.ContentType);
        });
        return group;
    }

    private static FrozenDictionary<string, (byte[] Body, string ContentType)> LoadFiles(string apiPrefix)
    {
        var assembly = typeof(AdminConsole).Assembly;
        var files = new Dictionary<string, (byte[], string)>(StringComparer.Ordinal);
        foreach (var resource in assembly.GetManifestResourceNames().Where(r => r.StartsWith(ResourcePrefix, StringComparison.Ordinal)))
        {
            var name = resource[ResourcePrefix.Length..];
            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            var bytes = buffer.ToArray();
            if (name == "index.html")
            {
                var html = Encoding.UTF8.GetString(bytes)
                    .Replace("{{API}}", System.Net.WebUtility.HtmlEncode("/" + apiPrefix.Trim('/')), StringComparison.Ordinal)
                    .Replace("{{VERSION}}", Uri.EscapeDataString(Templates.PackageVersion), StringComparison.Ordinal);
                bytes = Encoding.UTF8.GetBytes(html);
            }
            files[name] = (bytes, ContentTypes.GetValueOrDefault(Path.GetExtension(name), "application/octet-stream"));
        }
        return files.ToFrozenDictionary(StringComparer.Ordinal);
    }
}
