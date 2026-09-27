using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;

namespace ScrapyNet.Sandbox;

/// <summary>Options for a <see cref="SandboxSite"/>.</summary>
public sealed record SandboxOptions
{
    /// <summary>Port to listen on (0 = pick a free one).</summary>
    public int Port { get; init; }

    /// <summary>Name reported in <c>X-Sandbox-Proxy</c> when the site is used as a proxy.</summary>
    public string Name { get; init; } = "sandbox";

    /// <summary>Answer 403 to proxied requests after this many (simulates a proxy getting banned). 0 = never.</summary>
    public int BanProxyAfter { get; init; }

    /// <summary>Extra latency on every response, in milliseconds.</summary>
    public int LatencyMs { get; init; }
}

/// <summary>
/// A self-contained practice website for learning and testing Scrapy.Net offline, in the spirit of
/// quotes.toscrape.com and books.toscrape.com, plus the awkward cases real crawlers meet: logins with
/// CSRF tokens, authenticated APIs, sitemaps (gzipped too), RSS/CSV feeds, robots.txt, redirects, meta
/// refresh, flaky and slow endpoints, dropped connections, conditional requests, a link graph with
/// cycles, a JavaScript-rendered page, and a forward-proxy mode.
/// </summary>
/// <example>
/// <code>
/// await using var site = await SandboxSite.StartAsync();
/// Console.WriteLine(site.Url("/quotes/"));   // http://127.0.0.1:PORT/quotes/
/// </code>
/// </example>
public sealed class SandboxSite : IAsyncDisposable
{
    public const string ApiKey = "sandbox-key";
    public const string BearerToken = "sandbox-token";
    public const string Username = "scrapy";
    public const string Password = "net";

    // Explicit resolver: keeps working where reflection-based JSON is off by default (.NET 10 file-based apps).
    private static readonly JsonSerializerOptions JsonOptions = new() { TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver() };

    private readonly SandboxServer _server;
    private readonly SandboxOptions _options;
    private readonly ConcurrentDictionary<string, int> _flaky = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _sessions = new(StringComparer.Ordinal);
    private int _proxied;
    private int _liveVersion = 1;

    private SandboxSite(SandboxOptions options)
    {
        _options = options;
        _server = new SandboxServer(HandleAsync, options.Port);
    }

    /// <summary>Starts a site on a free loopback port (or <see cref="SandboxOptions.Port"/>).</summary>
    public static Task<SandboxSite> StartAsync(SandboxOptions? options = null)
    {
        var site = new SandboxSite(options ?? new SandboxOptions());
        site._server.Start();
        return Task.FromResult(site);
    }

    public string BaseUrl => _server.BaseUrl;

    public int Port => _server.Port;

    public string Name => _options.Name;

    /// <summary>Absolute URL for a path on this site.</summary>
    public string Url(string path) => BaseUrl + (path.StartsWith('/') ? path : "/" + path);

    /// <summary>The site's address in proxy form, for <c>meta["proxy"]</c> or <c>PROXY_LIST</c>.</summary>
    public string ProxyUrl => BaseUrl;

    public IReadOnlyCollection<SandboxRequest> Requests => _server.RequestLog;

    public long RequestCount => _server.RequestCount;

    /// <summary>Makes <c>/news/live</c> change (for change-detection demos).</summary>
    public void BumpLiveVersion() => Interlocked.Increment(ref _liveVersion);

    public ValueTask DisposeAsync() => _server.DisposeAsync();

    private async Task<SandboxResponse> HandleAsync(SandboxRequest request)
    {
        var response = Route(request);
        if (request.ViaProxy)
        {
            var count = Interlocked.Increment(ref _proxied);
            if (_options.BanProxyAfter > 0 && count > _options.BanProxyAfter)
                response = SandboxResponse.Html("<h1>Banned</h1>", 403);
            response.WithHeader("X-Sandbox-Proxy", _options.Name);
        }
        if (_options.LatencyMs > 0) await Task.Delay(_options.LatencyMs).ConfigureAwait(false);
        return response;
    }

