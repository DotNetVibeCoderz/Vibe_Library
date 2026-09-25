# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Scope

`APLNet/` is one project inside the **Vibe_Library** monorepo (the git root is one level up,
alongside `ActorNet`, `RLNet`, …). Keep all work inside `APLNet/`; do not touch sibling projects.
Workflows live in the **root** `.github/workflows/` (`aplnet-ci.yml`, `aplnet-publish.yml`),
path-scoped to `APLNet/**`. Release tags are namespaced: `APLNet-v1.0.0`.

`requirements.md` is the original spec. The owner's notes at its end override the body: target
**.NET 10**, the Avalonia **APL.Net Gallery** sample, bilingual (EN/ID) docs and README with real
screenshots, `Progress.md` (status) and `PLAN.md` (roadmap) kept separate and current, and the
credit "Gravicode Studios, led by Kang Fadhil" in the app and docs. The NuGet API key lives in
`C:\Users\mifma\Documents\CodeSandbox\PackageCredentials.txt`. Read it only at push time, and
never copy it into the repo.

## Commands

```sh
dotnet build APL.Net.slnx -c Release
dotnet test --project tests/APL.Net.Tests -c Release                       # 113 tests, ~10 s
dotnet test --project tests/APL.Net.Tests -c Release --filter-class "*ParallelExecutorTests"
dotnet run  --project tests/APL.Net.Tests -c Release                       # same suite, directly
DOTNET_EnableAVX2=0 dotnet run --project tests/APL.Net.Tests -c Release    # SIMD fallback paths
dotnet publish tests/APL.Net.AotTests -c Release -r win-x64 -o out/aot && out/aot/APL.Net.AotTests.exe
dotnet run -c Release --project benchmarks/APL.Net.Benchmarks -- --filter '*Simd*'
dotnet run --project samples/APL.Net.Gallery -c Release [-- --case zipf --lang id]
dotnet run --project samples/APL.Net.Gallery -c Release -- --screenshot docs/images   # regenerates every screenshot
dotnet pack src/APL.Net/APL.Net.csproj -c Release -o artifacts
cargo bench --manifest-path benchmarks/rust-rayon/Cargo.toml                # out-of-band reference
```

- `dotnet test` works only because `global.json` opts into Microsoft.Testing.Platform (xunit.v3).
  Without that setting, it fails with "VSTest target is no longer supported".
- NativeAOT publish on Windows needs `C:\Program Files (x86)\Microsoft Visual Studio\Installer`
  on `PATH` (for `vswhere.exe`).
- A running Gallery or benchmark locks the DLLs (MSB3021). Close it before building.
- **Never build while benchmarks are running.** Close to 100% of the numbers depend on idle cores.

## Architecture

The package is `APL.Net`, but the namespace is `AplNet` and the entry point is `Apl` (standard C#
acronym casing; `APL.For` inside namespace `APL` would not compile). Five layers:

1. `Apl` (delegate + struct overloads) → 2. body interfaces `IWorkBody`, `IWorkBody<T>`,
`IRangeWorkBody`, `IReduceBody<T>` → 3. `ParallelExecutor` + `Core/ParallelJob.cs` + partitioners →
4. `Simd/` (`SimdOps`, operator structs, `SimdKernels`) → 5. `Unsafe/` (`UnsafeParallel`, `NativeBuffer<T>`).

Every public loop is adapted (in `Core/Bodies.cs`) to a single internal shape, `IRangeWorkBody`,
and run by a pooled `RangeJob<TBody>` or `ReduceJob<T,TBody>`. The job **is** the
`IThreadPoolWorkItem`. It is queued `workers-1` times with `UnsafeQueueUserWorkItem`, and the
caller works too.

## Invariants: don't "fix" these (docs/en/design-decisions.md explains each)

- **Static partitioning is the default.** No dynamic or interlocked chunk claiming in it. Load
  balance is opt-in (`WorkStealingPartitioner`, `StripedPartitioner`). Static *loses* on uneven
  work (B6, the Gallery's Mandelbrot and Zipf cases). That loss is documented on purpose.
- **ExecutionContext does not flow** to pool workers. A test pins this behaviour.
- **The caller never waits for a queued item to be dequeued**, only for partitions that are
  running. Late items are handled by the job's reference count. Waiting for them deadlocks under
  a busy pool or nested loops.
- **Zero allocation on the struct path.** `AllocationTests` enforces it. Pools are per body type.
- **The body is copied to a local before looping** (`InvokeRange`). This is what lets the JIT keep
  fields in registers and inline `Invoke`. It was verified with `DOTNET_JitDisasm`. Re-check after
  touching `ParallelJob` or `Bodies`.
- **Spans never cross threads.** Parallel APIs take arrays or `Memory<T>`.
- **No reflection or `Reflection.Emit` in the library.** `IsAotCompatible` is on, and
  `APL.Net.AotTests` treats trim and AOT warnings as errors.
- **SIMD operator interfaces have no default methods** (DIMs box struct implementers).
- Inside `AplNet.*` code, `Unsafe.X` resolves to the `AplNet.Unsafe` *namespace*. Use an alias
  (`MemUnsafe`) or the full `System.Runtime.CompilerServices.Unsafe` name.

## Benchmarks and numbers

The figures in the docs were measured on an Intel i7-8650U (4C/8T, laptop on AC, High
performance plan), with .NET 10.0.12 on Windows 11. At 1M+ elements, trivial loops are bound by
memory bandwidth, and every variant converges. That is why NFR1 is marked partially met in
`Progress.md`. If you change something that moves a number, re-measure rather than editing the
prose. Gallery timings (in-app harness) are illustrative; quote BenchmarkDotNet figures.

## Documentation

`docs/en/` and `docs/id/` are parallel (13 files each, same file names). **Update both together.**
The README is bilingual in one file. `docs/images/` holds real captures from the Gallery's
`--screenshot` mode. Retake them after UI changes.

## Attribution

Every source file starts with `// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.`, and
`Directory.Build.props` puts the same credit into assembly metadata. Keep both when adding files.
