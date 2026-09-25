# Memulai

[English](../en/getting-started.md) · [Indeks](README.md)

## Instalasi

```sh
dotnet add package APL.Net
```

APL.Net menargetkan **.NET 10**. Source generator sudah ada di dalam paket, jadi tidak perlu
memasang apa pun lagi. Nama namespace mengikuti pedoman penamaan .NET: produknya *APL.Net*,
namespace-nya `AplNet`, dan titik masuknya kelas statis `Apl`.

```csharp
using AplNet;           // Apl, AplOptions, [AplBody]
using AplNet.Core;      // IWorkBody, IRangeWorkBody, IReduceBody<T>
using AplNet.Simd;      // SimdOps dan struct operator
```

Langkah-langkah di bawah diurutkan dari yang paling sedikit usahanya. Setiap langkah lebih cepat
dari sebelumnya, dan semuanya opsional. Langkah 1 saja sudah menghapus sebagian besar biaya
penjadwalan `Parallel.For`.

## 1. Bentuk TPL: pengganti langsung `Parallel.For`

```csharp
// TPL
Parallel.For(0, n, i => result[i] = Compute(data[i]));

// APL.Net: bentuk panggilan sama, overhead di baliknya lebih kecil
Apl.For(0, n, i => result[i] = Compute(data[i]));
```

Delegate masih dipanggil untuk setiap elemen. Yang berubah adalah semua hal di sekitarnya:

- Rentang dibagi sama rata sejak awal, jadi worker tidak perlu bergantian mengambil chunk dari
  penghitung bersama.
- Pekerjaan diantrikan tanpa membuat `Task`.
- Worker tidak menangkap lalu memulihkan `ExecutionContext`.
- Thread pemanggil ikut mengerjakan satu bagian, alih-alih hanya menunggu.

> **Perbedaan perilaku:** `ExecutionContext` tidak mengalir ke worker di thread pool. Nilai
> `AsyncLocal<T>` dan culture pemanggil tidak terlihat di body yang berjalan di thread lain. Jika
> body Anda bergantung pada state ambien, teruskan state itu secara eksplisit. Lihat
> [keputusan desain](design-decisions.md#2-unsafequeueuserworkitem-executioncontext-tidak-mengalir).

## 2. Struct body: tanpa delegate sama sekali

```csharp
readonly struct ComputeBody(double[] data, double[] result) : IWorkBody
{
    public void Invoke(int i) => result[i] = Compute(data[i]);
}

Apl.For(0, n, new ComputeBody(data, result));
```

`Apl.For<TBody>` generik atas struct Anda. Untuk argumen tipe berupa struct, JIT meng-compile
salinan loop khusus untuk tipe itu. Dengan begitu JIT bisa meng-inline `Invoke`, sehingga hasilnya
sama dengan loop yang Anda tulis sendiri. Kode mesinnya ditampilkan di
[keputusan desain](design-decisions.md#4-devirtualisasi-diverifikasi-bukan-diasumsikan). Setelah
pool job terisi, satu panggilan tidak melakukan alokasi.

## 3. Range body: satu bagian utuh, tanpa pengecekan batas

```csharp
readonly struct ComputeRange(double[] data, double[] result) : IRangeWorkBody
{
    public void Invoke(int from, int to)
    {
        ReadOnlySpan<double> src = data.AsSpan(from, to - from);
        Span<double> dst = result.AsSpan(from, src.Length);
        for (int k = 0; k < src.Length; k++)
            dst[k] = Compute(src[k]);
    }
}

Apl.ForRange(0, n, new ComputeRange(data, result));
```

Body dipanggil sekali per bagian, bukan sekali per elemen. Karena `k` dibatasi oleh `src.Length`,
JIT bisa menghapus pengecekan batas. Anda juga bisa memindahkan persiapan ke luar loop, atau
menyerahkan bagian itu ke kernel SIMD.

## 4. Reduksi

```csharp
readonly struct SumOfSquares(double[] values) : IReduceBody<double>
{
    public double Accumulate(int from, int to, double acc)
    {
        foreach (double v in values.AsSpan(from, to - from))
            acc += v * v;
        return acc;
    }

    public double Combine(double a, double b) => a + b;
}

double total = Apl.Reduce(0, values.Length, 0.0, new SumOfSquares(values));
```

Setiap worker melipat bagiannya ke akumulator pribadinya. Akumulator-akumulator itu lalu
digabungkan menurut urutan partisi, tanpa lock dan tanpa `Interlocked`.

## 5. SIMD

```csharp
SimdOps.ParallelTransformInPlace(samples, new MultiplyAddOperator<float>(gain, offset));
double sum  = SimdOps.ParallelSum(values);
float  dot  = SimdOps.ParallelDot(x, y);
```

Di dalam bagian setiap worker, kernel memakai `Vector512`, `Vector256`, atau `Vector128`, mana pun
yang dipercepat CPU. Jika tidak ada, kernel beralih ke kode skalar. Lihat [SIMD](simd.md).

## 6. Beban tidak merata: aktifkan penyeimbangan

```csharp
var options = new AplOptions { Partitioner = new WorkStealingPartitioner(1) };
Apl.For(0, rows, new RenderRow(image), options);
```

Partisi statis menjadi default karena paling murah. Jika biaya tiap iterasi sangat berbeda-beda,
pilih [work stealing atau striping](partitioners.md).

## 7. Tingkat unsafe

```csharp
static unsafe void Scale(float* p, int from, int to)
{
    for (int i = from; i < to; i++) p[i] *= 0.5f;
}

using var buffer = new NativeBuffer<float>(n);                  // rata 64 byte, di luar heap GC
unsafe { UnsafeParallel.ForRangePtr(buffer.Pointer, n, &Scale); }
```

Tingkat ini tanpa pengecekan batas dan tanpa delegate. Pemanggil bertanggung jawab memastikan
memori tetap valid selama panggilan berlangsung. Lihat [tingkat unsafe](unsafe.md).

## 8. Atau biarkan generator yang menulis struct-nya

```csharp
[AplBody]
internal static void Compute(int i, double[] data, double[] result) => result[i] = Compute(data[i]);

Apl.For(0, n, AplGen.Compute(data, result));
```

Lihat [source generator](source-generator.md).

## Opsi

```csharp
var options = new AplOptions
{
    MaxDegreeOfParallelism = 4,               // default: Environment.ProcessorCount
    CancellationToken = token,                // diperiksa di antara blok
    CancellationCheckInterval = 4096,         // iterasi per blok jika token bisa dibatalkan
    MinChunkSize = 10_000,                    // jangan beri worker kurang dari ini
    Partitioner = new StripedPartitioner(64), // default: rentang statis
};
```

Buat satu objek `AplOptions` lalu pakai ulang. Opsi bersifat immutable dan aman dibagi antar thread.

## Menjalankan sampel

```sh
dotnet run --project samples/APL.Net.Samples -c Release        # quick start konsol + demo gambar
dotnet run --project samples/APL.Net.Gallery -c Release        # galeri Avalonia
```