    private SandboxResponse Route(SandboxRequest r)
    {
        var p = r.Path;
        var segments = p.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        try
        {
            return p switch
            {
                "/" => Home(),
                "/robots.txt" => SandboxResponse.Text("User-agent: *\nDisallow: /private/\nAllow: /private/public-note.html\nCrawl-delay: 1\n\nUser-agent: BadBot\nDisallow: /\n\nSitemap: " + BaseUrl + "/sitemap.xml\n"),
                "/sitemap.xml" => SitemapIndex(),
                "/sitemap-books.xml" => SitemapBooks(),
                "/sitemap-quotes.xml.gz" => SitemapQuotesGz(),
                "/quotes" or "/quotes/" => QuotesPage(1, null),
                "/quotes/js" or "/quotes/js/" => QuotesJs(),
                "/api/quotes" => ApiQuotes(r),
                "/api/products" => ApiProducts(r, secure: false),
                "/api/secure/products" => ApiProducts(r, secure: true),
                "/books" or "/books/" or "/books/index.html" => BooksPage(1, null),
                "/login" when r.Method == "POST" => LoginPost(r),
                "/login" => LoginForm(),
                "/logout" => Logout(r),
                "/account" => Account(r),
                "/feeds/products.xml" => FeedXml(),
                "/feeds/products.csv" => FeedCsv(),
                "/files" or "/files/" => FilesIndex(),
                "/headers" => SandboxResponse.Json(JsonSerializer.Serialize(r.Headers, JsonOptions)),
                "/cookies" => SandboxResponse.Json(JsonSerializer.Serialize(r.Cookies(), JsonOptions)),
                "/cookies/set" => SetCookies(r),
                "/slow" => new SandboxResponse { Body = Encoding.UTF8.GetBytes(Layout("Slow", "<p class=\"slow\">finally</p>")), DelayMs = int.Parse(r.QueryValue("ms") ?? "500", CultureInfo.InvariantCulture) },
                "/drop" => new SandboxResponse { Abort = true },
                "/meta-refresh" => SandboxResponse.Html("<html><head><meta http-equiv=\"refresh\" content=\"0; url=/quotes/\"></head><body>Moving...</body></html>"),
                "/ip" => SandboxResponse.Json(JsonSerializer.Serialize(new { proxy = r.ViaProxy ? _options.Name : null, via_proxy = r.ViaProxy }, JsonOptions)),
                "/news" or "/news/" => NewsIndex(),
                "/news/live" => NewsLive(r),
                "/private/secret.html" => SandboxResponse.Html(Layout("Secret", "<p>You should not be here.</p>")),
                "/private/public-note.html" => SandboxResponse.Html(Layout("Public note", "<p>This one page is allowed.</p>")),
                "/infinite" => SandboxResponse.Redirect("/graph/0"),
                _ => RouteDynamic(r, segments),
            };
        }
        catch (FormatException)
        {
            return SandboxResponse.Html("<h1>400 Bad Request</h1>", 400);
        }
    }

