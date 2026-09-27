using ScrapyGallery.Infrastructure;
using ScrapyNet;
using ScrapyNet.Loaders;

namespace ScrapyGallery.Demos;

public sealed class LoadersDemo : Demo
{
    public override DemoGroup Group => DemoGroup.Extract;
    public override string Title => "Items and item loaders";
    public override string TitleId => "Item dan item loader";
    public override string Summary => "Typed C# items filled by an ItemLoader: processors clean whitespace, parse prices in any format, turn star-rating classes into numbers and resolve relative URLs as values are extracted.";
    public override string SummaryId => "Item C# bertipe diisi oleh ItemLoader: processor merapikan spasi, mengurai harga format apa pun, mengubah kelas rating bintang menjadi angka, dan menyelesaikan URL relatif.";
    public override IReadOnlyList<string> Concepts => ["ItemLoader<T>", "MapCompose", "Transforms.Price", "Transforms.Clean"];

    // <demo>
    public sealed class Book
    {
        public string? Title { get; set; }
        public decimal Price { get; set; }
        public int Rating { get; set; }
        public int Stock { get; set; }
        public string? Image { get; set; }
    }

    public sealed class BookPagesSpider(string listing) : Spider
    {
        public override IReadOnlyList<string> StartUrls => [listing];

        public override async IAsyncEnumerable<object> Parse(Response response)
        {
            foreach (var request in response.FollowAllCss("article.product_pod h3 a", ParseBook).Take(12))
                yield return request;
            await Task.CompletedTask;
        }

        private IEnumerable<object> ParseBook(Response response)
        {
            var loader = new ItemLoader<Book>(response);
            loader.AddCss(b => b.Title, "div.product_main h1::text", Transforms.Clean);
            loader.AddCss(b => b.Price, "div.product_main p.price_color::text", Transforms.Price);
            loader.AddCss(b => b.Rating, "div.product_main p.star-rating::attr(class)", Transforms.Str(StarsToNumber));
            loader.AddCss(b => b.Stock, "div.product_main p.availability::text", Transforms.ToInt);
            loader.Field(b => b.Image).Input(new MapCompose(Transforms.UrlJoin));
            loader.AddCss(b => b.Image, "div.item img::attr(src)");
            yield return loader.LoadItem();
        }

        private static object? StarsToNumber(string css) =>
            Array.IndexOf(["Zero", "One", "Two", "Three", "Four", "Five"], css.Split(' ').Last());
    }
    // </demo>

    public override async Task RunAsync(DemoContext ctx)
    {
        var result = await ctx.CrawlAsync(new BookPagesSpider(ctx.Site.Url("/books/")), ctx.NewSettings());
        ctx.Print(result.ToString());
    }
}
