# 5. Item pipelines and feed exports

## Item pipelines

A pipeline receives every item, returns it (possibly changed) or throws `DropItemException`:

```csharp
public sealed class PricePipeline : ItemPipeline
{
    public override ValueTask<object> ProcessItemAsync(object item, Spider spider)
    {
        var product = (Product)item;
        if (product.Price <= 0) throw new DropItemException($"no price for {product.Name}");
        return ValueTask.FromResult<object>(product with { Price = Math.Round(product.Price * 1.11m, 2) });
    }
}

settings.ItemPipelines.Add<PricePipeline>(300);          // lower numbers run first
settings.ItemPipelines.Add(new LambdaPipeline(i => i), 400);
```

`OpenSpiderAsync` and `CloseSpiderAsync` open and close resources (connections, files). Dropped items
are logged at warning level with the reason and counted in `item_dropped_count` and
`item_dropped_reasons_count/...`.

### Built-in pipelines

| Pipeline | Purpose |
|---|---|
| `CleaningPipeline` | Collapses whitespace, decodes entities, optionally strips tags in every string field; per-field transforms with `.Field(name, fn)` |
| `ValidationPipeline` | `.Require(fields)`, `.Match(field, regex)`, `.Range(field, min, max)`, `.Rule(name, predicate)` |
| `DuplicatesPipeline(keyFields)` | Drops items already seen (by key fields or all fields), stored as 128-bit hashes |
| `AdoNetPipeline` | Batched inserts into any ADO.NET database: SQL Server, PostgreSQL, MySQL, SQLite, Oracle |
| `HttpApiPipeline` | POSTs items (or batches) as JSON to a REST endpoint or webhook |
| `ElasticsearchPipeline` | Indexes through the `_bulk` API (Elasticsearch or OpenSearch), no client library |
| `FilesPipeline` | Downloads `file_urls`/`FileUrls`, stores `full/<sha1>.<ext>`, fills `files`/`Files` |
| `ImagesPipeline` | Same for `image_urls`/`ImageUrls`, validates and measures images, optional thumbnails |
| `RagIngestionPipeline` (AI package) | Extracts, chunks, embeds and indexes content — see [AI and RAG](10-ai-rag.md) |

A typical chain:

```csharp
settings.ItemPipelines
    .Add(new CleaningPipeline().Field("Title", Transforms.Clean), 100)
    .Add(new ValidationPipeline().Require("Title", "Price").Range("Price", min: 0), 200)
    .Add(new DuplicatesPipeline("Url"), 300)
    .Add(new AdoNetPipeline(() => new NpgsqlConnection(connectionString), "products")
    {
        CreateTableSql = "CREATE TABLE IF NOT EXISTS products (title text, price numeric, url text primary key)",
        BatchSize = 500,
    }, 800);
```

MongoDB, S3 and other stores are a few lines of `ItemPipeline` with their official client — the
contract is the same.

### Files and images

![Files and images](../images/gallery-media.png)

```csharp
public sealed class Report
{
    public List<string> FileUrls { get; set; } = [];
    public List<MediaFile> Files { get; set; } = [];     // filled by the pipeline: Url, Path, Checksum, Status
}

settings.Set(SettingKeys.FilesStore, "downloads");
settings.ItemPipelines.Add<FilesPipeline>(1);
```

Media downloads go through the engine, so they get the same headers, cookies, proxies and throttling
as pages. Files younger than `FILES_EXPIRES` days are reused (`uptodate`). `ImagesPipeline` reads
image dimensions from PNG, JPEG, GIF, WebP and BMP headers without decoding pixels, drops images
smaller than `IMAGES_MIN_WIDTH`/`IMAGES_MIN_HEIGHT`, and creates thumbnails (`IMAGES_THUMBS`) through
an `IImageProcessor` you plug in with `IMAGES_PROCESSOR`.

## Feed exports

```csharp
settings.Feeds
    .Add("output/%(name)s.json", new FeedOptions { Overwrite = true, Indent = 2 })
    .Add("output/%(name)s.jsonl")
    .Add("output/%(name)s.csv", new FeedOptions { Fields = ["Title", "Price"] })
    .Add("output/%(name)s.xml.gz")                                        // gzip by extension
    .Add("output/batches/part-%(batch_id)03d.jsonl", new FeedOptions { BatchItemCount = 1000 })
    .Add("https://bucket.s3.amazonaws.com/feed.json?X-Amz-Signature=...", "json");   // PUT on close
```

| Option | Meaning |
|---|---|
| `Format` | `json`, `jsonlines` (`jsonl`, `jl`), `csv`, `tsv`, `xml` — guessed from the extension if null |
| `Overwrite` | Replace instead of append (`-O` vs `-o`) |
| `Fields` | Columns/fields and their order |
| `Indent` | Pretty-print JSON/XML |
| `Encoding` | Output encoding (default UTF-8) |
| `StoreEmpty` | Write the file even with no items |
| `BatchItemCount` | New file every N items (needs `%(batch_id)d` or `%(batch_time)s`) |
| `ItemTypes` / `ItemFilter` | Export only some items |
| `Gzip` | Compress (also implied by a `.gz` URI) |
| `CsvDelimiter`, `IncludeHeadersLine`, `XmlRootElement`, `XmlItemElement` | Format details |

URI placeholders: `%(name)s` (spider name), `%(time)s` (start time), `%(batch_id)d`, `%(batch_id)05d`,
`%(batch_time)s`, and any spider argument as `%(argument)s`. Targets: file paths, `file://`,
`stdout:`, `memory:name` (for tests), and `http(s)://` (uploaded with PUT when the feed closes —
works with presigned S3, GCS, Azure Blob and MinIO URLs).

Exporters write JSON with `Utf8JsonWriter` directly, so exports keep working in trimmed/AOT apps and
.NET 10 file-based apps. Field serializers declared on `Item` fields are applied on export.

Use an exporter directly:

```csharp
using var stream = File.Create("items.csv");
var exporter = ItemExporter.Create("csv", stream, new FeedOptions());
exporter.StartExporting();
foreach (var item in items) exporter.ExportItem(item);
exporter.FinishExporting();
```