    private SandboxResponse RouteDynamic(SandboxRequest r, string[] s)
    {
        if (s is ["quotes", "page", var n, ..] && int.TryParse(n, out var page)) return QuotesPage(page, null);
        if (s is ["quotes", "tag", var tag, ..])
            return QuotesPage(s.Length >= 5 && s[3] == "page" && int.TryParse(s[4], out var tp) ? tp : 1, tag);
        if (s is ["quotes", "author", var slug, ..]) return Author(slug);
        if (s is ["books", "catalogue", var file] && file.StartsWith("page-", StringComparison.Ordinal))
            return BooksPage(int.Parse(file[5..].Replace(".html", ""), CultureInfo.InvariantCulture), null);
        if (s is ["books", "catalogue", var dir, "index.html"]) return BookDetail(dir);
        if (s is ["books", "category", var cat, ..]) return BooksPage(1, cat);
        if (s is ["books", "media", var img] && int.TryParse(img.Replace(".png", ""), out var bookId)) return BookImage(bookId);
        if (s is ["status", var code] && int.TryParse(code, out var status)) return SandboxResponse.Html(Layout($"Status {status}", $"<p class=\"status\">{status}</p>"), status);
        if (s is ["flaky", var key])
        {
            var fail = int.Parse(r.QueryValue("fail") ?? "2", CultureInfo.InvariantCulture);
            var failCode = int.Parse(r.QueryValue("code") ?? "503", CultureInfo.InvariantCulture);
            var seen = _flaky.AddOrUpdate(key, 1, (_, v) => v + 1);
            return seen <= fail
                ? SandboxResponse.Html(Layout("Try again", "<p>temporarily unavailable</p>"), failCode)
                : SandboxResponse.Html(Layout("Flaky", $"<p class=\"result\">ok after {seen - 1} failures</p>"));
        }
        if (s is ["redirect", var hops] && int.TryParse(hops, out var h))
            return h <= 0 ? SandboxResponse.Html(Layout("Redirect target", "<p class=\"final\">arrived</p>")) : SandboxResponse.Redirect($"/redirect/{h - 1}", h % 2 == 0 ? 301 : 302);
        if (s is ["graph", var node] && int.TryParse(node, out var g)) return Graph(g);
        if (s is ["files", var fileName] && fileName.EndsWith(".pdf", StringComparison.Ordinal)) return Pdf(fileName);
        if (s is ["news", var articleSlug]) return Article(articleSlug);
        return SandboxResponse.NotFound();
    }

    // ---- layout ---------------------------------------------------------------------------------

    private static string Layout(string title, string body, string head = "") =>
        $$"""
        <!DOCTYPE html>
        <html lang="en">
        <head>
          <meta charset="UTF-8">
          <title>{{WebUtility.HtmlEncode(title)}} | Scrapy.Net Sandbox</title>
          {{head}}
        </head>
        <body>
          <header class="site-header"><a href="/">Scrapy.Net Sandbox</a>
            <nav><a href="/quotes/">Quotes</a> <a href="/books/">Books</a> <a href="/news/">News</a> <a href="/login">Login</a></nav>
          </header>
          <main class="container">
        {{body}}
          </main>
          <footer class="footer">Made by Gravicode Studios, led by Kang Fadhil. Practice data only.</footer>
        </body>
        </html>
        """;

    private static string E(string s) => WebUtility.HtmlEncode(s);

    private SandboxResponse Home() => SandboxResponse.Html(Layout("Home", """
        <h1>Scrapy.Net Sandbox</h1>
        <ul class="sections">
          <li><a href="/quotes/">Quotes to scrape</a></li>
          <li><a href="/quotes/js/">Quotes rendered by JavaScript</a></li>
          <li><a href="/books/">Books to scrape</a></li>
          <li><a href="/news/">Newsroom (articles, JSON-LD)</a></li>
          <li><a href="/login">Login form</a></li>
          <li><a href="/files/">Downloadable files</a></li>
          <li><a href="/graph/0">Link graph</a></li>
          <li><a href="/api/products?page=1">JSON API</a></li>
          <li><a href="/feeds/products.xml">XML feed</a> · <a href="/feeds/products.csv">CSV feed</a></li>
        </ul>
        """));

    // ---- quotes ---------------------------------------------------------------------------------

