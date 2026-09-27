# 4. Selektor, item, dan item loader

![Taman bermain selector](../images/gallery-selectors.png)

## Selektor

Setiap respons teks menyediakan `Css`, `Xpath`, dan `Selector`. Dokumen diparse sekali secara malas
dengan parser HTML5 AngleSharp (toleran terhadap markup rusak) atau parser XML-nya untuk respons XML.

```csharp
response.Css("title::text").Get();                          // anak teks
response.Css("div.content ::text").GetAll();                // semua teks turunan (perhatikan spasinya)
response.Css("a.next::attr(href)").Get();                   // nilai atribut
response.Css("img").Attrib["src"];                          // atribut elemen pertama
response.Xpath("//h1/text()").Get();
response.Xpath("count(//div[@class='quote'])").Get();       // skalar dikembalikan sebagai string: "10"
response.Xpath("//li[has-class('active')]").GetAll();       // has-class() milik parsel
response.Xpath("//a[re:test(@href, '\\.pdf$')]/@href").GetAll();   // regex EXSLT
response.Xpath("//li[@data-id=$id]", new Dictionary<string, object> { ["id"] = "42" });
```

Seleksi bisa berantai, dan CSS pada sebuah elemen juga menguji elemen itu sendiri (seperti parsel):

```csharp
foreach (var quote in response.Css("div.quote"))
{
    var text = quote.Css("span.text::text").Get();
    var author = quote.Xpath(".//small[@class='author']/text()").Get();
}
```

| Method | Hasil |
|---|---|
| `Get(default)` | Nilai hasil pertama (HTML luar, teks, nilai atribut) atau default |
| `GetAll()` | Semua nilai |
| `GetText()` | Seluruh teks di dalamnya, spasi dirapikan (kemudahan yang tidak ada di parsel) |
| `Re(pattern)` / `ReFirst(pattern)` | Hasil regex (grup bernama `extract`, atau semua grup, atau seluruh kecocokan) |
| `Attrib` | Atribut elemen pertama |
| `Drop()` | Menghapus node yang cocok dari dokumen |

Namespace XML diabaikan secara default, sehingga `//url/loc` cocok dengan sitemap tanpa mendaftarkan
namespace. Panggil `selector.RegisterNamespace("g", "http://base.google.com/ns/1.0")` untuk XPath yang
sadar namespace.

Selektor mandiri: `Selector.FromHtml(html)`, `Selector.FromXml(xml)`.

Respons JSON: `((TextResponse)response).Json()` mengembalikan `JsonNode`; `Json<T>()` melakukan deserialisasi.

## Item

Objek apa pun bisa menjadi item. Pilih yang sesuai:

```csharp
// record atau class — bertipe dan idiomatis
public sealed record Product(string Name, decimal Price, string Url);

// objek anonim — cepat
yield return new { Name = name, Price = price };

// dictionary — field dinamis
yield return new Dictionary<string, object?> { ["name"] = name };

// ScrapyNet.Item — field dideklarasikan (salah ketik langsung error), dengan metadata per field
public sealed class ProductItem() : Item("name", "price", new Field("url") { Serializer = v => v?.ToString()?.ToLowerInvariant() });
```

`ItemAdapter.For(item)` memberi akses seragam ke semuanya — nama field, `Get`, `Set`, `AsDictionary()` —
memakai accessor properti hasil kompilasi yang di-cache per tipe. Pengisian field mencocokkan nama
secara longgar (`file_urls`, `FileUrls`, dan `fileUrls` dianggap sama) dan mengonversi tipe
(`"12.50"` → `decimal`).

## Item loader

Loader mengumpulkan nilai per field melalui **input processor** dan menghasilkan nilai akhir dengan
**output processor**:

```csharp
public sealed class Book
{
    public string? Title { get; set; }
    public decimal Price { get; set; }
    public List<string> Tags { get; set; } = [];
    public string? Url { get; set; }
}

var loader = new ItemLoader<Book>(response);
loader.AddCss(b => b.Title, "h1::text", Transforms.Clean);
loader.AddCss(b => b.Price, "p.price::text", Transforms.Price);       // "£51.77", "Rp 1.250.000", "12,50 €"
loader.AddXpath(b => b.Tags, "//a[@class='tag']/text()");
loader.Field(b => b.Url).Input(new MapCompose(Transforms.UrlJoin));
loader.AddCss(b => b.Url, "a.permalink::attr(href)");
Book book = loader.LoadItem();
```

Processor (`ScrapyNet.Loaders`):

| Processor | Fungsi |
|---|---|
| `Identity` | Mengembalikan nilai apa adanya (default input dan output) |
| `TakeFirst` | Nilai pertama yang tidak null dan tidak kosong |
| `Join(separator)` | Menggabungkan nilai menjadi string |
| `MapCompose(fns...)` | Menerapkan setiap fungsi ke setiap nilai, meratakan list dan membuang null |
| `Compose(fns...)` | Merangkai fungsi atas seluruh list |
| `AbsoluteUrl` | Menyelesaikan URL relatif terhadap respons |

Transform (`Transforms.*`): `Strip`, `Clean`, `RemoveTags`, `DecodeEntities`, `Lower`, `Upper`, `ToInt`,
`ToDecimal`, `ToDouble`, `Price`, `ToDateTime`, `Regex(pattern)`, `Replace(a, b)`, `Where(pattern)`,
`UrlJoin`, dan `Str(func)` untuk mengadaptasi fungsi string apa pun.

Menetapkan list ke properti skalar mengambil elemen pertamanya, sehingga item bertipe jarang
membutuhkan `TakeFirst`. Untuk dictionary, set `DefaultOutputProcessor = new TakeFirst()` agar mendapat
nilai skalar.

Loader bersarang dibatasi pada satu wilayah tetapi mengisi item yang sama:

```csharp
var details = loader.NestedCss("table.specs");
details.AddXpath("weight", ".//th[.='Weight']/following-sibling::td/text()");
```
