# APL.Net — Progress / Kemajuan

Made by Gravicode Studios, led by Kang Fadhil. Direction lives in [PLAN.md](PLAN.md); this file
tracks what is done and **how each item was verified**. A box is ticked only when something
automated proves it. The closing section lists what the ticks do *not* prove.

Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil. Arah pengembangan ada di
[PLAN.md](PLAN.md); berkas ini mencatat apa yang sudah selesai dan **bagaimana setiap butir
diverifikasi**. Kotak hanya dicentang jika ada hal otomatis yang membuktikannya. Bagian penutup
berisi hal-hal yang *tidak* dibuktikan oleh centang-centang itu.

## Functional requirements / Kebutuhan fungsional

- [x] **FR1**: `Apl.For` (struct and delegate), `Apl.ForEach` (array or `Memory<T>`, by ref or by value), plus `ForRange` and `Reduce`. Verified by `ParallelExecutorTests` and `CorrectnessAgainstSequential`
- [x] **FR2**: SIMD map, map-reduce and in-place transform for float/double/int/long. Verified by `SimdOpsTests` at 19 lengths per type
- [x] **FR3**: cancellation and `AggregateException`, TPL-compatible. Covered by 10 dedicated tests, including cancellation partway through with every partitioner
- [x] **FR4**: `MaxDegreeOfParallelism`, defaulting to `ProcessorCount`. `NeverRunsMoreBodiesAtOnceThanTheDegreeOfParallelism` checks it with a concurrency probe
- [x] **FR5**: the `AplNet.Unsafe` tier. Verified by `UnsafeParallelTests`
- [x] **FR6**: pluggable static, striped and work-stealing partitioners. Verified by `PartitionerTests` and `WorkStealingTests`
- [x] **FR7**: zero steady-state allocation on the struct path. `AllocationTests` measures < 8 B per call process-wide over 2,000 calls, and BenchmarkDotNet B7 confirms it
- [x] **FR8**: NativeAOT. `APL.Net.AotTests` publishes with `PublishAot=true` (trim and AOT warnings are errors, and there are none) and all of its checks pass

## Non-functional requirements / Kebutuhan non-fungsional

- [~] **NFR1**: ≥ 2× over `Parallel.For` for granular work. **Met** in cache and for repeated
  calls (B1 at 10K, B7). **Not met** at 1M+ elements of trivial work, where every variant is bound
  by memory bandwidth. See [benchmark results](docs/en/benchmark-results.md) for the numbers and
  the reason. This is recorded honestly rather than ticked.
- [x] **NFR2**: the default API is memory-safe; unsafe code lives only in `AplNet.Unsafe`
- [x] **NFR3**: scalar fallback. The whole suite passes with `DOTNET_EnableAVX2=0` and with `DOTNET_EnableHWIntrinsic=0`
- [x] **NFR4**: JIT and NativeAOT, no `Reflection.Emit`. `IsAotCompatible` is set, and the AOT app passes
- [x] **NFR5**: scheduling is testable independently of the core count, via `FixedParallelismProvider`
- [x] **NFR6**: EventSource counters, off by default. Verified by `DiagnosticsTests`

## Feature list / Daftar fitur

- [x] v1.0: struct invoker, delegate overloads, `AplOptions`, `ForPtr`, SIMD transform and map-reduce, `StaticRangePartitioner`, exception aggregation, BenchmarkDotNet project, Rust/Rayon reference (out of band)
- [x] v1.1: `StripedPartitioner`, `ForEachAsync`, Vector512 path with fallback, source generator
- [x] v2.0: `WorkStealingPartitioner`, EventSource counters
- [ ] v2.0: experimental distributed mode. Out of scope by design (spec §9; see PLAN.md)

## Deliverables / Hasil kerja

- [x] Library `src/APL.Net` (package `APL.Net`, generator included), 0 build warnings
- [x] Test suite: 113 tests, green on three repeated runs and with AVX2 or hardware intrinsics disabled
- [x] JIT inlining of struct bodies verified from `DOTNET_JitDisasm` output (design decisions §4)
- [x] Benchmarks B1–B7 run on the reference machine (66 benchmarks, 24 min). Results are in `docs/*/benchmark-results.md`, with the raw reports in `docs/benchmarks/`
- [x] Rust/Rayon reference run out of band. Results are in the benchmark docs
- [x] Avalonia **APL.Net Gallery**: 14 cases, APL.Net vs TPL, code, live benchmark, lane charts, EN/ID
- [x] Console samples: `QuickStart`, `ImageProcessingDemo`
- [x] Documentation: 13 pages each in `docs/en` and `docs/id`, plus a bilingual README
- [x] Screenshots: real captures made by `APL.Net.Gallery --screenshot`
- [x] `CONTRIBUTING.md`, `PLAN.md`, `CLAUDE.md`
- [x] CI workflows (in the monorepo root: `.github/workflows/aplnet-ci.yml` and `aplnet-publish.yml`): build, tests at three SIMD levels, NativeAOT, pack, tag-driven publish (`APLNet-v*`)
- [x] **Published `APL.Net` 1.0.0 to nuget.org** (2026-09-25), with the symbol package. Before pushing, the nupkg was installed into a fresh console project from a local feed: `[AplBody]` generation and `SimdOps` both worked

## What the ticks do not prove / Yang tidak dibuktikan oleh centang

- Performance figures come from **one machine** (Intel i7-8650U, 4 cores / 8 threads, laptop).
  Machines with more cores, or with AVX-512, have not been measured.
- **Arm64** runs only in CI (the macOS runner). Nobody has profiled it.
- The **Rust/Rayon** reference was run once (rustc 1.97.1, Criterion). It is a different runtime
  and harness, so its numbers are illustrative only.
- Gallery timings come from a desktop in-app harness. With the default static partitioner, the
  *Image filter* and *Mandelbrot* cases can lose to `Parallel.For` on a busy machine. The Gallery
  shows this, and PLAN.md 1.1 proposes an opt-in remedy.
- The xunit suite runs under the JIT only. NativeAOT is covered by the separate check app.
