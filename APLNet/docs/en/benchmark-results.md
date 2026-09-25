# Benchmark results

[Bahasa Indonesia](../id/benchmark-results.md) · [Index](README.md)

These are BenchmarkDotNet results for the matrix in spec §5, B1–B7. The raw reports, with every
column, are in [docs/benchmarks/](../benchmarks/). This page shows the numbers **where APL.Net loses
as well as where it wins**, and explains why in each case.

## Environment (spec §5.1)

| | |
|---|---|
| CPU | Intel Core i7-8650U, 1.90 GHz (turbo 4.2 GHz), Kaby Lake R: **4 cores / 8 threads**, AVX2, no AVX-512 |
| Machine | Laptop on AC power, Windows "High performance" power plan |
| OS | Windows 11 25H2 (10.0.26200) |
| Runtime | .NET 10.0.12, RyuJIT x86-64-v3, SDK 10.0.401 |
| Harness | BenchmarkDotNet 0.15.8, `Job.Default` on .NET 10, 5 warm-ups, 10–30 iterations, `MemoryDiagnoser` |
| Environment variables | none set (`DOTNET_TieredPGO` and `DOTNET_ReadyToRun` at their defaults) |
| Date | 2026-09-25, total run time 24 min for 66 benchmarks |

Reproduce with:

```sh
dotnet run -c Release --project benchmarks/APL.Net.Benchmarks -- --filter '*'
```

Close other applications first: static partitioning is sensitive to any busy core.

## Summary against the spec's success criteria (§5.3)

| Criterion | Result | Met? |
|---|---|---|
| B1/B2: struct invoker ≥ 2× `Parallel.For` at 1M+ | B1 1M: 0.98×, B1 100M: 0.99× (memory-bound). B2 1M: **1.26×** | **No**, see below |
| B1 in cache (10K), for reference | **3.9×** (struct), **4.4×** (range struct) | yes |
| B4/B5: SIMD ≥ 3× sequential scalar | B4 1M **7.6×**, B5 1M **6.0×**. At 10M: 1.2× / 2.6× (memory-bound) | at 1M |
| B4/B5: SIMD ≥ 1.5× `Parallel.For` | B4 **19×** (1M), **2.2×** (10M). B5 **5.7×** (1M), **2.0×** (10M) | **yes** |
| B6: document where `Parallel.For` wins | Static loses **2.4×** (sorted). Work stealing wins **1.44×** (sorted) and loses **1.5×** (shuffled) | documented |
| B7: zero allocation per struct-invoker call | **0 B** for every struct-body benchmark | **yes** |

### Why NFR1 (≥ 2× at 1M+) is not met for trivial work

`x * 2 + 1` over 1M doubles reads 8 MB and writes 8 MB, which is roughly the size of this CPU's
8 MB L3 cache. At 100M elements, it is 1.6 GB. Arithmetic is not the limit here: every variant,
**including the single-threaded loop**, finishes in about the time DRAM needs to move the data.
Sequential takes 1.20 ms and every parallel variant takes 1.18–1.41 ms. No scheduler can beat
memory bandwidth. When the same loop fits in cache (B1 at 10K), the dispatch overhead APL.Net
removes dominates, and the struct body is **3.9×** faster than `Parallel.For`.

B2 (`√x · sin x`, ~10 ns per element) is compute-bound, and APL.Net is **1.26×** faster. Delegate
dispatch is only part of the per-element cost, so this is the dispatch share and nothing more.
Claiming 2× here would require cheaper arithmetic, not a better scheduler.

## B1: trivial arithmetic, `dst[i] = src[i] * 2 + 1`