    private static SandboxResponse QuotesPage(int page, string? tag)
    {
        var all = tag is null ? SandboxData.Quotes : SandboxData.Quotes.Where(q => q.Tags.Contains(tag)).ToList();
        var pages = Math.Max(1, (all.Count + SandboxData.QuotesPerPage - 1) / SandboxData.QuotesPerPage);
        if (page < 1 || page > pages) return SandboxResponse.Html(Layout("Quotes", "<p>No quotes found!</p>"), 200);
        var sb = new StringBuilder();
        sb.Append(tag is null ? "<h1>Quotes</h1>" : $"<h1>Viewing tag: <a href=\"/quotes/tag/{E(tag)}/\">{E(tag)}</a></h1>");
        sb.Append("<div class=\"col-md-8\">");
        foreach (var q in all.Skip((page - 1) * SandboxData.QuotesPerPage).Take(SandboxData.QuotesPerPage))
        {
            sb.Append($"""
                <div class="quote" itemscope itemtype="http://schema.org/CreativeWork" data-id="{q.Id}">
                  <span class="text" itemprop="text">“{E(q.Text)}”</span>
                  <span>by <small class="author" itemprop="author">{E(q.Author)}</small>
                  <a href="/quotes/author/{q.AuthorSlug}/">(about)</a></span>
                  <div class="tags">Tags: <meta class="keywords" itemprop="keywords" content="{E(string.Join(",", q.Tags))}">
                """);
            foreach (var t in q.Tags) sb.Append($"<a class=\"tag\" href=\"/quotes/tag/{t}/page/1/\">{t}</a> ");
            sb.Append("</div></div>\n");
        }
        sb.Append("</div><nav><ul class=\"pager\">");
        var prefix = tag is null ? "/quotes" : $"/quotes/tag/{tag}";
        if (page > 1) sb.Append($"<li class=\"previous\"><a href=\"{prefix}/page/{page - 1}/\"><span>&larr;</span> Previous</a></li>");
        if (page < pages) sb.Append($"<li class=\"next\"><a href=\"{prefix}/page/{page + 1}/\">Next <span>&rarr;</span></a></li>");
        sb.Append("</ul></nav>");
        return SandboxResponse.Html(Layout($"Quotes page {page}", sb.ToString()));
    }

    private static SandboxResponse Author(string slug)
    {
        var a = SandboxData.Authors.FirstOrDefault(x => x.Slug == slug);
        if (a is null) return SandboxResponse.NotFound();
        return SandboxResponse.Html(Layout(a.Name, $"""
            <div class="author-details">
              <h3 class="author-title">{E(a.Name)}</h3>
              <p><strong>Born:</strong> <span class="author-born-date">{E(a.Born)}</span> <span class="author-born-location">{E(a.Location)}</span></p>
              <div class="author-description">{E(a.Bio)}</div>
            </div>
            """));
    }

    private static SandboxResponse QuotesJs() => SandboxResponse.Html(Layout("Quotes (JavaScript)", """
        <h1>Quotes rendered with JavaScript</h1>
        <div id="quotes"><p class="loading">Loading...</p></div>
        <script>
          fetch('/api/quotes?page=1').then(r => r.json()).then(data => {
            const box = document.getElementById('quotes');
            box.innerHTML = '';
            for (const q of data.quotes) {
              const div = document.createElement('div');
              div.className = 'quote';
              div.innerHTML = '<span class="text"></span> <small class="author"></small>';
              div.querySelector('.text').textContent = q.text;
              div.querySelector('.author').textContent = q.author;
              box.appendChild(div);
            }
          });
        </script>
        """));

    private static SandboxResponse ApiQuotes(SandboxRequest r)
    {
        var page = int.Parse(r.QueryValue("page") ?? "1", CultureInfo.InvariantCulture);
        var items = SandboxData.Quotes.Skip((page - 1) * SandboxData.QuotesPerPage).Take(SandboxData.QuotesPerPage)
            .Select(q => new { id = q.Id, text = q.Text, author = q.Author, tags = q.Tags }).ToList();
        return SandboxResponse.Json(JsonSerializer.Serialize(new { page, has_next = page < SandboxData.QuotePages, quotes = items }, JsonOptions));
    }

    // ---- books ----------------------------------------------------------------------------------

