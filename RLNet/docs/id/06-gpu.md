# GPU (opsional)

[← Daftar isi](README.md) · [English](../en/06-gpu.md)

```bash
dotnet add package RLNet.Gpu
```

```csharp
using RLNet.Gpu;

using var backend = GpuComputeBackend.TryCreate();
var agent = new DqnAgent(obs, act, backend: backend);
```

## Baca ini dulu

**GPU tidak otomatis lebih cepat di sini, dan untuk sebagian besar yang dilakukan RLNet justru lebih
lambat.**

Jaringan classic control adalah dua atau tiga layer berisi 64 sampai 256 unit. Satu forward pass
atas batch 256 hanya beberapa ratus mikrodetik aritmetika, sementara perjalanan bolak-balik untuk
memindahkan batch melintasi bus dan mengembalikan hasilnya sendiri memakan puluhan mikrodetik — dan
tidak mengecil seiring mengecilnya jaringan. Pada ukuran itu
[`CpuComputeBackend`](05-neural-network.md) umumnya menang telak.

**Di mana CPU berhenti unggul sepenuhnya bergantung pada perangkatnya**, jadi tidak ada satu angka
titik impas yang bisa disebutkan. Berikut hasil ukur `GpuBenchmarks` di mesin pengembangan — satu
forward pass dense pada batch 256, di Intel UHD 620 *terintegrasi* lewat OpenCL:

| Lebar tersembunyi | CPU SIMD | GPU (Intel UHD 620) | GPU |
|---:|---:|---:|---|
| 64 | 338 µs | 2.802 µs | **8,3× lebih lambat** |
| 256 | 3.932 µs | 8.424 µs | **2,2× lebih lambat** |
| 1.024 | 75.070 µs | 112.921 µs | **1,5× lebih lambat** |

GPU-nya tidak pernah menang di perangkat keras ini — tapi perhatikan jaraknya menyempit mantap
seiring melebarnya jaringan (8,3× → 2,2× → 1,5×), yang persis merupakan biaya transfer yang
teramortisasi atas aritmetika yang lebih banyak. GPU terintegrasi berbagi bandwidth memori dengan CPU
yang seharusnya ia kalahkan, jadi ini mendekati kasus terburuk; kartu diskret dengan memorinya
sendiri akan mencapai titik impas jauh lebih awal. Bentuk trennya itulah yang bisa dipindahkan, bukan
angkanya.

Di mana ia menguntungkan:

- Jaringan lebar, pada GPU diskret — tren di atas mengarah ke kemenangan, perangkat keras ini saja
  yang tidak pernah mencapainya
- Pembaruan offline dengan batch besar
- Observasi menyerupai citra, kalau Anda memperluas library sejauh itu
- Sapuan yang melatih banyak agen sekaligus

**Ukur di perangkat keras Anda.** `GpuBenchmarks` diparameterkan atas lebar tersembunyi (64, 256,
1024 pada batch 256) justru supaya titik impasnya bisa dibaca, bukan ditebak:

```bash
dotnet run -c Release --project benchmarks/RLNet.Benchmarks -- --filter '*GpuBenchmarks*'
```

## Cara ia memutuskan

Setiap panggilan membawa perkiraan beban kerja, dan apa pun di bawah `MinimumWorkPerCall` berjalan
di jalur CPU alih-alih membayar biaya transfer:

```csharp
if ((long)batch * inputSize * outputSize < MinimumWorkPerCall)
{
    _fallback.DenseForward(...);   // CPU
    return;
}
```

Ambangnya default 2²⁰ multiply-accumulate dan sengaja dibuat tinggi. Ini paling penting untuk
**pemilihan aksi**, yaitu satu forward pass observasi tunggal pada setiap langkah simulasi:
mengirimkannya ke perangkat akan membuat memilih aksi lebih lambat daripada seluruh sisa loop
training.

Turunkan ke 0 untuk memaksa semuanya ke perangkat (itulah yang dilakukan benchmark, supaya kasus
kecil mengukur GPU alih-alih mengukur CPU dua kali).

## Kembali ke CPU

Mesin tanpa GPU adalah kasus yang lumrah, dan itu seharusnya berarti "jalankan di CPU", bukan
"gagal saat start":

```csharp
using var backend = GpuComputeBackend.TryCreate();   // tidak pernah melempar exception
Console.WriteLine(backend.Name);
// "GPU (Cuda: NVIDIA GeForce RTX 4060)"  atau  "CPU SIMD (256-bit vectors)"
```

