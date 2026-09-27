using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Extensions.Logging;

namespace ScrapyNet
{

/// <summary>
/// A parsed robots.txt (RFC 9309): user-agent groups, <c>Allow</c>/<c>Disallow</c> with <c>*</c> and <c>$</c>
/// wildcards (the longest match wins, <c>Allow</c> wins ties), <c>Crawl-delay</c> and <c>Sitemap</c>.
/// </summary>
public sealed class RobotsTxt
{
    private readonly List<Group> _groups = [];

    private sealed class Group
    {
        public List<string> Agents { get; } = [];
        public List<(bool Allow, string Pattern)> Rules { get; } = [];
        public double? CrawlDelay { get; set; }
    }

    public IReadOnlyList<string> Sitemaps { get; private set; } = [];

    public static RobotsTxt Parse(string content)
    {
        var robots = new RobotsTxt();
        var sitemaps = new List<string>();
        Group? current = null;
        var lastWasAgent = false;
        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine;
            var hash = line.IndexOf('#');
            if (hash >= 0) line = line[..hash];
            line = line.Trim();
            var colon = line.IndexOf(':');
            if (colon <= 0) continue;
            var key = line[..colon].Trim().ToLowerInvariant();
            var value = line[(colon + 1)..].Trim();
            switch (key)
            {
                case "user-agent":
                    if (current is null || !lastWasAgent)
                    {
                        current = new Group();
                        robots._groups.Add(current);
                    }
                    current.Agents.Add(value.ToLowerInvariant());
                    lastWasAgent = true;
                    continue;
                case "allow" or "disallow":
                    if (current is not null && (value.Length > 0 || key == "allow")) current.Rules.Add((key == "allow", value));
                    break;
                case "crawl-delay":
                    if (current is not null && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var delay)) current.CrawlDelay = delay;
                    break;
                case "sitemap":
                    sitemaps.Add(value);
                    break;
            }
            lastWasAgent = false;
        }
        robots.Sitemaps = sitemaps;
        return robots;
    }

    /// <summary>True when <paramref name="userAgent"/> may fetch <paramref name="url"/>.</summary>
    public bool CanFetch(string url, string userAgent)
    {
        var group = FindGroup(userAgent);
        if (group is null) return true;
        var path = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.PathAndQuery : url;
        if (path.Length == 0) path = "/";
        if (path == "/robots.txt") return true;

        var bestLength = -1;
        var allowed = true;
        foreach (var (allow, pattern) in group.Rules)
        {
            if (pattern.Length == 0) continue;
            if (!Matches(pattern, path)) continue;
            var length = pattern.Length;
            if (length > bestLength || (length == bestLength && allow))
            {
                bestLength = length;
                allowed = allow;
            }
        }
        return allowed;
    }

    public double? CrawlDelay(string userAgent) => FindGroup(userAgent)?.CrawlDelay;

    private Group? FindGroup(string userAgent)
    {
        // The product token is the part before '/', e.g. "Scrapy.Net/0.1 (...)" → "scrapy.net".
        var token = userAgent.Split('/', ' ')[0].ToLowerInvariant();
        Group? wildcard = null;
        Group? best = null;
        var bestLen = 0;
        foreach (var g in _groups)
        {
            foreach (var agent in g.Agents)
            {
                if (agent == "*") wildcard ??= g;
                else if (token.Contains(agent, StringComparison.Ordinal) && agent.Length > bestLen)
                {
                    best = g;
                    bestLen = agent.Length;
                }
            }
        }
        return best ?? wildcard;
    }

    /// <summary>Robots path matching: <c>*</c> matches any run of characters, a trailing <c>$</c> anchors the end.</summary>
    internal static bool Matches(string pattern, string path)
    {
        var anchored = pattern.EndsWith('$');
        if (anchored) pattern = pattern[..^1];
        return Match(pattern, 0, path, 0, anchored);

        static bool Match(string p, int pi, string s, int si, bool anchored)
        {
            while (pi < p.Length)
            {
                if (p[pi] == '*')
                {
                    while (pi < p.Length && p[pi] == '*') pi++;
                    if (pi == p.Length) return true;
                    for (var k = si; k <= s.Length; k++)
                        if (Match(p, pi, s, k, anchored)) return true;
                    return false;
                }
                if (si >= s.Length || p[pi] != s[si]) return false;
                pi++;
                si++;
            }
            return !anchored || si == s.Length;
        }
    }
}

