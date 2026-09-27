# 13. Performa

## Pilihan desain

| Bagian | Pilihan | Alasan |
|---|---|---|
| Unduhan | Satu `SocketsHttpHandler` ber-pool per proxy, keep-alive, dekompresi gzip/deflate/brotli otomatis | Penggunaan ulang koneksi paling menentukan kecepatan crawl |
| Body | Array berukuran pas bila panjang diketahui; buffer ber-pool (`ArrayPool`) bila tidak | Satu kali salin per body, tanpa churn `MemoryStream` |
| Parsing | Dokumen diparse secara malas dan sekali per respons; parsing pseudo-element CSS di-cache | Callback dengan sepuluh query tetap parse sekali |
| Sidik jari | SHA-1 atas method, URL kanonik, dan body, disimpan sebagai struct 20 byte | Sejuta URL yang terlihat ≈ 40 MB, bukan 150+ MB sebagai string heksa |
| Scheduler | `PriorityQueue` dengan pemecah seri berbasis urutan | Enqueue/dequeue O(log n), LIFO/FIFO yang stabil |
| Start request | Diambil secara malas selama antrean pendek | Jutaan URL awal tidak pernah tinggal di memori |
| Slot | Jeda ditegakkan dengan memesan waktu mulai | Tanpa loop latar, tanpa lock saat menunggu |
| Engine | Sinyal bangun, bukan polling | CPU benar-benar diam saat idle |
| Statistik | `Interlocked` pada sel yang sudah dialokasikan | Counter per request tanpa lock dan tanpa alokasi |
| Sinyal | Array handler immutable yang ditukar saat connect | Pengiriman adalah baca tanpa lock; tanpa handler ≈ gratis |
| Item | Accessor properti terkompilasi yang di-cache per tipe | Ekspor tidak memakai refleksi per item |
| Ekspor | `Utf8JsonWriter` langsung ke stream | Juga membuat aplikasi AOT/trimmed tetap berjalan |
| Pencarian vektor | `TensorPrimitives.CosineSimilarity` (SIMD) dengan heap terbatas | Satu kali lintasan, memori O(k) |

## Hasil pengukuran

Laptop, .NET 10, Release, BenchmarkDotNet short job (`benchmarks/ScrapyNet.Benchmarks`):

| Benchmark | Rata-rata | Alokasi |
|---|---:|---:|
| Parse listing 20 produk + 3 query CSS | 0,72 ms | 325 KB |
| Parse + 3 query XPath | 0,98 ms | 719 KB |
| Ekstraksi tautan pada halaman yang sama | 0,79 ms | 385 KB |
| Sidik jari 1.000 request | 1,60 ms | 1,09 MB |
| Ekspor 1.000 item sebagai JSON Lines | 0,55 ms | 800 KB |
| Ekspor 1.000 item sebagai CSV | 0,98 ms | 1,45 MB |
| SimHash 12 artikel | 0,61 ms | 72 KB |
| Chunk 12 artikel | 0,29 ms | 202 KB |
| Embed satu paragraf (hashing) | 9,7 µs | 17 KB |
| Pencarian top-5 atas 10.000 vektor (512 dimensi) | 2,2 ms | 0,5 KB |

Throughput crawl end-to-end terhadap sandbox dalam proses (jaringan bukan penghambat di sini, jadi ini
mengukur framework-nya sendiri):

```
dotnet run -c Release --project benchmarks/ScrapyNet.Benchmarks -- crawl
CONCURRENT_REQUESTS=8     3605 pages in 1.34s = 161,574 pages/min
CONCURRENT_REQUESTS=16    3605 pages in 0.68s = 316,689 pages/min
CONCURRENT_REQUESTS=32    3605 pages in 0.70s = 309,543 pages/min
```

Pada situs sungguhan, setting kesopanan (`DOWNLOAD_DELAY`, AutoThrottle, konkurensi per domain) dan
latensi server menentukan throughput jauh sebelum framework-nya.

## Penyetelan

- Naikkan `CONCURRENT_REQUESTS` untuk banyak domain; jaga `CONCURRENT_REQUESTS_PER_DOMAIN` tetap sopan.
- Gunakan `SCHEDULER_ORDER=lifo` (default) untuk crawl besar: antrean tetap pendek.
- Pilih `jsonlines` daripada `json` untuk ekspor besar: bisa di-stream dan ditambahkan.
- Blokir gambar dan font di Playwright (`blockResourceTypes`), dan render hanya halaman yang perlu.
- `HTTPCACHE_ENABLED` selama pengembangan mengubah run ulang menjadi baca disk.
- `MEMUSAGE_LIMIT_MB` melindungi crawl jangka panjang dari memori yang membengkak.
