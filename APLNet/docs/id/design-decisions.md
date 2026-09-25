# Keputusan desain

[English](../en/design-decisions.md) · [Indeks](README.md)

Ini dokumen terpenting bagi siapa pun yang mengubah APL.Net. Setiap keputusan di bawah sengaja
mengorbankan sesuatu. Keputusan yang dicatat tanpa alasannya cenderung "diperbaiki" oleh
kontributor yang berniat baik, dan perbaikan itu justru mengembalikan overhead yang ingin
dihilangkan pustaka ini.

## 1. Partisi statis sebagai default

**Keputusan.** Secara default, rentang dibagi sekali sebelum pekerjaan dimulai: satu potongan
berurutan per worker, dengan ukuran yang seimbang (selisih paling banyak satu item). Setiap worker
mengklaim satu partisi utuh dengan satu `Interlocked.Increment`. Tidak ada klaim per iterasi, dan
tidak ada klaim per chunk.

**Mengapa.** Partitioner bawaan `Parallel.For` membagikan chunk secara dinamis. Setiap chunk adalah
satu compare-and-swap pada penghitung yang dipakai bersama semua core, ditambah pembukuannya. Untuk
body yang hanya butuh satu-dua nanodetik, biaya itu mendominasi. Pembagian statis membayar biaya
penjadwalan sekali per panggilan.

**Harganya, diterima dan didokumentasikan.** Jika sebagian iterasi jauh lebih mahal dari yang lain,
worker pemiliknya selesai paling akhir dan worker lain menganggur. Benchmark B6 serta kasus
*Mandelbrot* dan *Zipf* di Galeri menunjukkannya secara terbuka: dengan partitioner default,
`Parallel.For` menang di sana. Pembagian statis juga lebih sensitif terhadap satu core yang lambat
(thread yang di-deschedule, atau saudara hyperthread yang sedang sibuk), karena tidak ada worker
lain yang bisa mengambil alih potongannya.

**Yang tidak boleh dilakukan.** Jangan jadikan partitioner default dinamis, dan jangan diam-diam
beralih ke klaim dinamis saat sebuah loop "tampak timpang". Itu mengembalikan biaya per chunk TPL
ke setiap loop, termasuk loop yang tidak membutuhkannya. Perbaikannya bersifat opt-in:
[`WorkStealingPartitioner`](partitioners.md) atau `StripedPartitioner`. Kontribusi untuk
penyeimbangan beban tempatnya di sana.

**Seimbang, bukan `ceil(n / dop)`.** Sketsa di spesifikasi memakai `chunk = ceil(n / dop)`. Dengan
9 item dan 8 worker, cara itu memberi lima worker dua item dan membiarkan tiga worker menganggur.
Pembagian seimbang memberi satu worker dua item dan tujuh worker lainnya masing-masing satu.
Biayanya sama, hasilnya selalu lebih baik.

## 2. `UnsafeQueueUserWorkItem`: ExecutionContext tidak mengalir

**Keputusan.** Worker diantrikan dengan `ThreadPool.UnsafeQueueUserWorkItem(IThreadPoolWorkItem, false)`.

**Mengapa.** `Task.Run` dan `Parallel.For` menangkap `ExecutionContext` pemanggil lalu
memulihkannya di setiap worker. Untuk loop numerik yang tidak pernah membaca `AsyncLocal<T>` atau
culture, itu biaya murni.

**Konsekuensinya.** Body yang berjalan di worker pool tidak melihat nilai `AsyncLocal<T>` milik
pemanggil, maupun culture/impersonation yang dialirkan. Iterasi yang dijalankan pemanggil sendiri
*tetap* melihatnya, sehingga perilakunya bergantung pada thread mana yang mendapat potongan mana.
Perilaku ini dipatok oleh sebuah test (`ExecutionContextDoesNotFlowToPoolWorkers`). Jika body
butuh state ambien, teruskan state itu sebagai field struct body. Wrapper async (`ForEachAsync`,
`ForAsync`) dibangun di atas TPL dan *tetap* mengalirkan context. Keduanya API untuk pekerjaan I/O,
di mana biaya dispatch tidak berpengaruh.