| Method | 10K | 1M | 100M | Allocated |
|---|---:|---:|---:|---:|
| Sequential | 5.33 µs | 1,197 µs | 181.9 ms | – |
| `Parallel.For` | 25.44 µs | 1,383 µs | 166.7 ms | ~3.0 KB |
| `Parallel.ForEach(Partitioner.Create)` (TPL, expert) | 14.81 µs | 1,178 µs | 162.5 ms | ~4.5 KB |
| `Apl.For` (lambda) | 15.36 µs | 1,176 µs | 165.9 ms | 96 B (the lambda) |
| `Apl.For` (struct) | **6.53 µs** | 1,406 µs | 167.7 ms | **0 B** |
| `Apl.ForRange` (struct) | **5.81 µs** | 1,254 µs | 169.2 ms | **0 B** |

In cache, the struct-body loop costs about the same as a sequential loop plus one wake-up, while
`Parallel.For` costs **4.8×** as much as sequential. Out of cache, all variants are within noise
of one another.

## B2: medium compute, `√x · sin x`, 1M

| Method | Mean | vs `Parallel.For` | Allocated |
|---|---:|---:|---:|
| Sequential | 10,627 µs | 0.38× | – |
| `Parallel.For` | 3,986 µs | 1.00× | 3,111 B |
| `Apl.For` (lambda) | 3,760 µs | 1.06× | 96 B |
| `Apl.For` (struct) | **3,166 µs** | **1.26×** | **0 B** |

## B3: heavy compute, 4×4 matrix power (~880 ns per element), 100K

| Method | Mean | Allocated |
|---|---:|---:|
| Sequential | 88.2 ms | – |
| `Parallel.For` | 31.1 ms | 3,167 B |
| `Apl.For` (lambda) | 32.8 ms | 96 B |
| `Apl.For` (struct) | 31.6 ms | **0 B** |

As expected, all parallel variants are equal within noise. At ~1 µs per element, dispatch cost is
irrelevant, and about 2.8× on 4 physical cores is what the hardware gives.

## B4: SIMD AXPY, `x = x · 0.5 + 1` over floats

| Method | 1M | vs scalar | vs `Parallel.For` | 10M |
|---|---:|---:|---:|---:|
| Scalar sequential | 458 µs | 1.00× | 2.5× | 6,252 µs |
| `Parallel.For` | 1,138 µs | 0.40× | 1.00× | 11,572 µs |
| `Apl.For` (struct, scalar body) | 332 µs | 1.41× | 3.4× | 5,649 µs |
| `SimdOps.TransformInPlace` (1 thread) | 143 µs | 3.3× | 7.9× | 4,801 µs |
| **`SimdOps.ParallelTransformInPlace`** | **60 µs** | **7.6×** | **19×** | **5,310 µs** |
| `ParallelTransformInPlace` (delegates) | 90 µs | 5.1× | 12.7× | 5,336 µs |

`Parallel.For` is *slower than a single scalar thread* here. The body is too cheap to pay for a
delegate call and a loop-state check per element. At 10M floats (40 MB), everything is bound by
DRAM again, and the parallel SIMD kernel lands where the single-threaded one does.

## B5: sum of doubles

| Method | 1M | 10M | Allocated |
|---|---:|---:|---:|
| LINQ `Sum()` | 1,112 µs | 13,116 µs | – |
| Sequential loop | 1,099 µs | 13,481 µs | – |
| `Parallel.For` + thread-local subtotals | 1,103 µs | 10,151 µs | ~3.5 KB |
| `SimdOps.Sum` (1 thread) | 304 µs | 5,041 µs | – |
| **`SimdOps.ParallelSum`** | **194 µs** | **5,062 µs** | **0 B** |

## B6: uneven workload, Zipf-distributed cost over 10K items

| Method | Sorted (heavy first) | Shuffled |
|---|---:|---:|
| Sequential | 38.6 ms | 44.5 ms |
| `Parallel.For` | 12.9 ms | **10.1 ms** |
| `Apl.For`, static (default) | 31.3 ms | 13.2 ms |
| `Apl.For`, striped (16) | 17.7 ms | 17.1 ms |
| `Apl.For`, work stealing (grain 1) | **8.9 ms** | 15.0 ms |

