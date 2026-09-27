namespace ScrapyNet.Cmdline;

/// <summary>
/// Downloads single requests through the full engine (middlewares, cookies, retries, proxies) without
/// writing a spider — the machinery behind <c>scrapynet fetch</c>, the shell, and notebook one-liners.
/// </summary>
/// <example>
/// <code>
/// var response = await Fetcher.FetchAsync("https://quotes.toscrape.com/");
/// response.Css("span.text::text").GetAll();
/// </code>
/// </example>
public static class Fetcher
{
    /// <summary>Fetches a URL with default (or configured) settings.</summary>
    public static Task<Response> FetchAsync(string url, Action<Settings>? configure = null, CancellationToken cancellationToken = default) =>
        FetchAsync(new Request(url), configure, cancellationToken);

    /// <summary>Fetches a request. Non-2xx responses are returned too, not treated as errors.</summary>
    public static async Task<Response> FetchAsync(Request request, Action<Settings>? configure = null, CancellationToken cancellationToken = default)
    {
        var settings = new Settings();
        settings.Set(SettingKeys.LogLevel, "WARNING", SettingPriority.Command);
        settings.Set(SettingKeys.StatsDump, false, SettingPriority.Command);
        settings.Set(SettingKeys.LogStatsInterval, 0, SettingPriority.Command);
        // Error pages are answers too: hand them back instead of filtering them (redirects still followed).
        settings.Set(SettingKeys.HttpErrorAllowAll, true, SettingPriority.Command);
        configure?.Invoke(settings);
        return await FetchAsync(request, settings, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<Response> FetchAsync(Request request, Settings settings, CancellationToken cancellationToken = default)
    {
        var spider = new FetchSpider(request);
        await new Crawler(spider, settings).CrawlAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        if (spider.Failure is { } failure)
        {
            if (failure.Response is { } errorResponse) return errorResponse;
            throw new HttpRequestException($"Fetching {request.Url} failed: {failure.Message}", failure.Exception);
        }
        return spider.Response ?? throw new InvalidOperationException($"No response for {request.Url}");
    }

    private sealed class FetchSpider(Request request) : Spider
    {
        public override string Name => "fetch";

        public Response? Response { get; private set; }

        public Failure? Failure { get; private set; }

        public override async IAsyncEnumerable<Request> StartAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var r = request with { DontFilter = true, Callback = Callbacks.FromSync<Response>(Capture) };
            yield return r.WithErrback(f => Failure = f);
            await Task.CompletedTask.ConfigureAwait(false);
        }

        private IEnumerable<object> Capture(Response response)
        {
            Response = response;
            return [];
        }
    }
}