## 3. Job dari pool, bukan `CountdownEvent`

**Keputusan.** Spesifikasi membuat sketsa dengan `CountdownEvent` dan satu closure per chunk, lalu
menandai pooling sebagai tindak lanjut v1.1. Pooling dikerjakan sejak awal: setiap loop yang sedang
berjalan adalah satu `RangeJob<TBody>` dari pool, yang sekaligus menjadi `IThreadPoolWorkItem`,
dan digabung dengan `ManualResetEventSlim` milik job itu sendiri.

**Mengapa.** `CountdownEvent`, closure per chunk, dan `Task` masing-masing adalah alokasi di setiap
panggilan. Untuk banyak loop kecil (satu langkah simulasi, update per frame), alokasi itu beserta
pekerjaan GC yang ditimbulkannya lebih mahal daripada loop-nya sendiri. Hasil pengukuran ada di
[hasil benchmark](benchmark-results.md) (B7) dan di kasus *Many small loops* pada Galeri.

**Bagian yang halus: item yang terlambat.** Pemanggil tidak pernah menunggu item antrean diambil.
Ia hanya menunggu partisi yang sedang berjalan, dan mengklaim sendiri sisanya. Karena itu, sebuah
item bisa berjalan *setelah* loop-nya selesai. Reference count (satu per item antrean ditambah satu
untuk pemanggil) menjamin job baru kembali ke pool setelah pemegang terakhir melepasnya. Item yang
terlambat tidak menemukan partisi untuk diklaim, lalu melepas referensinya. **Jangan**
"menyederhanakan" ini dengan menunggu semua item antrean: di pool yang sibuk atau pada loop
bersarang, itu berubah menjadi deadlock.

## 4. Devirtualisasi diverifikasi, bukan diasumsikan

Klaim struct-invoker, bahwa tidak ada dispatch per elemen, bergantung pada JIT *meng-inline*
`Invoke`. Devirtualisasi saja tidak cukup. Hal ini diperiksa dengan membuang keluaran JIT
(`DOTNET_JitDisasm="InvokeRange"`, `DOTNET_TieredCompilation=0`) untuk
`RangeJob<ElementBody<Affine>>.InvokeRange`. Loop panas di dalamnya tidak berisi `call`:

```asm
       vmulsd   xmm2, xmm0, qword ptr [rax+8*r10+0x10]
       vaddsd   xmm2, xmm2, xmm1
       vmovsd   qword ptr [rcx+8*r10+0x10], xmm2
       inc      edx
       cmp      edx, r8d
       jl       SHORT G_M000_IG04
```

Keluaran yang sama untuk jalur delegate memperlihatkan `call [Action<int>.Invoke]` di dalam loop.
Untuk memeriksa ulang setelah mengubah executor:

```sh
DOTNET_TieredCompilation=0 DOTNET_JitDisasm="InvokeRange" \
  dotnet tests/APL.Net.AotTests/bin/Release/net10.0/APL.Net.AotTests.dll
```

Dua detail menjaga agar hal ini tetap berlaku:

- Job menyalin body ke variabel lokal sebelum loop. Dengan begitu JIT bisa menyimpan field-nya di
  register, dan body yang tidak readonly tidak disalin secara defensif di setiap panggilan.
- Adaptor (`ElementBody`, `ArrayItemBody`) adalah struct yang ditandai `AggressiveInlining`.

## 5. Pemanggil ikut bekerja

Thread pemanggil tidak hanya memblok, tetapi ikut mengambil bagian. Satu proses membangunkan thread
hilang dari jalur kritis. Selain itu, loop selalu maju meskipun semua thread pool sedang sibuk. Hal
inilah yang membuat panggilan `Apl.For` bersarang aman.

## 6. Span tidak pernah menyeberangi thread

`Span<T>` tidak bisa ditangkap closure atau disimpan di field. Karena itu, setiap API paralel
menerima `T[]`, `Memory<T>`, atau `ReadOnlyMemory<T>` di titik ketika pekerjaan diserahkan ke
thread lain. Data baru diubah menjadi span di dalam potongan milik satu worker. Spesifikasi sempat
mempertimbangkan pinning dan pointer mentah, tetapi lebih memilih jalur `Memory<T>`, dan APL.Net
mengikuti pilihan itu. Pointer mentah hanya ada di tingkat unsafe yang opt-in.

