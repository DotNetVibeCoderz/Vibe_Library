# Panduan API

[English](../en/api-guide.md) · [Indeks](README.md)

## `Apl` (namespace `AplNet`)

| Method | Body | Catatan |
|---|---|---|
| `For(from, to, Action<int>)` | delegate | Pengganti langsung `Parallel.For` |
| `For<TBody>(from, to, TBody)` | `struct, IWorkBody` | Tanpa alokasi; `Invoke` di-inline |
| `ForRange(from, to, Action<int,int>)` | delegate | Satu panggilan per potongan |
| `ForRange<TBody>(from, to, TBody)` | `struct, IRangeWorkBody` | Bentuk tercepat |
| `ForEach<T>(T[], Action<T>)` | delegate | Per nilai, seperti `Parallel.ForEach` |
| `ForEach<T>(T[], RefItemAction<T>)` | delegate | `(int index, ref T item)`: ubah elemen di tempatnya |
| `ForEach<T,TBody>(T[] atau Memory<T>, TBody)` | `struct, IWorkBody<T>` | Via `ref`, loop di atas span |
| `Reduce<T>(from, to, identity, accumulate, combine)` | delegate | `accumulate(from, to, acc)` melipat satu potongan |
| `Reduce<T,TBody>(from, to, identity, TBody)` | `struct, IReduceBody<T>` | Reduksi tanpa alokasi |
| `For(from, to, Action<int, AplLoopState>)` | delegate | Bisa berhenti lebih awal: `state.Stop()` / `state.Break()`. Mengembalikan `AplLoopResult` |
| `ForWithState<TBody>(from, to, TBody)` | `struct, IStatefulWorkBody` | Sama, tanpa alokasi |
| `For(long, long, Action<long>)` | delegate | Rentang 64-bit, di atas 2³¹ iterasi |
| `For<TBody>(long, long, TBody)` | `struct, ILongWorkBody` | 64-bit, tanpa alokasi |
| `ForRange(long, long, Action<long,long>)` | delegate | Potongan rentang 64-bit |
| `ForRange<TBody>(long, long, TBody)` | `struct, ILongRangeWorkBody` | 64-bit, bentuk tercepat |
| `ForAsync`, `ForEachAsync` (`IEnumerable`, `IAsyncEnumerable`) | delegate async | Wrapper tipis atas TPL; *tetap* mengalirkan ExecutionContext |

Semua method menerima `AplOptions` opsional. `ParallelExecutor` (di `AplNet.Core`) menyediakan
bentuk yang khusus struct (`For`, `ForRange`, `Reduce`, `ForWithState`) bagi yang lebih suka nama
dari spesifikasi. [Referensi API](../api/README.md) memuat setiap overload beserta dokumentasi
lengkapnya.

## Interface body (namespace `AplNet.Core`)

```csharp
public interface IWorkBody        { void Invoke(int index); }
public interface IWorkBody<T>     { void Invoke(int index, ref T item); }
public interface IRangeWorkBody   { void Invoke(int fromInclusive, int toExclusive); }
public interface IReduceBody<T>
{
    T Accumulate(int fromInclusive, int toExclusive, T accumulator);
    T Combine(T left, T right);
}
public interface IStatefulWorkBody  { void Invoke(int index, AplLoopState state); }
public interface ILongWorkBody      { void Invoke(long index); }
public interface ILongRangeWorkBody { void Invoke(long fromInclusive, long toExclusive); }
```

Panduan:

- Tulis body sebagai `readonly struct` dan kirim dengan tipe konkretnya. Mengirimnya lewat variabel
  bertipe interface menyebabkan boxing dan spesialisasi JIT hilang.
- Setiap worker memanggil **salinannya sendiri** dari struct itu. Field referensi (array, objek)
  dipakai bersama; field nilai tidak.
- Range body menerima rentang yang tidak pernah tumpang tindih dan, jika digabung, mencakup seluruh
  loop tepat satu kali. Jika token bisa dibatalkan, setiap rentang paling panjang
  `CancellationCheckInterval`. Jika tidak, range body menerima partisi utuh.
- Untuk `Reduce`, `Combine` harus asosiatif dan `identity` harus menjadi identitasnya. Partisi
  digabung dalam urutan tetap, sehingga hasil floating-point bisa diulang selama derajat paralelisme
  dan partitioner-nya sama.

## `AplOptions`

