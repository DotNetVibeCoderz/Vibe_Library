# APL.Net documentation

[Bahasa Indonesia](../id/README.md)

APL.Net ("Another Parallel Library") gives .NET data-parallel loops without the per-iteration
overhead of `Parallel.For`, plus SIMD kernels, an opt-in pointer tier and a source generator.
It is built and maintained by **Gravicode Studios, led by Kang Fadhil**.

| Page | What it covers |
|---|---|
| [Getting started](getting-started.md) | Install it, then go step by step: delegate → struct body → range body → SIMD → unsafe |
| [Architecture](architecture.md) | The five layers, and how one call moves through them |
| [Design decisions](design-decisions.md) | **Read this before changing the scheduler.** Why each trade-off was made |
| [API guide](api-guide.md) | `Apl`, `AplOptions`, the body interfaces, exceptions and cancellation |
| [Partitioners](partitioners.md) | Static, striped and work-stealing partitioning, and how to pick one |
| [SIMD](simd.md) | `SimdOps`, operator structs, custom operators, and hardware fallback |
| [Unsafe tier](unsafe.md) | `UnsafeParallel`, function pointers and `NativeBuffer<T>` |
| [Source generator](source-generator.md) | `[AplBody]` / `[AplRangeBody]` and diagnostics APL001–APL008 |
| [Diagnostics](diagnostics.md) | The `AplNet` EventSource counters |
| [NativeAOT](native-aot.md) | Publishing with AOT, and which vector widths you get |
| [Gallery](gallery.md) | The Avalonia showcase app, and how its screenshots are made |
| [Benchmark results](benchmark-results.md) | BenchmarkDotNet numbers for B1–B7, plus the Rust/Rayon reference |
| [Troubleshooting & FAQ](troubleshooting.md) | When not to use APL.Net, and common surprises |

![APL.Net Gallery overview](../images/gallery-overview.png)