## 7. SIMD lewat struct operator, bukan delegate

`SimdOps` generik atas struct operator (`IUnaryOperator<T>`, `IBinaryOperator<T>`,
`IReduceOperator<T>`). Setiap operator menuliskan operasinya sekali untuk skalar dan sekali per
lebar vektor. Dua alternatif ditolak:

- **Default interface method** bisa menurunkan bentuk yang lebih lebar dari yang lebih sempit.
  Namun DIM yang dipanggil pada struct berjalan melalui salinan ber-boxing, dan justru biaya itulah
  yang ingin dihindari.
- **Delegate** (`Func<Vector256<T>, Vector256<T>>`) tetap disediakan sebagai kemudahan karena
  tercantum di spesifikasi. Biayanya satu panggilan per vektor, dan hanya mendukung vektor 256-bit.

Reduksi memakai empat akumulator vektor yang independen, karena tanpa itu latensi penjumlahan
floating-point membatasi sum hanya satu vektor per empat siklus. Akibatnya, hasil floating-point
sama dengan sum sekuensial hanya sampai batas pembulatan. Hasil bilangan bulat sama persis,
termasuk wrap-around saat overflow.

## 8. `stackalloc` dibatasi, atau tidak dipakai

Sketsa `SumParallelSimd` di spesifikasi memakai `dop <= 64 ? stackalloc : new`. APL.Net tidak
membutuhkan keduanya: hasil parsial setiap reduksi disimpan di array milik job yang berasal dari
pool, berjarak satu cache line agar worker tidak mengalami false sharing. Tidak ada buffer yang
dibuat per panggilan.

## 9. Exception dari tingkat unsafe juga diagregasi

Spesifikasi menggambarkan tingkat pointer sebagai fail-fast saat worker melempar exception. APL.Net
justru melewatkannya melalui executor yang sama, sehingga exception terkelola dari body berupa
function pointer tiba sebagai `AggregateException`, sama seperti di bagian lain. Blok `try` tidak
berbiaya sampai ada yang dilempar, jadi memilih menghentikan proses alih-alih melaporkan error
tidak memberi keuntungan kecepatan. Access violation tetap tidak bisa ditangkap; hal itu memang
tidak pernah bisa dipilih.

## 10. Penamaan

Nama produk dan paketnya **APL.Net**. Pedoman penamaan .NET menulis akronim tiga huruf atau lebih
dengan gaya Pascal, dan pemilik proyek meminta konvensi C# standar. Karena itu namespace-nya
`AplNet` dan titik masuknya `Apl`: `Apl.For(...)`, bukan `APL.For(...)`. Ada alasan praktis juga:
tipe bernama `APL` di dalam namespace `APL` akan membuat setiap `APL.For` di-resolve ke namespace
dan gagal di-compile.

## 11. Tambahan di luar spesifikasi, dan alasannya

| Tambahan | Alasan |
|---|---|
| `IRangeWorkBody` / `Apl.ForRange` | Bentuk tercepat: satu panggilan per potongan, span tanpa cek batas, ruang untuk SIMD |
| `Apl.Reduce` dengan `IReduceBody<T>` | Reduksi adalah hal yang paling sering salah dikerjakan dengan `Parallel.For` (lock, `Interlocked`) |
| `MinChunkSize` | Mencegah loop kecil dipecah menjadi potongan yang lebih murah daripada biaya membangunkan thread |
| `FixedParallelismProvider` | Membuat penjadwalan deterministik di test (NFR5) |
| `NativeBuffer<T>` | Memori native yang rata untuk tingkat pointer dan SIMD |
| `SimdCapabilities` | Memberi tahu pengguna lebar vektor yang benar-benar dipakai kernel |
| Aplikasi uji NativeAOT | Runner xunit bergantung pada reflection, jadi separuh AOT dari definition of done butuh binary tersendiri |
