# 15. Troubleshooting

**My spider finishes immediately with 0 items.**
Look at the log at `INFO`: offsite filtering (`AllowedDomains` doesn't include the start URL's host),
robots.txt (`ROBOTSTXT_OBEY` with a disallowing rule — stat `robotstxt/forbidden`), or HTTP errors
(`Ignoring response <404 ...>`). Run `scrapynet parse URL --spider NAME` to see exactly what a
callback yields.

**Selectors return nothing but the page shows the data in a browser.**
The content is probably built by JavaScript. Check with `scrapynet view URL`; if the data is missing
there, use [Playwright](09-javascript.md). Also check the JSON calls the page makes — scraping the
API directly is faster.

**`Missing scheme in request url`.**
Requests need absolute URLs. Use `response.Follow(href)` or `response.UrlJoin(href)` for relative links.

**Requests seem to be skipped.**
The duplicate filter drops requests it has seen (`dupefilter/filtered` in stats). Set
`DontFilter = true` on requests that must repeat, or check that your URLs don't differ only by
parameters that don't matter.

**A pause/resume run starts over.**
Stop gracefully (one Ctrl+C), use the same `JOBDIR`, and make callbacks named spider methods — lambdas
can't be saved (`scheduler/unserializable` counts them).

**`NotConfiguredException` in the log / a middleware isn't running.**
Built-in components switch themselves off when their setting is missing (`FILES_STORE`,
`HTTPCACHE_ENABLED`, `PROXY_LIST`, ...). Set it, and check the "Enabled downloader middlewares" lines
logged at startup.

**Timeouts on slow sites.**
Raise `DOWNLOAD_TIMEOUT` (or `meta["download_timeout"]` for one request), lower concurrency per
domain, and enable AutoThrottle.

**Getting 403 or 429.**
Identify yourself with a real `USER_AGENT`, slow down (`DOWNLOAD_DELAY`, AutoThrottle), obey
robots.txt, and add `RETRY_BACKOFF_BASE` for 429s. If the site allows crawling through proxies, use
`ProxyRotationMiddleware`.

**Playwright: `Executable doesn't exist`.**
Install the browsers once: `pwsh bin/Debug/net10.0/playwright.ps1 install chromium` or
`PlaywrightSetup.InstallBrowsers("chromium")`.

**JSON serialization errors in a `dotnet run file.cs` script.**
File-based apps turn reflection-based JSON off. Scrapy.Net is unaffected; for your own types add
`#:property JsonSerializerIsReflectionEnabledByDefault=true`.

**Tests: `dotnet test` fails with MSB4025.**
xunit.v3 runs on Microsoft.Testing.Platform; run the test project directly:
`dotnet run --project tests/ScrapyNet.Tests`.

**LLM calls fail with "Unsupported parameter: max_tokens" or "temperature".**
Reasoning models need `max_completion_tokens` and the default temperature: use
`OpenAiChatModel.ForAzure(...)`, or set `TokenLimitParameter = "max_completion_tokens"` and
`Temperature = null`.