namespace DownloaderMiddlewares
{
    /// <summary>
    /// Honours robots.txt when <c>ROBOTSTXT_OBEY</c> is on: fetches each host's robots.txt once (through
    /// the downloader, so it gets the same headers, proxies and retries) and drops disallowed requests
    /// with <see cref="IgnoreRequestException"/>. Port of Scrapy's <c>RobotsTxtMiddleware</c>.
    /// </summary>
    public sealed class RobotsTxtMiddleware : DownloaderMiddleware
    {
        private readonly ConcurrentDictionary<string, Lazy<Task<RobotsTxt?>>> _parsers = new(StringComparer.OrdinalIgnoreCase);
        private readonly Crawler _crawler;
        private readonly ILogger _logger;
        private readonly string _userAgent;

        public RobotsTxtMiddleware(Crawler crawler, ILogger logger)
        {
            if (!crawler.Settings.GetBool(SettingKeys.RobotsTxtObey)) throw new NotConfiguredException();
            _crawler = crawler;
            _logger = logger;
            _userAgent = crawler.Settings.GetString(SettingKeys.RobotsTxtUserAgent) ?? crawler.Settings.GetString(SettingKeys.UserAgent) ?? "*";
        }

        public override async ValueTask<DownloadResult> ProcessRequestAsync(Request request, Spider spider)
        {
            if (request.GetMeta(MetaKeys.DontObeyRobotsTxt, false)) return DownloadResult.Continue;
            if (request.Uri.Scheme is not ("http" or "https")) return DownloadResult.Continue;

            var netloc = request.Uri.GetLeftPart(UriPartial.Authority);
            var robots = await _parsers.GetOrAdd(netloc, key => new Lazy<Task<RobotsTxt?>>(() => FetchAsync(key))).Value.ConfigureAwait(false);
            if (robots is null) return DownloadResult.Continue;

            var agent = request.Headers.Get("User-Agent") ?? _userAgent;
            if (!robots.CanFetch(request.Url, agent))
            {
                _logger.LogDebug("Forbidden by robots.txt: {Request}", request);
                _crawler.Stats.Inc("robotstxt/forbidden");
                throw new IgnoreRequestException("Forbidden by robots.txt");
            }
            return DownloadResult.Continue;
        }

        /// <summary>The parsed robots.txt of a host, if it has been fetched.</summary>
        public RobotsTxt? GetParsed(string netloc) =>
            _parsers.TryGetValue(netloc, out var lazy) && lazy.IsValueCreated && lazy.Value.IsCompletedSuccessfully ? lazy.Value.Result : null;

        private async Task<RobotsTxt?> FetchAsync(string netloc)
        {
            var request = new Request(netloc + "/robots.txt")
            {
                Priority = 1000,
                DontFilter = true,
                Meta = { [MetaKeys.DontObeyRobotsTxt] = true },
            };
            _crawler.Stats.Inc("robotstxt/request_count");
            try
            {
                var response = await _crawler.Engine.DownloadAsync(request).ConfigureAwait(false);
                _crawler.Stats.Inc("robotstxt/response_count");
                _crawler.Stats.Inc($"robotstxt/response_status_count/{response.Status}");
                if (response.Status != 200 || response.Body.Length == 0) return null;
                return RobotsTxt.Parse(EncodingDetector.Decode(response.Body, System.Text.Encoding.UTF8));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug("Error downloading {Request}: {Error}", request, ex.Message);
                _crawler.Stats.Inc($"robotstxt/exception_count/{ex.GetType().FullName}");
                return null;
            }
        }
    }
}
}