    private static SandboxResponse BooksPage(int page, string? category)
    {
        var all = category is null ? SandboxData.Books : SandboxData.Books.Where(b => b.Category.Equals(category, StringComparison.OrdinalIgnoreCase)).ToList();
        var pages = Math.Max(1, (all.Count + SandboxData.BooksPerPage - 1) / SandboxData.BooksPerPage);
        if (page < 1 || page > pages) return SandboxResponse.NotFound();
        var sb = new StringBuilder();
        sb.Append("<div class=\"page-header\"><h1>").Append(category is null ? "All products" : E(category)).Append("</h1></div>");
        sb.Append("<aside class=\"sidebar\"><ul class=\"nav nav-list\">");
        foreach (var c in SandboxData.Categories) sb.Append($"<li><a href=\"/books/category/{c.ToLowerInvariant()}/index.html\">{c}</a></li>");
        sb.Append("</ul></aside>");
        sb.Append($"<form class=\"form-horizontal\"><strong>{all.Count}</strong> results - showing <strong>{(page - 1) * SandboxData.BooksPerPage + 1}</strong> to <strong>{Math.Min(all.Count, page * SandboxData.BooksPerPage)}</strong>.</form>");
        sb.Append("<ol class=\"row\">");
        foreach (var b in all.Skip((page - 1) * SandboxData.BooksPerPage).Take(SandboxData.BooksPerPage))
        {
            sb.Append($"""
                <li class="col-xs-6"><article class="product_pod">
                  <div class="image_container"><a href="{b.Url}"><img src="{b.ImageUrl}" alt="{E(b.Title)}" class="thumbnail"></a></div>
                  <p class="star-rating {b.RatingWord}"></p>
                  <h3><a href="{b.Url}" title="{E(b.Title)}">{E(b.Title)}</a></h3>
                  <div class="product_price"><p class="price_color">{SandboxData.Money(b.Price)}</p>
                  <p class="instock availability">{(b.Stock > 0 ? "In stock" : "Out of stock")}</p></div>
                </article></li>
                """);
        }
        sb.Append("</ol><ul class=\"pager\">");
        if (category is null)
        {
            if (page > 1) sb.Append($"<li class=\"previous\"><a href=\"/books/catalogue/page-{page - 1}.html\">previous</a></li>");
            sb.Append($"<li class=\"current\">Page {page} of {pages}</li>");
            if (page < pages) sb.Append($"<li class=\"next\"><a href=\"/books/catalogue/page-{page + 1}.html\">next</a></li>");
        }
        sb.Append("</ul>");
        return SandboxResponse.Html(Layout("Books", sb.ToString()));
    }

    private static SandboxResponse BookDetail(string dir)
    {
        var id = int.TryParse(dir[(dir.LastIndexOf('_') + 1)..], out var n) ? n : -1;
        var b = SandboxData.Books.FirstOrDefault(x => x.Id == id);
        if (b is null) return SandboxResponse.NotFound();
        return SandboxResponse.Html(Layout(b.Title, $"""
            <ul class="breadcrumb"><li><a href="/books/">Home</a></li><li><a href="/books/category/{b.Category.ToLowerInvariant()}/index.html">{b.Category}</a></li><li class="active">{E(b.Title)}</li></ul>
            <article class="product_page">
              <div class="item active"><img src="{b.ImageUrl}" alt="{E(b.Title)}"></div>
              <div class="product_main">
                <h1>{E(b.Title)}</h1>
                <p class="price_color">{SandboxData.Money(b.Price)}</p>
                <p class="instock availability">{(b.Stock > 0 ? $"In stock ({b.Stock} available)" : "Out of stock")}</p>
                <p class="star-rating {b.RatingWord}"></p>
              </div>
              <div id="product_description" class="sub-header"><h2>Product Description</h2></div>
              <p>{E(b.Description)}</p>
              <table class="table table-striped">
                <tr><th>UPC</th><td>{b.Upc}</td></tr>
                <tr><th>Product Type</th><td>Books</td></tr>
                <tr><th>Price (excl. tax)</th><td>{SandboxData.Money(b.Price)}</td></tr>
                <tr><th>Availability</th><td>{(b.Stock > 0 ? $"In stock ({b.Stock} available)" : "Out of stock")}</td></tr>
              </table>
            </article>
            """));
    }

