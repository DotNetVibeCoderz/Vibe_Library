using ScrapyGallery.Infrastructure;
using ScrapyNet;
using ScrapyNet.Sandbox;

namespace ScrapyGallery.Demos;

public sealed class LoginDemo : Demo
{
    public override DemoGroup Group => DemoGroup.Crawl;
    public override string Title => "Logins, forms and sessions";
    public override string TitleId => "Login, formulir, dan sesi";
    public override string Summary => "Submit the login form with FormRequest.FromResponse, which keeps the hidden CSRF token. The session cookie is stored by the cookies middleware and sent with every page behind the login.";
    public override string SummaryId => "Kirim formulir login dengan FormRequest.FromResponse yang mempertahankan token CSRF tersembunyi. Cookie sesi disimpan middleware cookie dan dikirim ke setiap halaman di balik login.";
    public override IReadOnlyList<string> Concepts => ["FormRequest.FromResponse", "CSRF", "CookiesMiddleware", "cookiejar"];

    public override IReadOnlyList<DemoInput> Inputs =>
    [
        new("user", "Username", "Nama pengguna", SandboxSite.Username),
        new("password", "Password (try a wrong one)", "Kata sandi (coba yang salah)", SandboxSite.Password),
    ];

    // <demo>
    public sealed class LoginSpider(string loginUrl, string user, string password) : Spider
    {
        public override IReadOnlyList<string> StartUrls => [loginUrl];

        public override async IAsyncEnumerable<object> Parse(Response response)
        {
            yield return FormRequest.FromResponse(response,
                [new("username", user), new("password", password)],
                callback: AfterLogin);
            await Task.CompletedTask;
        }

        private IEnumerable<object> AfterLogin(Response response)
        {
            if (response.Css("p.error::text").Get() is { } error)
            {
                yield return new { Result = "login failed", Detail = error };
                yield break;
            }
            yield return new { Result = "logged in", Detail = response.Css("h1.welcome::text").Get() };
            foreach (var order in response.Css("li.order::text").GetAll())
                yield return new { Result = "order", Detail = order };
        }
    }
    // </demo>

    public override async Task RunAsync(DemoContext ctx)
    {
        var result = await ctx.CrawlAsync(new LoginSpider(ctx.Site.Url("/login"), ctx.Input("user"), ctx.Input("password")), ctx.NewSettings());
        ctx.Print(result.ToString());
    }
}
