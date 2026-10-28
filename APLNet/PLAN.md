# APL.Net — Roadmap / Rencana Pengembangan

Made by Gravicode Studios, led by Kang Fadhil.

This file sets **direction**: what APL.Net is for, and what comes next. **Status**, meaning what
is built and how it was verified, lives in [Progress.md](Progress.md).

Berkas ini berisi **arah**: untuk apa APL.Net dibuat dan apa langkah berikutnya. **Status**
(apa yang sudah dibangun dan bagaimana diverifikasi) ada di [Progress.md](Progress.md).

## Purpose / Tujuan

Data-parallel primitives for .NET that remove what `Parallel.For` pays on every iteration: a
delegate call, dynamic chunk claiming, `Task` allocation and ExecutionContext flow. On top of
that come explicit SIMD, with no native interop on the default path.

Primitif paralel-data untuk .NET yang menghapus biaya yang dibayar `Parallel.For` di setiap
iterasi: pemanggilan delegate, klaim chunk dinamis, alokasi `Task`, dan aliran ExecutionContext.
Di atasnya ditambahkan SIMD eksplisit, tanpa interop native di jalur default.

## Milestones / Tahapan

The spec staged the features as v1.0 / v1.1 / v2.0. The first release, **1.0.0**, delivered all
three stages. The roadmap that followed it had two more stages, 1.1 and 1.2. Release **1.1.0**
delivers both, so the roadmap is complete apart from the items under *Later*.

Spesifikasi membagi fitur menjadi v1.0 / v1.1 / v2.0. Rilis pertama, **1.0.0**, memuat ketiga tahap
itu. Roadmap sesudahnya punya dua tahap lagi, 1.1 dan 1.2. Rilis **1.1.0** memuat keduanya, jadi
roadmap sudah selesai, kecuali butir di bawah *Nanti*.

### 1.0.0: delivered / sudah dikirim
- Struct-invoker loops `For` / `ForRange` / `ForEach` / `Reduce`, delegate overloads, and `AplOptions`
- Static, striped and work-stealing partitioners (the spec placed work stealing in v2.0)
- SIMD kernels for Vector128/256/512 with a scalar fallback; map, map-reduce and transform for float/double/int/long
- The unsafe tier (`UnsafeParallel`, `NativeBuffer<T>`)
- The `[AplBody]` / `[AplRangeBody]` source generator (spec: v1.1)
- `ForEachAsync` / `ForAsync` (spec: v1.1)
- EventSource counters (spec: v2.0)
- The Avalonia Gallery, the console samples, BenchmarkDotNet B1–B7, and a Rust/Rayon reference
- Bilingual documentation, verified NativeAOT support

### 1.1.0: delivered / sudah dikirim
Roadmap stage 1.1:
- **Straggler tolerance for static loops**, opt-in: `new StaticRangePartitioner(partitionsPerWorker: k)`.
  Still static and claimed per partition, per design decision §1. Measured in B9.
  *Toleransi straggler opt-in untuk loop statis.*
- **`long` ranges**: `Apl.For(long, long, ...)` and `Apl.ForRange(long, long, ...)`, struct and
  delegate, up to the full `[long.MinValue, long.MaxValue)`.
  *Rentang `long`.*
- **Loop state**: `AplLoopState.Stop()` / `Break()`, equivalent to `ParallelLoopState`. It is checked
  before every iteration, which is finer than the block boundaries planned, at the cost of one load
  and one compare. `ForWithState` covers the zero-allocation struct path.
  *`Stop()` / `Break()` setara `ParallelLoopState`, diperiksa sebelum setiap iterasi.*
- **More SIMD operators**: `ExpOperator`, `LogOperator`, and `FusedMultiplyAddOperator` as the explicit
  FMA opt-in. Measured in B10.
  *Operator SIMD tambahan.*
- **Generated API reference** in [docs/api](docs/api/README.md), from the XML documentation by
  `tools/APL.Net.ApiDocs`. CI fails if it drifts.
  *Referensi API yang dibangkitkan dari dokumentasi XML.*

Roadmap stage 1.2 (it was marked exploratory):
- **Persistent spinning workers**, opt-in: `SpinningWorkers` in `AplOptions.Workers`, with stable
  partition placement. Measured in B8.
  *Worker yang tetap berputar (opt-in).*
- **NUMA-aware placement**: `WorkerAffinity.Core` / `NumaNode` pin the workers, and `AplTopology`
  reads the NUMA layout on Windows and Linux. Contiguous partitions stay on one node.
  *Penempatan partisi yang sadar NUMA.*
- **Arm64 on real hardware**: CI runs the suite and NativeAOT on `ubuntu-24.04-arm` (Neoverse) and
  `macos-latest` (Apple silicon). The `APL.Net Arm64 benchmarks` workflow measures it with
  BenchmarkDotNet.
  *Validasi Arm64 di perangkat nyata.*

Also in 1.1.0: a package icon, a GitHub release per tag, and Gallery variants for spinning workers
and straggler tolerance.

### Next / Berikutnya
Release 1.1.0 left only questions that need hardware we do not have:
- **Measure NUMA placement on a multi-socket server.** The mapping is unit-tested, but the speed-up
  has not been measured. *Ukur penempatan NUMA di server multi-socket.*
- **Profile Arm64 beyond hosted runners**, for example on a Graviton or Ampere bare-metal machine.
  *Profiling Arm64 di luar runner bersama.*
- **Measure on a machine with more cores and with AVX-512.** *Ukur di mesin dengan lebih banyak core dan AVX-512.*

### Later / Nanti
- **Distributed execution** is out of scope for this library (spec §9). The recommended shape is
  APL.Net as the kernel *inside* each node, with Orleans or a queue-based worker pool
  orchestrating the nodes. That would be a separate project with its own spec.
  *Eksekusi terdistribusi di luar cakupan pustaka ini; APL.Net menjadi kernel di dalam setiap
  node, sedangkan orkestrasi antar-node ditangani Orleans atau worker pool berbasis antrean.*
- **GPU offload** is a non-goal.
  *Offload ke GPU bukan tujuan.*

## Principles that do not change / Prinsip yang tidak berubah

1. The default path stays static. Balancing is always opt-in. *Jalur default tetap statis;
   penyeimbangan selalu opt-in.*
2. Steady state allocates nothing. *Kondisi stabil tanpa alokasi.*
3. No reflection or runtime code generation in the hot path. The library stays NativeAOT-safe.
   Native calls are limited to opt-in thread affinity, through compile-time generated `LibraryImport`.
   *Tanpa reflection/pembangkitan kode saat runtime di jalur panas; tetap aman untuk NativeAOT.
   Panggilan native hanya untuk afinitas thread yang opt-in.*
4. Every performance claim is measured and reproducible, including the cases where TPL wins.
   *Setiap klaim performa diukur dan bisa diulang, termasuk saat TPL menang.*
