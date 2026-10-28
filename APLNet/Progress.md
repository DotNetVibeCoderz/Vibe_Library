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
- [ ] v2.0: experimental distributed mode. Out of scope by design: spec §9 says to build it as a separate project, with APL.Net as the per-node kernel (see PLAN.md)

## Release 1.1.0 / Rilis 1.1.0

Everything on the 1.1 and 1.2 roadmap stages. Each item names what proves it.

Seluruh tahap roadmap 1.1 dan 1.2. Setiap butir menyebutkan buktinya.

- [x] **Straggler tolerance**: `StaticRangePartitioner(partitionsPerWorker)`. `StragglerTolerantStaticSplitsEachShareIntoKPartitions` and the overflow test check the arithmetic. It is in the partitioner list every executor test runs, and B9 measures it
- [x] **`long` ranges**: `LongRangeTests` covers every index exactly once with every partitioner, ranges beyond 2³¹ including the full `[long.MinValue, long.MaxValue)`, cancellation, exceptions, and the check that `int` arguments still pick the `int` overloads
- [x] **Loop state**: `Stop` / `Break` / `ShouldExitCurrentIteration`. `LoopStateTests` checks with every partitioner that every iteration below the break runs, and also covers nested loops and the Stop/Break conflict
- [x] **SIMD `Exp` / `Log` / FMA**: `SimdMathOperatorTests` runs 17 lengths per type, special values, an exact FMA check, and the single rounding; B10 measures them
- [x] **Generated API reference**: `docs/api/`, 51 type pages. CI regenerates it and fails on any difference
- [x] **Spinning workers**: `SpinningWorkersTests` covers every partitioner, reductions, 20,000 back-to-back loops, workers parked between every loop, nested loops, concurrent callers, disposal, failures and cancellation. Stable placement is checked by `SpinningWorkerPlacementTests`, and B8 measures it
- [x] **Core / NUMA pinning and topology**: `TopologyTests` (CPU-list parsing, two-node placement), `AffinityIsAppliedWhereThePlatformSupportsIt` (Windows, Linux x64 and Linux Arm64 in CI), and the NativeAOT app
- [x] **Arm64 on real hardware**: CI runs the suite, the scalar-only rerun and NativeAOT on `ubuntu-24.04-arm` and `macos-latest`. BenchmarkDotNet on Arm64 is available on demand through the `APL.Net Arm64 benchmarks` workflow
- [x] **Zero allocation still holds**: `AllocationTests` adds `long` loops, `ForWithState` and spinning workers, each under 8 B per call
- [x] **Inlining re-verified** after the `ParallelJob` / `Bodies` changes: `DOTNET_JitDisasm` shows that the element, `long` and stateful bodies are fully inlined into `InvokeRange`, with no call left in the loop
- [x] **Package icon** (`tools/icon/make_icon.py`), packed as `icon.png`. Gallery variants for spinning workers and straggler tolerance, with retaken screenshots
- [x] **Package 1.1.0** packed and installed from a local feed into a fresh console project. Loop state, `long` ranges, `[AplBody]`, `LogOperator` and pinning all worked

## Deliverables / Hasil kerja

- [x] Library `src/APL.Net` (package `APL.Net`, generator included), 0 build warnings
- [x] Test suite: 172 tests (113 in 1.0.0), green on three repeated runs and with AVX-512, AVX2 or hardware intrinsics disabled
- [x] JIT inlining of struct bodies verified from `DOTNET_JitDisasm` output (design decisions §4)
- [x] Benchmarks B1–B7 run on the reference machine (66 benchmarks, 24 min). Results are in `docs/*/benchmark-results.md`, with the raw reports in `docs/benchmarks/`
- [x] Rust/Rayon reference run out of band. Results are in the benchmark docs
- [x] Avalonia **APL.Net Gallery**: 14 cases, APL.Net vs TPL, code, live benchmark, lane charts, EN/ID
- [x] Console samples: `QuickStart`, `ImageProcessingDemo`
- [x] Documentation: 14 pages each in `docs/en` and `docs/id` (1.1 added `workers.md`), plus a bilingual README and the generated `docs/api`
- [x] Screenshots: real captures made by `APL.Net.Gallery --screenshot`
- [x] `CONTRIBUTING.md`, `PLAN.md`, `CLAUDE.md`
- [x] CI workflows (in the monorepo root: `.github/workflows/aplnet-ci.yml`, `aplnet-publish.yml`, `aplnet-arm64.yml`): build, tests at three SIMD levels on x64 and Arm64, NativeAOT on linux-x64 and linux-arm64, an API-reference drift check, pack, and a tag-driven publish (`APLNet-v*`) using the `NUGET_API_KEY` secret, which also creates a GitHub release
- [x] **Published `APL.Net` 1.0.0 to nuget.org** (2026-09-25), with the symbol package. Before pushing, the nupkg was installed into a fresh console project from a local feed: `[AplBody]` generation and `SimdOps` both worked

## What the ticks do not prove / Yang tidak dibuktikan oleh centang

- Performance figures come from **one machine** (Intel i7-8650U, 4 cores / 8 threads, laptop).
  Machines with more cores, or with AVX-512, have not been measured.
- **NUMA placement** is correct by test (synthetic two-node topology), but its *speed-up* has not
  been measured: the reference machine has one node.
- **Arm64** is tested in CI on real hardware (Apple silicon, Neoverse). Benchmarks there run on
  shared hosted runners, so they are indicative, not profiled.
- **B9** varies from run to run on a laptop. Both runs are published, and they disagree in one
  column.
- The **Rust/Rayon** reference was run once (rustc 1.97.1, Criterion). It is a different runtime
  and harness, so its numbers are illustrative only.
- Gallery timings come from a desktop in-app harness. With the default static partitioner, the
  *Image filter* and *Mandelbrot* cases can lose to `Parallel.For` on a busy machine. The Gallery
  shows this, and PLAN.md 1.1 proposes an opt-in remedy.
- The xunit suite runs under the JIT only. NativeAOT is covered by the separate check app.