`new GpuComputeBackend()` melempar `NotSupportedException` ketika tidak ada akselerator. Pakai itu
hanya kalau GPU adalah syarat mutlak dan Anda ingin gagal dengan lantang.

Untuk melihat apa yang tersedia tanpa memulai run:

```bash
dotnet run -c Release --project benchmarks/RLNet.Benchmarks -- --devices
```

## Apa yang berjalan di perangkat

Tepat tiga perkalian matriks tempat sebuah layer dense terurai, ditambah aktivasinya:

| Kernel | Thread | Yang dihitung |
|---|---|---|
| `ForwardKernel` | batch × outputSize | `activation(x · W + b)` |
| `GradInputKernel` | batch × inputSize | `dL/dx = gradOut · Wᵀ` |
| `GradWeightKernel` | inputSize × outputSize | `dL/dW += xᵀ · gradOut` |
| `GradBiasKernel` | outputSize | `dL/db += Σ gradOut` |
| `ActivationBackwardKernel` | batch × outputSize | melipat turunan nonlinearitas ke dalamnya |

`GradWeightKernel` memberi setiap thread **satu weight** dan menjumlahkan sendiri atas batch,
sehingga tidak pernah ada dua thread menulis elemen yang sama dan kernelnya tidak butuh atomik —
itulah yang membuatnya menskala.

Nonlinearitasnya bagian dari kontrak backend alih-alih diterapkan belakangan oleh pemanggil, karena
backend perangkat ingin meleburnya ke kernel yang sama; menjadikannya langkah terpisah akan memaksa
satu perjalanan bolak-balik per layer dan mengembalikan sebagian besar keuntungan akselerator.

## ILGPU

ILGPU sepenuhnya managed: ia mengompilasi kernel ke PTX (CUDA) atau OpenCL C **saat runtime**, dari
sumber C# yang sama. Jadi `RLNet.Gpu` tetap lintas platform dan tidak mengirim binary native
tambahan selain driver yang sudah ada di mesin.

Kernel harus tetap berada di subset yang bisa dikompilasi ILGPU — tanpa alokasi, tanpa exception,
tanpa closure, tanpa `MathF` (pakai `XMath` dari `ILGPU.Algorithms`).

Buffer perangkat di-cache dan ditumbuhkan alih-alih dialokasikan per panggilan. Alokasi pada
akselerator adalah operasi yang menyinkronkan, dan melakukannya sekali per gradient step akan lebih
mahal daripada penghematan kernelnya. Bentuk datanya berulang sepanjang satu run training, jadi
setelah beberapa panggilan pertama ini berhenti mengalokasikan sama sekali.

## Kebenaran

Implementasi kedua dari aritmetika yang sama adalah tempat kedua ia bisa salah, dan kernel GPU yang
salahnya halus menghasilkan agen yang terlatih sedikit lebih buruk — tidak terlihat tanpa
perbandingan.

`GpuBackendTests` menjalankan input identik lewat kedua backend dan menuntut kesepakatan sampai 1e-3
relatif:

```csharp
CpuComputeBackend.Instance.DenseForward(weights, biases, input, cpuOutput, ...);
gpu.DenseForward(weights, biases, input, gpuOutput, ...);
AssertClose(cpuOutput, gpuOutput);
```

Kesamaan persis mustahil dicapai — urutan penjumlahan float berbeda antara loop CPU serial dan
kernel paralel — tapi toleransinya cukup ketat sehingga indeks yang tertukar atau suku yang hilang
akan meleset beberapa orde besaran.

Tesnya **melewati dirinya sendiri** ketika tidak ada akselerator, yang merupakan kasus normal pada
runner CI. Masing-masing memastikan lebih dulu bahwa ia benar-benar mendapat akselerator, jadi tes
yang dilewati itu jujur, bukan tes yang diam-diam lulus karena menjalankan jalur CPU dua kali.

## Batasan

- Hanya `float`. Tanpa presisi campuran, tanpa tensor core.
- Satu akselerator. Tanpa multi-GPU.
- Hanya layer dense — cakupannya sama dengan backend CPU.
- Tanpa CUDA graph capture atau tumpang tindih stream; setiap panggilan menyinkronkan.

Yang terakhir adalah langit-langit dari apa yang bisa dicapai saat ini. Versi yang menyalurkan
peluncuran kernel secara pipeline akan lebih baik pada jaringan besar; itu belum ada.

## Selanjutnya

- [Mesin neural](05-neural-network.md) — apa yang ditopang backend ini
- [Benchmark](09-benchmark.md) — di mana titik impasnya sebenarnya
