using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace ScrapyNet.DownloaderMiddlewares;

/// <summary>
/// Keeps cookies like a browser: stores <c>Set-Cookie</c> responses and sends matching cookies back.
/// Several independent sessions can run side by side with <c>meta["cookiejar"] = key</c> (e.g. one per
/// logged-in account). <c>COOKIES_PERSIST_FILE</c> saves and restores every jar across runs.
/// Port of Scrapy's <c>CookiesMiddleware</c>.
/// </summary>
public sealed class CookiesMiddleware : DownloaderMiddleware
{
    private readonly Dictionary<string, CookieContainer> _jars = new(StringComparer.Ordinal);
    private readonly bool _debug;
    private readonly string? _persistFile;
    private readonly ILogger _logger;

    public CookiesMiddleware(Settings settings, ILogger logger)
    {
        if (!settings.GetBool(SettingKeys.CookiesEnabled, true)) throw new NotConfiguredException();
        _debug = settings.GetBool(SettingKeys.CookiesDebug);
        _persistFile = settings.GetString(SettingKeys.CookiesPersistFile);
        _logger = logger;
    }

    /// <summary>The cookie jar for a key (<c>null</c> = the default jar). Exposed for inspection and login helpers.</summary>
    public CookieContainer GetJar(object? key = null)
    {
        var name = key?.ToString() ?? "";
        lock (_jars)
        {
            if (!_jars.TryGetValue(name, out var jar)) _jars[name] = jar = new CookieContainer { PerDomainCapacity = 200, Capacity = 10000 };
            return jar;
        }
    }

    public IReadOnlyCollection<string> JarNames
    {
        get
        {
            lock (_jars) return [.. _jars.Keys];
        }
    }

    public override ValueTask OpenSpiderAsync(Spider spider)
    {
        if (!string.IsNullOrEmpty(_persistFile) && File.Exists(_persistFile))
        {
            var saved = JsonSerializer.Deserialize<Dictionary<string, List<SavedCookie>>>(File.ReadAllText(_persistFile), ScrapyJson.Default) ?? [];
            foreach (var (name, cookies) in saved)
            {
                var jar = GetJar(name);
                foreach (var c in cookies)
                {
                    try
                    {
                        jar.Add(new Cookie(c.Name, c.Value, c.Path, c.Domain) { Secure = c.Secure, HttpOnly = c.HttpOnly, Expires = c.Expires ?? DateTime.MinValue });
                    }
                    catch (CookieException)
                    {
                    }
                }
            }
            _logger.LogInformation("Loaded cookies for {Count} jar(s) from {File}", saved.Count, _persistFile);
        }
        return ValueTask.CompletedTask;
    }

    public override ValueTask CloseSpiderAsync(Spider spider, string reason)
    {
        if (string.IsNullOrEmpty(_persistFile)) return ValueTask.CompletedTask;
        var data = new Dictionary<string, List<SavedCookie>>();
        lock (_jars)
        {
            foreach (var (name, jar) in _jars)
                data[name] = [.. jar.GetAllCookies().Where(c => !c.Expired).Select(c => new SavedCookie(c.Name, c.Value, c.Domain, c.Path, c.Secure, c.HttpOnly, c.Expires == DateTime.MinValue ? null : c.Expires))];
        }
        var dir = Path.GetDirectoryName(Path.GetFullPath(_persistFile));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(_persistFile, JsonSerializer.Serialize(data, ScrapyJson.Indented));
        return ValueTask.CompletedTask;
    }

    public override ValueTask<DownloadResult> ProcessRequestAsync(Request request, Spider spider)
    {
        if (request.GetMeta(MetaKeys.DontMergeCookies, false)) return DownloadResult.Continue;
        if (request.Uri.Scheme is not ("http" or "https")) return DownloadResult.Continue;

        var jar = GetJar(request.Meta.GetValueOrDefault(MetaKeys.CookieJar));
        if (request.HasCookies)
        {
            foreach (var (name, value) in request.Cookies)
            {
                try
                {
                    jar.Add(request.Uri, new Cookie(name, value));
                }
                catch (CookieException ex)
                {
                    _logger.LogWarning("Invalid cookie {Name} for {Request}: {Error}", name, request, ex.Message);
                }
            }
        }

        if (!request.Headers.Contains("Cookie"))
        {
            var header = jar.GetCookieHeader(request.Uri);
            if (!string.IsNullOrEmpty(header))
            {
                request.Headers.Set("Cookie", header);
                if (_debug) _logger.LogDebug("Sending cookies to: {Request}\nCookie: {Cookie}", request, header);
            }
        }
        return DownloadResult.Continue;
    }

    public override ValueTask<DownloadResult> ProcessResponseAsync(Request request, Response response, Spider spider)
    {
        if (request.GetMeta(MetaKeys.DontMergeCookies, false)) return (DownloadResult)response;
        var setCookies = response.Headers.GetValues("Set-Cookie");
        if (setCookies.Count == 0) return (DownloadResult)response;
        var jar = GetJar(request.Meta.GetValueOrDefault(MetaKeys.CookieJar));
        foreach (var header in setCookies)
        {
            try
            {
                jar.SetCookies(response.Uri, header);
            }
            catch (CookieException ex)
            {
                _logger.LogDebug("Ignoring invalid Set-Cookie from {Response}: {Error}", response, ex.Message);
            }
        }
        if (_debug) _logger.LogDebug("Received cookies from: {Response}\n{Cookies}", response, string.Join("\n", setCookies.Select(c => "Set-Cookie: " + c)));
        return (DownloadResult)response;
    }

    private sealed record SavedCookie(string Name, string Value, string Domain, string Path, bool Secure, bool HttpOnly, DateTime? Expires);
}