| Properti | Default | Arti |
|---|---|---|
| `MaxDegreeOfParallelism` | `null` → provider (ProcessorCount) | Jumlah worker, termasuk pemanggil. `-1` juga berarti default |
| `CancellationToken` | tidak ada | Diperiksa sebelum mulai dan di antara blok |
| `CancellationCheckInterval` | 4096 | Panjang blok jika token bisa dibatalkan |
| `MinChunkSize` | loop: 1, SIMD: 32.768 | Paling banyak `ceil(n / MinChunkSize)` worker |
| `Partitioner` | `StaticRangePartitioner.Instance` | Lihat [partitioner](partitioners.md) |
| `ParallelismProvider` | `DefaultParallelismProvider` | Pakai `FixedParallelismProvider(n)` di test |
| `Workers` | `null` (thread pool) | Kelompok [`SpinningWorkers`](workers.md) sebagai pengganti thread pool |

`AplOptions.Default` dipakai bersama. Opsi bersifat immutable setelah dibuat, jadi buat sekali lalu
pakai ulang.

## Exception dan pembatalan (kompatibel dengan TPL)

- Setiap exception dari body dikumpulkan. Setelah semua worker yang berjalan berhenti, semuanya
  dilempar ulang sebagai **satu `AggregateException`**. Kegagalan membuat worker lain berhenti di
  batas partisi atau blok berikutnya. Loop yang cukup kecil untuk berjalan inline pun tetap
  membungkus exception-nya, seperti TPL.
- Token yang sudah dibatalkan saat loop dimulai melempar `OperationCanceledException` sebelum satu
  iterasi pun berjalan.
- Pembatalan di tengah jalan menghentikan worker di batas blok berikutnya, dan loop melempar
  `OperationCanceledException(token)`. Body yang melempar `OperationCanceledException` dengan token
  milik loop itu sendiri dianggap pembatalan, bukan kegagalan. `OperationCanceledException` lain
  dianggap kegagalan.
- Rentang `int` yang lebih panjang dari `Int32.MaxValue` iterasi melempar
  `ArgumentOutOfRangeException`; pakai overload `long`. Rentang kosong atau terbalik tidak
  menjalankan apa pun, dan `Reduce` mengembalikan identity.

## Menghentikan loop lebih awal: `AplLoopState`

```csharp
AplLoopResult result = Apl.For(0, n, (i, state) =>
{
    if (data[i] < 0)
        state.Break();          // semua iterasi di bawah i tetap dijalankan
});
int? negatifPertama = result.LowestBreakIteration;
```

`AplLoopState` mengikuti `ParallelLoopState`. `Stop()` mengakhiri loop secepat mungkin: tidak ada
iterasi yang dimulai setelah sebuah worker melihatnya. `Break()` menjamin setiap iterasi *di bawah*
iterasi yang memanggilnya tetap berjalan, dan boleh melewati yang di atasnya, sehingga
`LowestBreakIteration` adalah kecocokan pertama menurut urutan indeks. Satu loop memakai salah
satunya saja, tidak keduanya (`InvalidOperationException`, seperti di TPL).
`ShouldExitCurrentIteration` memungkinkan iterasi yang panjang memeriksa dan kembali lebih awal.
State diperiksa sebelum **setiap** iterasi dengan biaya satu load dan satu perbandingan. State
berupa struct dua field yang dikirim per nilai, jadi `ForWithState` dengan struct body tidak
mengalokasi apa pun.

## Rentang 64-bit

`For` dan `ForRange` juga menerima batas `long`, untuk loop di atas 2³¹ item (file memory-mapped
yang sangat besar, ruang indeks yang bukan array). Keduanya berjalan di mesin yang sama dengan loop
`int`. Jika jumlahnya muat di `int`, setiap indeks adalah satu unit. Di atas itu, setiap unit
rentang internal mewakili beberapa iterasi berurutan (`ceil(count / Int32.MaxValue)`), sehingga
range body bisa menerima potongan lebih dari 2³¹ iterasi, dan `MinChunkSize` serta
`CancellationCheckInterval` dibulatkan ke unit utuh. Rentang penuh
`[long.MinValue, long.MaxValue)` pun bisa dipakai.

## Parallelism provider

```csharp
public interface IParallelismProvider { int DegreeOfParallelism { get; } }
DefaultParallelismProvider.Instance      // Environment.ProcessorCount
new FixedParallelismProvider(3)          // deterministik, untuk test
```
