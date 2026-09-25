# Troubleshooting & FAQ

[Bahasa Indonesia](../id/troubleshooting.md) · [Index](README.md)

## When *not* to use APL.Net

- **Small `n`.** Waking even one pool thread costs microseconds. A loop that finishes in a
  microsecond or two on one thread won't get faster. Use a plain `for`, or set `MinChunkSize`
  so APL.Net runs it inline.
- **Uneven work with the default partitioner.** If iteration costs vary widely, the static split
  loses to `Parallel.For`. Choose `WorkStealingPartitioner`, or just use TPL.
- **I/O-bound bodies.** If the body waits on disk or the network, dispatch cost is irrelevant.
  Use `Parallel.ForEachAsync` (or `Apl.ForEachAsync`, which wraps it).
- **Bodies that rely on `AsyncLocal<T>`, culture or impersonation** flowing from the caller.
  The ExecutionContext does not flow. Pass the state in explicitly, or use TPL.
- **Memory-bound loops over very large arrays.** At 1M+ elements of trivial work, every
  variant, sequential included, hits memory bandwidth. See B1 at 100M in
  [benchmark results](benchmark-results.md). SIMD and threads cannot beat DRAM.

## Common surprises

**My loop is slower with `Apl.For` than with `Parallel.For`.**
The work is probably uneven, or the machine is busy. With static partitioning, one slow or
descheduled core delays the whole loop. Look at the lanes (Gallery or a profiler) and try
`StripedPartitioner` or `WorkStealingPartitioner`.

**`AsyncLocal` values are null inside my body.**
That is by design. See [design decisions §2](design-decisions.md#2-unsafequeueuserworkitem-no-executioncontext-flow).

**Floating-point sums differ slightly from a `for` loop.**
Vector and parallel reductions add in a different order. The result is reproducible for a fixed
degree of parallelism and partitioner. Integer sums match exactly.

**`ArgumentException: Destination partially overlaps the source`.**
A transform's destination must be either exactly the source memory (in place) or separate memory.

**`InvalidOperationException` from my partitioner.**
`GetPartitionCount` returned less than 1.

**My struct body does not seem to be inlined.**
Pass it by its concrete type (`Apl.For(0, n, new MyBody(...))`), not through an `IWorkBody`
variable. Keep `Invoke` small, or mark it `[MethodImpl(MethodImplOptions.AggressiveInlining)]`.
You can check with `DOTNET_JitDisasm`, as described in
[design decisions §4](design-decisions.md#4-devirtualisation-is-verified-not-assumed).

## Development

**`dotnet test` fails with "Testing with VSTest target is no longer supported".**
The repository's `global.json` opts `dotnet test` into Microsoft.Testing.Platform. Run it from the
repository root, or run the test project directly:
`dotnet run --project tests/APL.Net.Tests -c Release`.

**Builds fail with MSB3021 (file locked).**
The Gallery or a benchmark is still running. Close it first.

**NativeAOT publish fails with `'vswhere.exe' is not recognized`.**
See [NativeAOT](native-aot.md#windows-prerequisites).
