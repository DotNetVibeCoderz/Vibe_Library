using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using ScrapyNet.Handlers;

namespace ScrapyNet.Playwright;

/// <summary>Request meta keys understood by the Playwright handler.</summary>
public static class PlaywrightMeta
{
    /// <summary>Set to <c>true</c> to render the request in a browser.</summary>
    public const string Enabled = "playwright";

    /// <summary>A list of <see cref="PageMethod"/> run after navigation, in order.</summary>
    public const string PageMethods = "playwright_page_methods";

    /// <summary>Keep the page open and put it in <c>response.Meta["playwright_page"]</c> (you must close it).</summary>
    public const string IncludePage = "playwright_include_page";

    public const string Page = "playwright_page";

    /// <summary>Browser context name (separate cookies/storage per name).</summary>
    public const string Context = "playwright_context";

    /// <summary><c>load</c>, <c>domcontentloaded</c>, <c>networkidle</c> or <c>commit</c>.</summary>
    public const string WaitUntil = "playwright_wait_until";

    /// <summary>PNG bytes of the last <see cref="PageMethod.Screenshot"/>.</summary>
    public const string Screenshot = "playwright_screenshot";

    /// <summary>Return values of <see cref="PageMethod.Evaluate"/> calls, in order.</summary>
    public const string EvaluateResults = "playwright_evaluate_results";
}

/// <summary>An action to perform on the page after it loads.</summary>
public sealed record PageMethod(string Method, params object?[] Args)
{
    public static PageMethod WaitForSelector(string selector, float? timeoutMs = null) => new("wait_for_selector", selector, timeoutMs);
    public static PageMethod Click(string selector) => new("click", selector);
    public static PageMethod Fill(string selector, string value) => new("fill", selector, value);
    public static PageMethod Press(string selector, string key) => new("press", selector, key);
    public static PageMethod Evaluate(string script) => new("evaluate", script);
    public static PageMethod WaitForTimeout(float ms) => new("wait_for_timeout", ms);
    public static PageMethod WaitForLoadState(string state = "networkidle") => new("wait_for_load_state", state);

    /// <summary>Scrolls to the bottom <paramref name="times"/> times, pausing between scrolls (infinite scroll pages).</summary>
    public static PageMethod ScrollToBottom(int times = 1, float pauseMs = 500) => new("scroll_to_bottom", times, pauseMs);

    public static PageMethod Screenshot(bool fullPage = true) => new("screenshot", fullPage);
}

/// <summary>
/// Renders requests marked with <c>meta["playwright"] = true</c> in a real browser and returns the
/// rendered DOM as an <see cref="HtmlResponse"/>; everything else goes through the normal HTTP handler.
/// The browser starts lazily on the first rendered request. Port of scrapy-playwright.
/// </summary>
/// <remarks>
/// Settings: <c>PLAYWRIGHT_BROWSER_TYPE</c> (chromium | firefox | webkit), <c>PLAYWRIGHT_HEADLESS</c>,
/// <c>PLAYWRIGHT_MAX_PAGES</c> (concurrent pages, default 8), <c>PLAYWRIGHT_NAVIGATION_TIMEOUT</c> (ms),
/// <c>PLAYWRIGHT_BLOCKED_RESOURCE_TYPES</c> (e.g. <c>image,font,media</c>). Browsers are installed with
/// <see cref="PlaywrightSetup.InstallBrowsers"/> or <c>pwsh bin/.../playwright.ps1 install</c>.
/// </remarks>
public sealed class PlaywrightDownloadHandler : IDownloadHandler, IAsyncDisposable, IDisposable
{
    private readonly HttpDownloadHandler _http;
    private readonly ILogger _logger;
    private readonly IStatsCollector _stats;
    private readonly string _browserType;
    private readonly bool _headless;
    private readonly float _timeout;
    private readonly HashSet<string> _blocked;
    private readonly SemaphoreSlim _pages;
    private readonly SemaphoreSlim _launchGate = new(1, 1);
    private readonly ConcurrentDictionary<string, Task<IBrowserContext>> _contexts = new(StringComparer.Ordinal);
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public PlaywrightDownloadHandler(Crawler crawler)
    {
        var s = crawler.Settings;
        _http = new HttpDownloadHandler(crawler);
        _logger = crawler.LoggerFactory.CreateLogger("scrapynet.playwright");
        _stats = crawler.Stats;
        _browserType = (s.GetString("PLAYWRIGHT_BROWSER_TYPE") ?? "chromium").ToLowerInvariant();
        _headless = s.GetBool("PLAYWRIGHT_HEADLESS", true);
        _timeout = (float)s.GetDouble("PLAYWRIGHT_NAVIGATION_TIMEOUT", 30000);
        _blocked = [.. s.GetList("PLAYWRIGHT_BLOCKED_RESOURCE_TYPES")];
        _pages = new SemaphoreSlim(Math.Max(1, s.GetInt("PLAYWRIGHT_MAX_PAGES", 8)));
    }

