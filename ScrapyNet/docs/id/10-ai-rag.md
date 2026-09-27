# 10. AI dan RAG

![Dari crawl ke basis pengetahuan](../images/gallery-rag.png)

`Gravicode.ScrapyNet.AI` mengubah halaman hasil crawl menjadi pengetahuan yang siap untuk AI:

```
Situs → Scrapy.Net → bersihkan / normalisasi → ekstrak artikel → deduplikasi → chunk → embed → vector store → RAG
```

```bash
dotnet add package Gravicode.ScrapyNet.AI
```

## Pipeline dalam satu langkah

```csharp
using ScrapyNet.AI;

var kb = new KnowledgeBase(new HashingEmbeddingGenerator(512));
settings.ItemPipelines.Add(new RagIngestionPipeline(kb)
{
    Summarizer = new ExtractiveSummarizer(),
    Categorizer = new KeywordCategorizer(new Dictionary<string, string[]>
    {
        ["harga"] = ["price", "discount", "plan", "harga"],
        ["dukungan"] = ["error", "install", "troubleshoot"],
    }),
}, 800);

// Item dari spider menyediakan "html" (diekstrak otomatis) atau "text", serta "url" dan "title".
await Scrapy.CrawlAsync(new DocsSpider(), settings);

var hits = await kb.SearchAsync("bagaimana cara reset kata sandi?", top: 5);
```

Statistik: `rag/documents_seen`, `rag/documents_ingested`, `rag/chunks`, `rag/duplicates`.

## Blok penyusun

| Kelas | Fungsi |
|---|---|
| `MetadataExtractor` | Judul, deskripsi, penulis, tanggal terbit, bahasa, URL kanonik, OpenGraph, Twitter card, JSON-LD |
| `ArticleExtractor` | Konten utama tanpa navigasi, sidebar, iklan, dan footer (penilaian gaya Readability) |
| `TextNormalizer` | NFKC, penyeragaman tanda kutip dan tanda hubung, perapian spasi, pemecahan kalimat, tokenisasi, stemming |
| `TextChunker` | Chunking per paragraf, kalimat, jendela tetap, atau heading Markdown, dengan overlap |
| `ContentHasher` | SHA-256 dari teks ternormalisasi untuk duplikat persis dan deteksi perubahan |
| `SimHash`, `NearDuplicateIndex` | Sidik jari 64-bit; pencarian hampir-duplikat dengan banding prinsip pigeonhole |
| `HashingEmbeddingGenerator` | Embedding lokal yang deterministik (feature hashing), tanpa model |
| `OpenAiEmbeddingGenerator` | `/v1/embeddings` apa pun yang kompatibel OpenAI: OpenAI, Azure OpenAI, Ollama, LM Studio, vLLM |
| `InMemoryVectorStore` | Pencarian cosine SIMD dengan heap terbatas; simpan/muat ke disk |
| `QdrantVectorStore` | Qdrant lewat REST |
| `OpenAiChatModel` | Endpoint chat apa pun yang kompatibel OpenAI; `ForAzure(...)` untuk Azure OpenAI |
| `ExtractiveSummarizer` / `LlmSummarizer` | Ringkasan TextRank / ringkasan tulisan model |
| `KeywordCategorizer` / `NaiveBayesCategorizer` / `LlmCategorizer` | Aturan, classifier terlatih, zero-shot |
| `ContentChangeStore`, `IncrementalRecrawlMiddleware` | Request kondisional ETag/Last-Modified dan deteksi perubahan berbasis hash konten |
| `KnowledgeBase` | Ingest, pencarian, dan `AskAsync` dengan sumber yang disitasi |

## Jawaban dari model bahasa

```csharp
var model = OpenAiChatModel.ForAzure(endpoint, Environment.GetEnvironmentVariable("AZURE_OPENAI_KEY")!, "gpt-5-mini");
// atau: new OpenAiChatModel("deepseek-v4-flash", key, "https://api.deepseek.com/v1/")
// atau: new OpenAiChatModel("llama3.1", baseUrl: "http://localhost:11434/v1/")      // Ollama

var kb = new KnowledgeBase(new HashingEmbeddingGenerator()) { LanguageModel = model };
var answer = await kb.AskAsync("Apa yang dilakukan rotasi proxy saat sebuah proxy diblokir?");
Console.WriteLine(answer.Answer);
foreach (var source in answer.Sources) Console.WriteLine(source.Record.Metadata["url"]);
```

Simpan kunci di variabel lingkungan atau `SecretStore` milik platform, jangan di kode. Model reasoning
(GPT-5, seri o) menolak `temperature` dan `max_tokens`; `ForAzure` mengatur `max_completion_tokens` dan
menghilangkan temperature untuk Anda.

## Embedding semantik

`HashingEmbeddingGenerator` mencocokkan kata dan bentuk kata, cukup baik untuk dokumentasi dan katalog,
dan tanpa biaya. Untuk pencarian berdasarkan makna, gunakan model sungguhan:

```csharp
var embeddings = new OpenAiEmbeddingGenerator("text-embedding-3-small", 1536, apiKey);
var kb = new KnowledgeBase(embeddings, new QdrantVectorStore("http://localhost:6333", "docs", 1536));
```

## Re-crawl inkremental

```csharp
settings.Set("CHANGE_STORE_PATH", "state/changes.json");
settings.DownloaderMiddlewares.Add<IncrementalRecrawlMiddleware>(950);
```

Run berikutnya mengirim `If-None-Match`/`If-Modified-Since`; respons `304 Not Modified` dan halaman yang
hash kontennya tidak berubah dilewati (`change_detection/unchanged`), sehingga hanya halaman baru dan
yang berubah yang diekstrak dan di-embed ulang.
