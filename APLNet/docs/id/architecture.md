# Arsitektur

[English](../en/architecture.md) · [Indeks](README.md)

## Lapisan

```
┌──────────────────────────────────────────────┐
│  API ergonomis    Apl.For / ForEach / Reduce │  overload delegate, bentuk panggilan TPL
├──────────────────────────────────────────────┤
│  Struct invoker   IWorkBody, IRangeWorkBody, │  generik atas struct Anda:
│                   IWorkBody<T>, IReduceBody  │  JIT menspesialisasi dan meng-inline
├──────────────────────────────────────────────┤
│  Penjadwalan      ParallelExecutor, job dari │  partisi sekali, antre tanpa Task
│                   pool, IPartitioner         │  maupun ExecutionContext, pemanggil ikut kerja
├──────────────────────────────────────────────┤
│  Kernel SIMD      SimdOps, struct operator   │  Vector512/256/128, ekor skalar
├──────────────────────────────────────────────┤
│  Tingkat unsafe   UnsafeParallel, NativeBuf. │  pointer, delegate*, tanpa cek batas
└──────────────────────────────────────────────┘
```

| Namespace | Isi |
|---|---|
| `AplNet` | `Apl`, `AplOptions`, `[AplBody]`, `[AplRangeBody]` |
| `AplNet.Core` | Interface body, `ParallelExecutor`, `IParallelismProvider`, mesin job (internal) |
| `AplNet.Core.Partitioners` | `IPartitioner`, `StaticRangePartitioner`, `StripedPartitioner`, `WorkStealingPartitioner` |
| `AplNet.Simd` | `SimdOps`, `SimdCapabilities`, interface operator dan operator bawaan |
| `AplNet.Unsafe` | `UnsafeParallel`, `NativeBuffer<T>` |
| `AplNet.Diagnostics` | `AplEventSource` |

## Satu bentuk di dalam

Setiap loop publik diubah ke satu bentuk internal, yaitu `IRangeWorkBody` ("jalankan
`[from, to)`"). Konversinya dilakukan oleh beberapa adaptor kecil berupa `struct`:

- `ElementBody<TBody>` menjalankan loop atas `IWorkBody.Invoke(i)`.
- `DelegateBody` menjalankan loop atas `Action<int>`.
- `ArrayItemBody<T, TBody>` menjalankan loop atas span array dan memberikan tiap elemen via `ref`.
- Operasi SIMD membungkus satu panggilan kernel per bagian.

Karena setiap adaptor adalah struct, JIT menspesialisasi langsung hingga ke body Anda. Satu-satunya
dispatch saat runtime yang tersisa adalah satu panggilan virtual per rentang, dari job ke body-nya.

## Satu panggilan, langkah demi langkah

1. **Rencana** (`ParallelExecutor.TryPlan`). Executor memvalidasi rentang, memeriksa token, dan
   membaca derajat paralelisme: dari `AplOptions.MaxDegreeOfParallelism`, atau dari
   `IParallelismProvider` yang default-nya `Environment.ProcessorCount`. Jumlah worker dibatasi
   `ceil(n / MinChunkSize)`, lalu partitioner ditanya berapa banyak partisi yang dibuat. Jika hanya
   ada satu worker, loop berjalan **inline** di pemanggil, tanpa job dan tanpa antrean.
2. **Sewa job.** Setiap tipe body punya pool job kecil yang lock-free (`RangeJob<TBody>`,
   `ReduceJob<T, TBody>`). Dalam kondisi stabil, menyewa dan mengembalikan job tidak mengalokasi.
3. **Antrekan.** Job itu sendiri adalah `IThreadPoolWorkItem`. Job diantrikan `workers - 1` kali
   lewat `ThreadPool.UnsafeQueueUserWorkItem(item, preferLocal: false)`. Panggilan ini tidak
   membuat `Task`, closure, maupun context yang ditangkap.
4. **Bekerja.** Pemanggil menjadi salah satu worker. Cara worker mendapat bagiannya bergantung
   pada partitioner:
   - **Statis atau striped:** tiap worker mengklaim *partisi* berikutnya dengan satu
     `Interlocked.Increment`. Satu increment per partisi, tidak pernah per iterasi.
   - **Work stealing:** tiap worker mengklaim satu *slot*. Ia mengambil grain dari depan rentangnya
     sendiri dengan CAS, dan saat rentangnya habis, mencuri separuh belakang rentang terpenuh milik
     worker lain.
5. **Gabung.** Worker menghitung partisi yang selesai (atau iterasi, pada work stealing). Worker
   terakhir yang selesai men-set `ManualResetEventSlim` milik job. Pemanggil berputar sebentar,
   lalu menunggunya. **Pemanggil hanya menunggu pekerjaan yang benar-benar sudah dimulai, tidak
   pernah menunggu item antrean diambil.** Jika pool sedang sibuk, pemanggil mengklaim sendiri
   partisi yang tersisa. Karena itulah loop bersarang tidak bisa deadlock.
6. **Lepas.** Job memegang reference count: satu untuk pemanggil dan satu per item antrean. Item
   antrean yang baru berjalan setelah loop selesai tidak menemukan apa pun untuk diklaim, lalu
   melepas referensinya. Saat referensi terakhir dilepas, job mengosongkan body-nya (agar tidak
   menahan referensi apa pun) dan kembali ke pool.
7. **Hasil.** Exception dari body dikumpulkan, dan satu `AggregateException` dilempar setelah
   semua worker yang berjalan berhenti. Pembatalan diperiksa di antara blok sepanjang
   `CancellationCheckInterval` iterasi dan muncul sebagai `OperationCanceledException`, sama
   seperti TPL.

## Mengapa loop dalam cepat

Untuk setiap `(from, to)` yang dijalankan, job menyalin struct body ke variabel lokal. Dengan begitu
JIT bisa menyimpan field body (referensi array, skalar) di register selama loop dalam berjalan.
Body yang mutable juga tidak bisa berebut data dengan worker lain lewat field bersama.

Untuk `Apl.For(0, n, new Affine(src, dst))`, loop dalam yang benar-benar dihasilkan JIT adalah
berikut ini. Pengecekan batas sudah diangkat keluar lewat loop cloning:

```asm
G_M000_IG04:
       mov      r10d, edx
       vmulsd   xmm2, xmm0, qword ptr [rax+8*r10+0x10]
       vaddsd   xmm2, xmm2, xmm1
       vmovsd   qword ptr [rcx+8*r10+0x10], xmm2
       inc      edx
       cmp      edx, r8d
       jl       SHORT G_M000_IG04
```

Overload delegate menjalankan loop yang sama, tetapi dengan `call [Action<int>.Invoke]` per elemen.

## Isi repositori

```
src/APL.Net/                 pustaka (paket "APL.Net", namespace AplNet)
src/APL.Net.SourceGen/       generator Roslyn, dikemas di dalam paket
tests/APL.Net.Tests/         suite xunit.v3 (JIT)
tests/APL.Net.AotTests/      pengecekan konsol, di-publish dengan PublishAot=true
benchmarks/APL.Net.Benchmarks/  BenchmarkDotNet B1–B7
benchmarks/rust-rayon/       referensi Rust/Rayon terpisah (tanpa FFI)
samples/APL.Net.Samples/     quick start konsol + demo pemrosesan gambar
samples/APL.Net.Gallery/     etalase Avalonia yang membandingkan APL.Net dan TPL
docs/en, docs/id             dokumentasi ini, dalam bahasa Inggris dan Indonesia
```
