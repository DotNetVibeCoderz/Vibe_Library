# Spinning worker

[English](../en/workers.md) · [Indeks](README.md)

Secara default, setiap loop APL.Net berjalan di thread pool .NET. Mengantrikan pekerjaan di sana
murah, tetapi thread pool tertidur saat tidak ada pekerjaan, dan membangunkannya butuh beberapa
mikrodetik per loop. Loop yang berjalan beberapa milidetik tidak merasakannya. Loop yang hanya
beberapa mikrodetik tetapi dijalankan ribuan kali per detik (tick game atau simulasi, solver
iteratif, blok audio) membayarnya di setiap panggilan.

`SpinningWorkers` adalah jawabannya yang opt-in: sekumpulan thread khusus yang tetap berputar
(spinning) beberapa saat setelah setiap loop, sehingga loop berikutnya mendapati mereka sudah
terjaga.

```csharp
using AplNet;
using AplNet.Core;

using var workers = new SpinningWorkers();               // ProcessorCount - 1 thread
var options = new AplOptions { Workers = workers };

while (running)
{
    Apl.For(0, bodies.Length, new Integrate(bodies, dt), options);   // tidak ada thread yang dibangunkan
    Apl.For(0, bodies.Length, new Collide(bodies), options);
}
```

## Kompromi

| | Thread pool (default) | `SpinningWorkers` |
|---|---|---|
| Biaya memulai loop | Membangunkan thread pool (µs) | Menulis satu slot per worker (ns) |
| CPU di antara loop | Tidak ada | Setiap worker berputar selama `SpinDuration`, lalu tidur |
| Dipakai bersama bagian lain aplikasi | Ya | Tidak: thread khusus |
| ExecutionContext | Tidak mengalir | Tidak mengalir |

Worker yang berputar membuat satu core tetap sibuk. Untuk loop yang berjalan setiap beberapa
mikrodetik, itulah yang diinginkan. Untuk loop yang berjalan sekali per detik, itu membuang daya.
`SpinDuration` (default 1 ms) membatasi pemborosannya: setelah selama itu tanpa pekerjaan, worker
memblok dan tidak berbiaya sampai loop berikutnya membangunkannya. `Timeout.InfiniteTimeSpan`
tidak pernah memblok.

Hasil pengukuran ada di [hasil benchmark, B8](benchmark-results.md). Peningkatannya sedang, karena
jalur thread pool sudah murah, dan paling besar untuk loop berisi beberapa ribu sampai puluhan ribu
elemen.

## Konstruktor

```csharp
new SpinningWorkers(
    threadCount: -1,                  // -1: Environment.ProcessorCount - 1
    spinDuration: null,               // null: 1 ms; Timeout.InfiniteTimeSpan: berputar terus
    affinity: WorkerAffinity.None)    // None, Core, atau NumaNode
```

Pemanggil selalu ikut bekerja, jadi `ProcessorCount - 1` thread sudah cukup melayani loop dengan
derajat paralelisme default. Dispose kelompoknya untuk menghentikan thread-nya. Loop yang masih
merujuk kelompok yang sudah di-dispose berjalan di thread pool.

## Penempatan stabil

Di setiap loop, worker *k* lebih dulu mencoba mengklaim partisi *k + 1* (milik pemanggil adalah
partisi 0), baru kemudian membantu partisi lain. Dengan partitioner statis dan striped, potongan
data yang sama diproses oleh thread yang sama dari panggilan ke panggilan, dan tetap hangat di cache
core itu. Work stealing memindahkan pekerjaan antar-worker secara sengaja, jadi ia tetap memakai
cara klaimnya sendiri.

Penempatan bersifat upaya terbaik, tidak pernah menunggu: jika worker *k* terlambat, pemanggil atau
worker lain mengambil partisinya, persis seperti di thread pool. Loop tidak pernah memblok karena
satu worker tertentu.

## Pinning: `WorkerAffinity`

| Nilai | Efek |
|---|---|
| `None` (default) | OS menjadwalkan thread dengan bebas |
| `Core` | Worker *k* di-pin ke satu prosesor logis, berurutan, mulai setelah prosesor pertama |
| `NumaNode` | Worker *k* di-pin ke node NUMA milik prosesor itu, dan boleh memakai prosesor mana pun di node tersebut |

Pinning bekerja bersama penempatan stabil. Dengan `Core`, partisi *k + 1* selalu berjalan di core
yang sama. Dengan `NumaNode`, partisi yang berdekatan jatuh ke worker di node yang sama, sehingga
memori yang pertama kali disentuh sebuah node (Windows dan Linux menempatkan halaman memori di node
yang pertama kali menulisnya) adalah memori yang terus dikerjakannya. Itulah arti penempatan yang
sadar NUMA di sini. Alokasikan dan inisialisasi array besar dengan bentuk loop yang sama dengan yang
nanti dipakai untuk menghitung, agar sentuhan pertama sudah terjadi di node yang tepat.

`IsAffinityApplied` melaporkan apakah semua thread berhasil di-pin. Pinning bekerja di Windows
(lewat processor group, jadi juga di atas 64 prosesor) dan Linux (`sched_setaffinity`, mengikuti
himpunan CPU yang diizinkan untuk proses). macOS tidak punya API afinitas thread. Di sana worker
berjalan tanpa pin, dan `IsAffinityApplied` bernilai false.

## Topologi

```csharp
Console.WriteLine(AplTopology.Describe());        // "2 NUMA nodes: [0-15] [16-31]"
int nodes = AplTopology.NumaNodeCount;
IReadOnlyList<int> cpus = AplTopology.GetNodeProcessors(0);
```

Dibaca sekali saat pertama dipakai: dari API NUMA di Windows dan dari `/sys/devices/system/node` di
Linux. Di tempat lain, atau jika informasinya tidak tersedia, mesin dianggap satu node berisi
prosesor `0 .. ProcessorCount - 1`. Panggilan ini dan pinning adalah satu-satunya interop native di
APL.Net, dan hanya berjalan jika afinitas diminta ([keputusan desain §12](design-decisions.md)).

## Yang sudah diverifikasi

- Kebenaran dengan setiap partitioner, reduksi, loop bersarang, pemanggil bersamaan, kegagalan,
  pembatalan, dan worker yang tidur di antara setiap loop (`SpinningWorkersTests`).
- Tanpa alokasi per panggilan (`AllocationTests`).
- Penempatan stabil: di mesin yang tidak sedang sibuk, partisi tetap pada worker-nya di sedikitnya
  75% panggilan (`SpinningWorkerPlacementTests`). Dalam praktiknya hampir semuanya.
- Pinning berlaku di Windows dan Linux, termasuk Linux Arm64 di CI, dan di bawah NativeAOT.
- **Belum diverifikasi:** percepatan dari penempatan NUMA. Itu butuh mesin multi-socket, sedangkan
  mesin referensi hanya punya satu node. Logika pemetaannya diuji unit dengan topologi dua node
  sintetis.
