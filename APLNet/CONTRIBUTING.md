# Contributing to APL.Net / Berkontribusi ke APL.Net

Made by Gravicode Studios, led by Kang Fadhil. Contributions are welcome. /
Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil. Kontribusi dipersilakan.

## Before you change the scheduler / Sebelum mengubah scheduler

Read [docs/en/design-decisions.md](docs/en/design-decisions.md)
([Bahasa Indonesia](docs/id/design-decisions.md)). Every trade-off there is deliberate.

**Pull requests that "fix" load imbalance by adding dynamic chunk claiming to the default
partitioner will be redirected to the opt-in `WorkStealingPartitioner` (or `StripedPartitioner`)
instead.** Dynamic claiming in the default would put TPL's per-chunk overhead back into every
loop, including the loops that don't need it. Benchmark B6 exists to show that trade-off
honestly, not to hide it.

**PR yang "memperbaiki" ketimpangan beban dengan menambahkan klaim chunk dinamis ke partitioner
default akan diarahkan ke `WorkStealingPartitioner` (atau `StripedPartitioner`) yang opt-in.**

Changes that are also out of bounds / Perubahan lain yang tidak diterima:

- Making pool workers flow `ExecutionContext` (design decision §2).
- Making the caller wait for queued work items to be dequeued. That can deadlock; see §3.
- Reflection, `Reflection.Emit` or `Activator.CreateInstance` in the hot path (NativeAOT).
- Allocating per call on the struct-body path. `AllocationTests` will fail.

## Workflow / Alur kerja

```sh
dotnet build APL.Net.slnx -c Release
dotnet test --project tests/APL.Net.Tests -c Release          # or: dotnet run --project tests/APL.Net.Tests -c Release
DOTNET_EnableAVX2=0 dotnet run --project tests/APL.Net.Tests -c Release    # SIMD fallbacks
dotnet publish tests/APL.Net.AotTests -c Release -r <rid>                  # NativeAOT checks
dotnet run -c Release --project benchmarks/APL.Net.Benchmarks -- --filter '*'
```

- **Correctness:** every parallel API must match a sequential reference over randomised inputs
  (see `CorrectnessAgainstSequential`). Cover these edge cases: `n = 0`, `n = 1`, `n < dop`,
  cancellation partway through, and an exception thrown from one chunk.
- **Performance:** quote BenchmarkDotNet numbers, not Gallery numbers, and say which machine they
  came from. If a change moves a published number, measure again rather than editing the prose.
- **Inlining:** after touching `ParallelJob` or the adapters, check the JIT output again as
  described in design decision §4.
- **Documentation:** `docs/en` and `docs/id` are parallel. Update both. Screenshots are real
  captures from `APL.Net.Gallery --screenshot docs/images`; retake them after UI changes.
- **Attribution:** every source file starts with
  `// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.` Keep it.
- **Naming:** standard C# conventions. The namespace is `AplNet` and the entry point is `Apl`.