    public Task<Response> DownloadAsync(Request request, Spider spider, CancellationToken cancellationToken) =>
        request.GetMeta(PlaywrightMeta.Enabled, false) ? RenderAsync(request, cancellationToken) : _http.DownloadAsync(request, spider, cancellationToken);

    private async Task<Response> RenderAsync(Request request, CancellationToken cancellationToken)
    {
        var contextName = request.GetMeta<string>(PlaywrightMeta.Context) ?? "default";
        var context = await GetContextAsync(contextName).ConfigureAwait(false);
        await _pages.WaitAsync(cancellationToken).ConfigureAwait(false);
        IPage? page = null;
        var keepPage = request.GetMeta(PlaywrightMeta.IncludePage, false);
        try
        {
            page = await context.NewPageAsync().ConfigureAwait(false);
            _stats.Inc("playwright/page_count");
            if (_blocked.Count > 0)
            {
                await page.RouteAsync("**/*", async route =>
                {
                    if (_blocked.Contains(route.Request.ResourceType))
                    {
                        _stats.Inc("playwright/request_count/aborted");
                        await route.AbortAsync().ConfigureAwait(false);
                    }
                    else
                    {
                        await route.ContinueAsync().ConfigureAwait(false);
                    }
                }).ConfigureAwait(false);
            }
            // One call with every header: each SetExtraHTTPHeadersAsync call replaces the previous set.
            var extraHeaders = request.Headers
                .Where(h => !h.Key.Equals("Cookie", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(h => h.Key, h => string.Join(", ", h.Value));
            if (extraHeaders.Count > 0) await page.SetExtraHTTPHeadersAsync(extraHeaders).ConfigureAwait(false);

            var waitUntil = (request.GetMeta<string>(PlaywrightMeta.WaitUntil) ?? "load").ToLowerInvariant() switch
            {
                "domcontentloaded" => WaitUntilState.DOMContentLoaded,
                "networkidle" => WaitUntilState.NetworkIdle,
                "commit" => WaitUntilState.Commit,
                _ => WaitUntilState.Load,
            };
            var started = DateTimeOffset.UtcNow;
            var navigation = await page.GotoAsync(request.Url, new PageGotoOptions { WaitUntil = waitUntil, Timeout = _timeout }).ConfigureAwait(false);
            request.Meta[MetaKeys.DownloadLatency] = (DateTimeOffset.UtcNow - started).TotalSeconds;

            if (request.Meta.GetValueOrDefault(PlaywrightMeta.PageMethods) is IEnumerable<PageMethod> methods)
                foreach (var method in methods) await RunAsync(page, method, request).ConfigureAwait(false);

            var html = await page.ContentAsync().ConfigureAwait(false);
            var headers = new Headers();
            if (navigation is not null)
                foreach (var (k, v) in await navigation.AllHeadersAsync().ConfigureAwait(false))
                    if (!k.Equals("content-encoding", StringComparison.OrdinalIgnoreCase) && !k.Equals("content-length", StringComparison.OrdinalIgnoreCase))
                        headers.Set(k, v);
            headers.Set("Content-Type", "text/html; charset=utf-8");

            var response = new HtmlResponse(page.Url, navigation?.Status ?? 200, headers, Encoding.UTF8.GetBytes(html), request, ["playwright"], encoding: Encoding.UTF8);
            _stats.Inc("playwright/response_count");
            if (keepPage) request.Meta[PlaywrightMeta.Page] = page;
            return response;
        }
        catch (TimeoutException ex)
        {
            throw new DownloadTimeoutException($"Playwright timed out rendering {request.Url}: {ex.Message}", ex);
        }
        finally
        {
            if (page is not null && !keepPage) await page.CloseAsync().ConfigureAwait(false);
            _pages.Release();
        }
    }

    private async Task RunAsync(IPage page, PageMethod method, Request request)
    {
        var a = method.Args;
        switch (method.Method.ToLowerInvariant())
        {
            case "wait_for_selector":
                await page.WaitForSelectorAsync((string)a[0]!, new PageWaitForSelectorOptions { Timeout = a.Length > 1 && a[1] is float t ? t : _timeout }).ConfigureAwait(false);
                break;
            case "click":
                await page.ClickAsync((string)a[0]!).ConfigureAwait(false);
                break;
            case "fill":
                await page.FillAsync((string)a[0]!, (string)a[1]!).ConfigureAwait(false);
                break;
            case "press":
                await page.PressAsync((string)a[0]!, (string)a[1]!).ConfigureAwait(false);
                break;
            case "evaluate":
                var result = await page.EvaluateAsync<object?>((string)a[0]!).ConfigureAwait(false);
                if (request.Meta.GetValueOrDefault(PlaywrightMeta.EvaluateResults) is not List<object?> results)
                    request.Meta[PlaywrightMeta.EvaluateResults] = results = [];
                results.Add(result);
                break;
            case "wait_for_timeout":
                await page.WaitForTimeoutAsync(Convert.ToSingle(a[0], System.Globalization.CultureInfo.InvariantCulture)).ConfigureAwait(false);
                break;
            case "wait_for_load_state":
                await page.WaitForLoadStateAsync((string)a[0]! switch
                {
                    "load" => LoadState.Load,
                    "domcontentloaded" => LoadState.DOMContentLoaded,
                    _ => LoadState.NetworkIdle,
                }).ConfigureAwait(false);
                break;
            case "scroll_to_bottom":
                var times = Convert.ToInt32(a[0], System.Globalization.CultureInfo.InvariantCulture);
                var pause = Convert.ToSingle(a[1], System.Globalization.CultureInfo.InvariantCulture);
                for (var i = 0; i < times; i++)
                {
                    await page.EvaluateAsync("window.scrollTo(0, document.body.scrollHeight)").ConfigureAwait(false);
                    await page.WaitForTimeoutAsync(pause).ConfigureAwait(false);
                }
                break;
            case "screenshot":
                request.Meta[PlaywrightMeta.Screenshot] = await page.ScreenshotAsync(new PageScreenshotOptions { FullPage = a.Length == 0 || a[0] is true }).ConfigureAwait(false);
                break;
            default:
                throw new NotSupportedException($"Unknown page method '{method.Method}'.");
        }
    }

    private Task<IBrowserContext> GetContextAsync(string name) =>
        _contexts.GetOrAdd(name, async _ =>
        {
            var browser = await GetBrowserAsync().ConfigureAwait(false);
            return await browser.NewContextAsync(new BrowserNewContextOptions { IgnoreHTTPSErrors = false }).ConfigureAwait(false);
        });

    private async Task<IBrowser> GetBrowserAsync()
    {
        if (_browser is not null) return _browser;
        await _launchGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_browser is not null) return _browser;
            _playwright = await Microsoft.Playwright.Playwright.CreateAsync().ConfigureAwait(false);
            var type = _browserType switch
            {
                "firefox" => _playwright.Firefox,
                "webkit" => _playwright.Webkit,
                _ => _playwright.Chromium,
            };
            _logger.LogInformation("Launching {Browser} (headless: {Headless})", _browserType, _headless);
            _browser = await type.LaunchAsync(new BrowserTypeLaunchOptions { Headless = _headless }).ConfigureAwait(false);
            return _browser;
        }
        finally
        {
            _launchGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var ctx in _contexts.Values)
        {
            try
            {
                await (await ctx.ConfigureAwait(false)).CloseAsync().ConfigureAwait(false);
            }
            catch (PlaywrightException)
            {
            }
        }
        if (_browser is not null) await _browser.CloseAsync().ConfigureAwait(false);
        _playwright?.Dispose();
        _http.Dispose();
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
}

/// <summary>Configuration helpers.</summary>
public static class PlaywrightExtensions
{
    /// <summary>Routes http and https through the Playwright handler (only requests with <c>meta["playwright"]</c> use the browser).</summary>
    public static Settings UsePlaywright(this Settings settings, string browser = "chromium", bool headless = true, int maxPages = 8, params string[] blockResourceTypes)
    {
        settings.SetDownloadHandler("http", typeof(PlaywrightDownloadHandler));
        settings.SetDownloadHandler("https", typeof(PlaywrightDownloadHandler));
        settings.Set("PLAYWRIGHT_BROWSER_TYPE", browser);
        settings.Set("PLAYWRIGHT_HEADLESS", headless);
        settings.Set("PLAYWRIGHT_MAX_PAGES", maxPages);
        if (blockResourceTypes.Length > 0) settings.Set("PLAYWRIGHT_BLOCKED_RESOURCE_TYPES", string.Join(",", blockResourceTypes));
        return settings;
    }

    /// <summary>Marks a request for browser rendering, with optional page actions.</summary>
    public static Request WithPlaywright(this Request request, params PageMethod[] methods)
    {
        var copy = request with { };
        copy.Meta[PlaywrightMeta.Enabled] = true;
        if (methods.Length > 0) copy.Meta[PlaywrightMeta.PageMethods] = methods.ToList();
        return copy;
    }
}

/// <summary>Browser installation.</summary>
public static class PlaywrightSetup
{
    /// <summary>Downloads browser binaries (<c>chromium</c>, <c>firefox</c>, <c>webkit</c>). Returns the installer's exit code.</summary>
    public static int InstallBrowsers(params string[] browsers) =>
        Microsoft.Playwright.Program.Main(["install", .. (browsers.Length == 0 ? ["chromium"] : browsers)]);
}
