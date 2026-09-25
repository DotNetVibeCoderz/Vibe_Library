# APL.Net — Roadmap / Rencana Pengembangan

Made by Gravicode Studios, led by Kang Fadhil.

This file sets **direction**: what APL.Net is for, and what comes next. **Status**, meaning what
is built and how it was verified, lives in [Progress.md](Progress.md).

Berkas ini berisi **arah**: untuk apa APL.Net dibuat dan apa langkah berikutnya. **Status**
(apa yang sudah dibangun dan bagaimana diverifikasi) ada di [Progress.md](Progress.md).

## Purpose / Tujuan

Data-parallel primitives for .NET that remove what `Parallel.For` pays on every iteration: a
delegate call, dynamic chunk claiming, `Task` allocation and ExecutionContext flow. On top of
that come explicit SIMD, with no native interop.

Primitif paralel-data untuk .NET yang menghapus biaya yang dibayar `Parallel.For` di setiap
iterasi: pemanggilan delegate, klaim chunk dinamis, alokasi `Task`, dan aliran ExecutionContext.
Di atasnya ditambahkan SIMD eksplisit, tanpa interop native.

## Milestones / Tahapan

The spec staged the features as v1.0 / v1.1 / v2.0. The first release, **1.0.0**, delivers all
three stages, apart from the items listed under *Later*.

Spesifikasi membagi fitur menjadi v1.0 / v1.1 / v2.0. Rilis pertama, **1.0.0**, memuat ketiga
tahap itu, kecuali butir yang tercantum di bawah *Nanti*.

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

### 1.1: next / berikutnya
- **Opt-in straggler tolerance for static loops.** On busy machines, one descheduled core delays
  a static loop (see the Gallery's image filter). The idea: a `StaticRangePartitioner` option that
  splits into `k × workers` partitions, so late workers take the leftovers. It must stay opt-in,
  per design decision §1.
  *Toleransi straggler opt-in untuk loop statis (dibagi `k × workers` partisi).*
- **`long` ranges** (`Apl.For(long, long, ...)`) for loops over more than 2³¹ items.
  *Rentang `long` untuk loop di atas 2³¹ item.*
- **Loop state:** `Stop()` / `Break()`, equivalent to `ParallelLoopState`, checked at block
  boundaries.
  *`Stop()` / `Break()` setara `ParallelLoopState`, diperiksa di batas blok.*
- **More SIMD operators:** `Exp`, `Log`, and `FusedMultiplyAdd` as an explicit opt-in.
  *Operator SIMD tambahan.*
- **Generated API reference** from the XML documentation.
  *Referensi API yang dibangkitkan dari dokumentasi XML.*

### 1.2: exploratory / eksplorasi
- **Persistent spinning workers** as an opt-in: for very frequent, very small loops (game and
  simulation ticks), keep the workers spinning instead of waking pool threads. This trades CPU for
  latency.
  *Worker yang tetap berputar (opt-in) untuk loop kecil yang sangat sering.*
- **NUMA-aware partition placement** for large servers.
  *Penempatan partisi yang sadar NUMA untuk server besar.*
- **Arm64 validation** on real hardware. AdvSimd is exercised today only through the Vector128 path.
  *Validasi Arm64 di perangkat nyata.*

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
   *Tanpa reflection/pembangkitan kode saat runtime di jalur panas; tetap aman untuk NativeAOT.*
4. Every performance claim is measured and reproducible, including the cases where TPL wins.
   *Setiap klaim performa diukur dan bisa diulang, termasuk saat TPL menang.*
