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
| `ForAsync`, `ForEachAsync` (`IEnumerable`, `IAsyncEnumerable`) | delegate async | Wrapper tipis atas TPL; *tetap* mengalirkan ExecutionContext |

Semua method menerima `AplOptions` opsional. `ParallelExecutor` (di `AplNet.Core`) menyediakan
bentuk yang khusus struct (`For`, `ForRange`, `Reduce`) bagi yang lebih suka nama dari spesifikasi.

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
- Rentang yang lebih panjang dari `Int32.MaxValue` iterasi melempar `ArgumentOutOfRangeException`.
  Rentang kosong atau terbalik tidak menjalankan apa pun, dan `Reduce` mengembalikan identity.

## Parallelism provider

```csharp
public interface IParallelismProvider { int DegreeOfParallelism { get; } }
DefaultParallelismProvider.Instance      // Environment.ProcessorCount
new FixedParallelismProvider(3)          // deterministik, untuk test
```
