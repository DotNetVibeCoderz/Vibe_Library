using ScrapyGallery.Infrastructure;
using ScrapyNet;
using ScrapyNet.Contracts;

namespace ScrapyGallery.Demos;

public sealed class ContractsDemo : Demo
{
    public override DemoGroup Group => DemoGroup.Operate;
    public override string Title => "Spider contracts";
    public override string TitleId => "Kontrak spider";
    public override string Summary => "Attributes on a callback say which page to test it on and what it must return. scrapynet check fetches each page once, runs the callback and reports what held: a quick test that a site's layout has not changed. One contract here fails on purpose.";
    public override string SummaryId => "Atribut pada callback menyebut halaman uji dan apa yang harus dikembalikan. scrapynet check mengambil setiap halaman sekali, menjalankan callback, lalu melaporkan mana yang terpenuhi: tes cepat bahwa tata letak situs belum berubah. Satu kontrak di sini sengaja gagal.";
    public override IReadOnlyList<string> Concepts => ["[Url]", "[Returns]", "[Scrapes]", "scrapynet check", "ContractRunner"];
    public override bool ShowsWeb => false;

    // <demo>
    public sealed class BooksSpider(string site) : Spider
    {
        public override string Name => "books";
        public override IReadOnlyList<string> StartUrls => [site];

        [Url("/books/")]
        [Returns("requests", 20)]          // twenty books plus "next"
        [Returns("items", 0, 0)]
        public override async IAsyncEnumerable<object> Parse(Response response)
        {
            foreach (var href in response.Css("article.product_pod h3 a::attr(href)").GetAll())
                yield return response.Follow(href, ParseBook);
            if (response.Css("li.next a::attr(href)").Get() is { } next)
                yield return response.Follow(next, Parse);
            await Task.CompletedTask;
        }

        [Url("/books/catalogue/the-silent-harbor_12/index.html")]
        [Returns("items", 1, 1)]
        [Scrapes("title", "price", "isbn")]    // the page has no ISBN: this contract fails
        public async IAsyncEnumerable<object> ParseBook(Response response)
        {
            yield return new Dictionary<string, object?>
            {
                ["title"] = response.Css("h1::text").Get(),
                ["price"] = response.Css("p.price_color::text").Get(),
            };
            await Task.CompletedTask;
        }
    }
    // </demo>

    public override async Task RunAsync(DemoContext ctx)
    {
        var spider = new BooksSpider(ctx.Site.Url("/"));
        var report = await ContractRunner.CheckAsync([spider], ctx.NewSettings(), ctx.CancellationToken);
        foreach (var r in report.Results)
            ctx.Observer.AddItem(new { Callback = r.Method, Check = r.Check, Outcome = r.Outcome.ToString(), r.Message });
        ctx.Print("$ scrapynet check books\n" + report.Format());
    }
}