    private static SandboxResponse BookImage(int id)
    {
        var b = SandboxData.Books.FirstOrDefault(x => x.Id == id);
        if (b is null) return SandboxResponse.NotFound();
        var hue = (id * 47) % 360;
        return SandboxResponse.Bytes(PngWriter.Solid(120 + id % 5 * 10, 180, hue), "image/png");
    }

    private static SandboxResponse ApiProducts(SandboxRequest r, bool secure)
    {
        if (secure && r.Header("X-Api-Key") != ApiKey && r.Header("Authorization") != "Bearer " + BearerToken)
            return SandboxResponse.Json("{\"error\":\"unauthorized\"}", 401);
        var page = int.Parse(r.QueryValue("page") ?? "1", CultureInfo.InvariantCulture);
        const int size = 20;
        var items = SandboxData.Books.Skip((page - 1) * size).Take(size)
            .Select(b => new { id = b.Id, title = b.Title, price = b.Price, rating = b.Rating, stock = b.Stock, category = b.Category, url = b.Url }).ToList();
        var next = page * size < SandboxData.Books.Count ? $"/api/{(secure ? "secure/" : "")}products?page={page + 1}" : null;
        return SandboxResponse.Json(JsonSerializer.Serialize(new { page, total = SandboxData.Books.Count, next, products = items }, JsonOptions));
    }

    // ---- login ----------------------------------------------------------------------------------

    private SandboxResponse LoginForm()
    {
        var token = Guid.NewGuid().ToString("N");
        _sessions["csrf:" + token] = "1";
        return SandboxResponse.Html(Layout("Login", $"""
            <form id="login" action="/login" method="post">
              <input type="hidden" name="csrf_token" value="{token}">
              <label>Username <input type="text" name="username"></label>
              <label>Password <input type="password" name="password"></label>
              <input type="checkbox" name="remember" value="yes" checked>
              <input type="submit" name="submit" value="Login">
            </form>
            """));
    }

    private SandboxResponse LoginPost(SandboxRequest r)
    {
        var form = r.Form();
        if (!form.TryGetValue("csrf_token", out var token) || !_sessions.TryRemove("csrf:" + token, out _))
            return SandboxResponse.Html(Layout("Login", "<p class=\"error\">Invalid CSRF token</p>"), 403);
        if (form.GetValueOrDefault("username") != Username || form.GetValueOrDefault("password") != Password)
            return SandboxResponse.Html(Layout("Login", "<p class=\"error\">Invalid username or password</p>"), 200);
        var session = Guid.NewGuid().ToString("N");
        _sessions["session:" + session] = Username;
        return SandboxResponse.Redirect("/account").WithHeader("Set-Cookie", $"session={session}; Path=/; HttpOnly");
    }

    private SandboxResponse Logout(SandboxRequest r)
    {
        if (r.Cookies().TryGetValue("session", out var s)) _sessions.TryRemove("session:" + s, out _);
        return SandboxResponse.Redirect("/login").WithHeader("Set-Cookie", "session=; Path=/; Max-Age=0");
    }

    private SandboxResponse Account(SandboxRequest r)
    {
        if (!r.Cookies().TryGetValue("session", out var s) || !_sessions.TryGetValue("session:" + s, out var user))
            return SandboxResponse.Redirect("/login");
        return SandboxResponse.Html(Layout("Account", $"""
            <h1 class="welcome">Welcome, {E(user)}!</h1>
            <ul class="orders"><li class="order">Order #1001 — The Silent Harbor</li><li class="order">Order #1002 — Paper Lantern</li></ul>
            <a href="/logout">Logout</a>
            """));
    }

