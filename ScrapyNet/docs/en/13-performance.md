# 13. Performance

## Design choices

| Where | Choice | Why |
|---|---|---|
| Downloads | One pooled `SocketsHttpHandler` per proxy, keep-alive, automatic gzip/deflate/brotli | Connection reuse dominates crawl speed |
| Bodies | Exact-size arrays for known lengths; pooled growing buffers (`ArrayPool`) otherwise | One copy per body, no `MemoryStream` churn |
| Parsing | Documents parsed lazily and once per response; CSS pseudo-element parsing cached | A callback running ten queries parses once |
| Fingerprints | SHA-1 over method, canonical URL and body, stored as a 20-byte struct | A million seen URLs take ≈ 40 MB instead of 150+ MB as hex strings |
| Scheduler | `PriorityQueue` with sequence tie-breaks | O(log n) enqueue/dequeue, stable LIFO/FIFO |
| Start requests | Pulled lazily while the queue is short | Millions of start URLs never sit in memory |
| Slots | Delay enforced by reserving start times | No background loop, no lock held while waiting |
| Engine | Wake-up signal instead of polling | Idle CPU is idle |
| Stats | `Interlocked` on pre-allocated cells | Per-request counters don't lock or allocate |
| Signals | Immutable handler arrays swapped on connect | Sending is a lock-free read; no handlers ≈ free |
| Items | Compiled property accessors cached per type | Exporting doesn't use reflection per item |
| Exports | `Utf8JsonWriter` straight to the stream | Also keeps AOT/trimmed apps working |
| Vector search | `TensorPrimitives.CosineSimilarity` (SIMD) with a bounded heap | One pass, O(k) memory |

## Measured

Laptop, .NET 10, Release, BenchmarkDotNet short job (`benchmarks/ScrapyNet.Benchmarks`):

| Benchmark | Mean | Allocated |
|---|---:|---:|
| Parse a 20-product listing + 3 CSS queries | 0.72 ms | 325 KB |
| Parse + 3 XPath queries | 0.98 ms | 719 KB |
| Link extraction on the same page | 0.79 ms | 385 KB |
| Fingerprint 1,000 requests | 1.60 ms | 1.09 MB |
| Export 1,000 items as JSON Lines | 0.55 ms | 800 KB |
| Export 1,000 items as CSV | 0.98 ms | 1.45 MB |
| SimHash of 12 articles | 0.61 ms | 72 KB |
| Chunk 12 articles | 0.29 ms | 202 KB |
| Embed one paragraph (hashing) | 9.7 µs | 17 KB |
| Top-5 search over 10,000 vectors (512-d) | 2.2 ms | 0.5 KB |

End-to-end crawl throughput against the in-process sandbox (the network is not the bottleneck here,
so this measures the framework itself):

```
dotnet run -c Release --project benchmarks/ScrapyNet.Benchmarks -- crawl
CONCURRENT_REQUESTS=8     3605 pages in 1.34s = 161,574 pages/min
CONCURRENT_REQUESTS=16    3605 pages in 0.68s = 316,689 pages/min
CONCURRENT_REQUESTS=32    3605 pages in 0.70s = 309,543 pages/min
```

Against real websites, politeness settings (`DOWNLOAD_DELAY`, AutoThrottle, per-domain concurrency)
and server latency decide throughput long before the framework does.

## Tuning

- Raise `CONCURRENT_REQUESTS` for many domains; keep `CONCURRENT_REQUESTS_PER_DOMAIN` polite.
- Use `SCHEDULER_ORDER=lifo` (default) for large crawls: the queue stays short.
- Prefer `jsonlines` over `json` for big exports: it streams and can be appended.
- Block images and fonts in Playwright (`blockResourceTypes`), and render only pages that need it.
- `HTTPCACHE_ENABLED` while developing turns re-runs into disk reads.
- `MEMUSAGE_LIMIT_MB` protects long-running crawls from runaway memory.
