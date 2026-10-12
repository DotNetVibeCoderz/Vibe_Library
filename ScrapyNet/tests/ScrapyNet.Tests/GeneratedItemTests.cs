using System.Text.Json.Serialization;
using ScrapyNet.Exporters;
using ScrapyNet.Loaders;
using Xunit;

namespace ScrapyNet.Tests;

[ScrapyItem]
public sealed record GeneratedBook
{
    public string? Title { get; init; }
    public decimal Price { get; init; }
    public List<string> Tags { get; init; } = [];
    public string? Url { get; set; }
    public string Shelf => "fiction";
    [JsonIgnore] public string? Secret { get; set; }
    public int Stock;
}

public class GeneratedItemBase
{
    public string? Id { get; set; }
}

[ScrapyItem]
public sealed class GeneratedDerived : GeneratedItemBase
{
    public string? Name { get; set; }
}

public class GeneratedItemTests
{
    [Fact]
    public void Generator_registers_marked_types_only()
    {
        Assert.True(ItemAccessors.IsRegistered(typeof(GeneratedBook)));
        Assert.True(ItemAccessors.IsRegistered(typeof(GeneratedDerived)));
        Assert.False(ItemAccessors.IsRegistered(typeof(Book)));
        Assert.True(ItemAdapter.TypeAccessor.Get(typeof(GeneratedBook)).Generated);
    }

    [Fact]
    public void Generated_accessors_read_write_init_properties_and_fields()
    {
        var book = new GeneratedBook { Title = "Dune" };
        var adapter = ItemAdapter.For(book);

        Assert.Equal(["Title", "Price", "Tags", "Url", "Shelf", "Stock"], adapter.FieldNames);
        adapter.Set("price", "12.50");           // init-only, loose name, converted
        adapter.Set("tags", new[] { "a", "b" });
        adapter.Set("Stock", "3");               // public field
        adapter.Set("URL", "https://x/1");

        Assert.Equal(12.50m, book.Price);
        Assert.Equal(["a", "b"], book.Tags);
        Assert.Equal(3, book.Stock);
        Assert.Equal("https://x/1", book.Url);
        Assert.Equal("fiction", adapter.Get("Shelf"));
        Assert.Throws<InvalidOperationException>(() => adapter.Set("Shelf", "x"));
        Assert.False(adapter.HasField("Secret"));
        Assert.Equal(typeof(decimal), adapter.GetFieldType("price"));
    }

    [Fact]
    public void Generated_accessors_include_inherited_members()
    {
        var item = new GeneratedDerived();
        var adapter = ItemAdapter.For(item);
        adapter.Set("id", "42");
        adapter.Set("name", "n");
        Assert.Equal(["Name", "Id"], adapter.FieldNames);
        Assert.Equal("42", item.Id);
    }

    [Fact]
    public void Loader_and_exporter_work_through_generated_accessors()
    {
        var html = "<div><h1> Dune </h1><p class='price'>£12.50</p></div>";
        var response = new HtmlResponse("https://example.com/b", body: System.Text.Encoding.UTF8.GetBytes(html));
        var loader = new ItemLoader<GeneratedBook>(response);
        loader.AddCss(b => b.Title, "h1::text", Transforms.Clean);
        loader.AddCss(b => b.Price, "p.price::text", Transforms.Price);
        var book = loader.LoadItem();
        Assert.Equal("Dune", book.Title);
        Assert.Equal(12.50m, book.Price);

        using var stream = new MemoryStream();
        var exporter = new JsonLinesItemExporter(stream);
        exporter.StartExporting();
        exporter.ExportItem(book);
        exporter.FinishExporting();
        var line = System.Text.Encoding.UTF8.GetString(stream.ToArray());
        Assert.Contains("\"Title\":\"Dune\"", line);
        Assert.Contains("\"Price\":12.50", line);
        Assert.DoesNotContain("Secret", line);
    }
}