    private static SandboxResponse SetCookies(SandboxRequest r)
    {
        var response = SandboxResponse.Redirect("/cookies");
        foreach (var part in r.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2);
            response.WithHeader("Set-Cookie", $"{kv[0]}={(kv.Length > 1 ? kv[1] : "")}; Path=/");
        }
        return response;
    }

    // ---- sitemaps and feeds -----------------------------------------------------------------------

    private SandboxResponse SitemapIndex() => SandboxResponse.Text($"""
        <?xml version="1.0" encoding="UTF-8"?>
        <sitemapindex xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
          <sitemap><loc>{BaseUrl}/sitemap-books.xml</loc><lastmod>2026-03-01</lastmod></sitemap>
          <sitemap><loc>{BaseUrl}/sitemap-quotes.xml.gz</loc><lastmod>2026-03-01</lastmod></sitemap>
        </sitemapindex>
        """, "application/xml");

    private SandboxResponse SitemapBooks()
    {
        var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\" xmlns:xhtml=\"http://www.w3.org/1999/xhtml\">\n");
        foreach (var b in SandboxData.Books)
            sb.Append($"  <url><loc>{BaseUrl}{b.Url}</loc><lastmod>2026-02-{1 + b.Id % 28:00}</lastmod><changefreq>weekly</changefreq><priority>0.{b.Rating + 3}</priority></url>\n");
        sb.Append("</urlset>");
        return SandboxResponse.Text(sb.ToString(), "application/xml");
    }

    private SandboxResponse SitemapQuotesGz()
    {
        var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">\n");
        for (var p = 1; p <= SandboxData.QuotePages; p++) sb.Append($"  <url><loc>{BaseUrl}/quotes/page/{p}/</loc></url>\n");
        foreach (var a in SandboxData.Authors) sb.Append($"  <url><loc>{BaseUrl}/quotes/author/{a.Slug}/</loc></url>\n");
        sb.Append("</urlset>");
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true)) gz.Write(Encoding.UTF8.GetBytes(sb.ToString()));
        return SandboxResponse.Bytes(ms.ToArray(), "application/x-gzip");
    }

    private SandboxResponse FeedXml()
    {
        var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<rss version=\"2.0\" xmlns:g=\"http://base.google.com/ns/1.0\"><channel><title>Sandbox products</title>\n");
        foreach (var b in SandboxData.Books)
            sb.Append($"<item><g:id>{b.Id}</g:id><title>{E(b.Title)}</title><link>{BaseUrl}{b.Url}</link><g:price>{b.Price.ToString(CultureInfo.InvariantCulture)} GBP</g:price><g:availability>{(b.Stock > 0 ? "in stock" : "out of stock")}</g:availability><category>{b.Category}</category></item>\n");
        sb.Append("</channel></rss>");
        return SandboxResponse.Text(sb.ToString(), "application/rss+xml; charset=utf-8");
    }

    private static SandboxResponse FeedCsv()
    {
        var sb = new StringBuilder("id,title,price,rating,stock,category\n");
        foreach (var b in SandboxData.Books)
            sb.Append(CultureInfo.InvariantCulture, $"{b.Id},\"{b.Title.Replace("\"", "\"\"")}\",{b.Price},{b.Rating},{b.Stock},{b.Category}\n");
        return SandboxResponse.Text(sb.ToString(), "text/csv; charset=utf-8");
    }

    // ---- files, graph, news -----------------------------------------------------------------------

    private static SandboxResponse FilesIndex()
    {
        var sb = new StringBuilder("<h1>Reports</h1><ul class=\"files\">");
        for (var i = 1; i <= 5; i++) sb.Append($"<li><a class=\"report\" href=\"/files/report-{i}.pdf\">Quarterly report {i}</a></li>");
        sb.Append("</ul>");
        return SandboxResponse.Html(Layout("Files", sb.ToString()));
    }

    private static SandboxResponse Pdf(string name)
    {
        var text = $"%PDF-1.4\n% Scrapy.Net sandbox document {name}\n1 0 obj << /Type /Catalog >> endobj\ntrailer << /Root 1 0 R >>\n%%EOF\n";
        return SandboxResponse.Bytes(Encoding.ASCII.GetBytes(text), "application/pdf");
    }

    private static SandboxResponse Graph(int n)
    {
        const int size = 200;
        if (n < 0 || n >= size) return SandboxResponse.NotFound();
        int[] links = [(n * 7 + 1) % size, (n * 13 + 5) % size, (n + 1) % size, n / 2];
        var sb = new StringBuilder($"<h1 class=\"node\">Node {n}</h1><ul class=\"links\">");
        foreach (var l in links.Distinct()) sb.Append($"<li><a href=\"/graph/{l}\">node {l}</a></li>");
        sb.Append("</ul>");
        return SandboxResponse.Html(Layout($"Node {n}", sb.ToString()));
    }

    private static SandboxResponse NewsIndex()
    {
        var sb = new StringBuilder("<h1>Newsroom</h1><div class=\"articles\">");
        foreach (var a in SandboxData.Articles)
            sb.Append($"<article class=\"teaser\"><h2><a href=\"/news/{a.Slug}\">{E(a.Title)}</a></h2><time datetime=\"{a.Published:yyyy-MM-dd}\">{a.Published:MMMM d, yyyy}</time></article>");
        sb.Append("</div>");
        return SandboxResponse.Html(Layout("News", sb.ToString()));
    }

    private SandboxResponse Article(string slug)
    {
        var a = SandboxData.Articles.FirstOrDefault(x => x.Slug == slug);
        if (a is null) return SandboxResponse.NotFound();
        var jsonLd = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "NewsArticle",
            ["headline"] = a.Title,
            ["author"] = new Dictionary<string, string> { ["@type"] = "Person", ["name"] = a.Author },
            ["datePublished"] = a.Published.ToString("O", CultureInfo.InvariantCulture),
            ["articleSection"] = a.Category,
        }, JsonOptions);
        var head = $"""
            <meta name="description" content="{E(a.Paragraphs[0])}">
            <meta property="og:title" content="{E(a.Title)}">
            <meta property="og:type" content="article">
            <meta property="article:published_time" content="{a.Published:O}">
            <meta name="author" content="{E(a.Author)}">
            <link rel="canonical" href="{BaseUrl}/news/{a.Slug}">
            <script type="application/ld+json">{jsonLd}</script>
            """;
        var sb = new StringBuilder();
        sb.Append("<aside class=\"sidebar\"><h3>Trending</h3><ul>");
        foreach (var t in SandboxData.Articles.Take(4)) sb.Append($"<li><a href=\"/news/{t.Slug}\">{E(t.Title)}</a></li>");
        sb.Append("</ul><div class=\"ad\">Advertisement: Try Scrapy.Net today!</div></aside>");
        sb.Append($"<article class=\"story\"><h1 class=\"headline\">{E(a.Title)}</h1><p class=\"byline\">By <span class=\"author\">{E(a.Author)}</span> · <time datetime=\"{a.Published:O}\">{a.Published:MMMM d, yyyy}</time></p>");
        sb.Append("<div class=\"article-body\">");
        foreach (var para in a.Paragraphs) sb.Append($"<p>{E(para)}</p>");
        sb.Append("</div></article><div class=\"share\">Share on social media</div>");
        return SandboxResponse.Html(Layout(a.Title, sb.ToString(), head));
    }

    private SandboxResponse NewsLive(SandboxRequest r)
    {
        var version = Volatile.Read(ref _liveVersion);
        var etag = $"\"v{version}\"";
        if (r.Header("If-None-Match") == etag) return new SandboxResponse { Status = 304 }.WithHeader("ETag", etag);
        return SandboxResponse.Html(Layout("Live blog", $"<article class=\"story\"><h1 class=\"headline\">Live blog</h1><div class=\"article-body\"><p>Update number {version}: the crawl continues.</p></div></article>"))
            .WithHeader("ETag", etag)
            .WithHeader("Last-Modified", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddHours(version).ToString("R", CultureInfo.InvariantCulture));
    }
}