This benchmark exists to show the weakness honestly:

- **Sorted:** the first static partition owns about 78% of the work, so static partitioning is
  2.4× slower than `Parallel.For`. Work stealing recovers the loss and beats TPL by 1.44×.
- **Shuffled:** `Parallel.For` wins against every APL.Net partitioner. Its dynamic chunks, and the
  extra threads the pool injects during a long loop, spread randomly placed expensive items better
  than work stealing with 8 workers and a grain of 1. This run also had the highest variance
  (StdDev 1.7–6.9 ms), and the sequential baseline itself varied by ±15%. Treat the shuffled
  column as "TPL wins", not as a precise ratio.

For skewed workloads: measure with your own data, and if neither APL.Net partitioner wins,
**use `Parallel.For`**.

## B7: the fixed cost of one call

| Method | 1,000 elements | 16,000 elements | Allocated |
|---|---:|---:|---:|
| `Parallel.For` | 10.35 µs | 37.14 µs | 2.9–3.0 KB |
| `Apl.For` (lambda) | 3.66 µs (**2.8×**) | 21.93 µs (1.7×) | 96 B (the lambda) |
| `Apl.For` (struct) | **2.79 µs (3.7×)** | **9.56 µs (3.9×)** | **0 B, no Gen0** |
| Sequential | 0.66 µs | 7.81 µs | – |

The struct path allocates **nothing**. The job, its wait handle and its partition state are
pooled per body type. The 96 B on the lambda path is the benchmark's own closure and delegate.

## Rust/Rayon reference (out-of-band, illustrative only)

A separate Rust program (`benchmarks/rust-rayon`, rustc 1.97.1 (8bab26f4f 2026-07-14), Rayon 1.x, Criterion 0.5, `lto = "fat"`)
ran on the same machine the same day. **Different runtime, different harness, illustrative only.**
The numbers are not apples to apples, because process startup and measurement methodology differ.
There is no FFI: nothing links the two.

| Workload | Rust sequential | Rayon `par_iter` | APL.Net best | `Parallel.For` |
|---|---:|---:|---:|---:|
| B1 `x*2+1`, 10K | 2.45 µs | 69.5 µs | 5.81 µs | 25.44 µs |
| B1 `x*2+1`, 1M | 2.00 ms | 1.74 ms | 1.18 ms | 1.38 ms |
| B1 `x*2+1`, 100M | 166.8 ms | 165.9 ms | 165.9 ms | 166.7 ms |
| B4 AXPY, 1M floats | 300 µs | 159 µs | **60 µs** | 1,138 µs |
| B4 AXPY, 10M floats | 5.18 ms | 5.47 ms | 5.31 ms | 11.57 ms |
| B5 sum, 1M doubles | 1.15 ms | 372 µs | **194 µs** | 1,103 µs |
| B5 sum, 10M doubles | 11.4 ms | 5.50 ms | 5.06 ms | 10.15 ms |

What this suggests, with the caveats above:

- On data-parallel loops, APL.Net is in Rayon's league. It is ahead where explicit SIMD matters:
  Rust's float sum is not vectorised, because reordering floating-point additions is not allowed
  by default, which is the same reason the .NET JIT doesn't do it either.
- At 10K elements, Rayon's work-stealing split costs far more than it saves. rustc also
  auto-vectorises the sequential loop, which is why Rust sequential beats everything at that size.
- At 100M, all three are bound by DRAM and agree to within 1%.

Reproduce: `cargo bench --manifest-path benchmarks/rust-rayon/Cargo.toml`.

## Reading these numbers

- One machine, one run. On more cores, the dispatch savings grow (TPL's shared chunk counter
  contends more), and so does the benefit of opting into load balancing.
- Laptop CPUs change clock speed with temperature. The standard deviations are shown; ratios
  under ~1.1× are within noise.
- The Gallery shows the same comparisons interactively on *your* machine. Its in-app harness is
  quicker and noisier than BenchmarkDotNet.
