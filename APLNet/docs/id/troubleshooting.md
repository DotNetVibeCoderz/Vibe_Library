# Pemecahan masalah & FAQ

[English](../en/troubleshooting.md) · [Indeks](README.md)

## Kapan *tidak* memakai APL.Net

- **`n` kecil.** Membangunkan satu thread pool saja butuh beberapa mikrodetik. Loop yang selesai
  dalam satu-dua mikrodetik di satu thread tidak akan menjadi lebih cepat. Pakai `for` biasa, atau
  setel `MinChunkSize` agar APL.Net menjalankannya inline.
- **Beban timpang dengan partitioner default.** Jika biaya iterasi sangat bervariasi, pembagian
  statis kalah dari `Parallel.For`. Pilih `WorkStealingPartitioner`, atau pakai TPL saja.
- **Body yang terikat I/O.** Jika body menunggu disk atau jaringan, biaya dispatch tidak relevan.
  Pakai `Parallel.ForEachAsync` (atau `Apl.ForEachAsync` yang membungkusnya).
- **Body yang bergantung pada `AsyncLocal<T>`, culture, atau impersonation** dari pemanggil.
  ExecutionContext tidak mengalir, jadi teruskan state secara eksplisit atau pakai TPL.
- **Loop terikat memori atas array yang sangat besar.** Pada 1 juta elemen lebih dengan kerja
  sederhana, semua varian (termasuk sekuensial) mentok di bandwidth memori. Lihat B1 pada 100M di
  [hasil benchmark](benchmark-results.md). SIMD dan thread tidak bisa mengalahkan DRAM.

## Hal yang sering mengejutkan

**Loop saya lebih lambat dengan `Apl.For` daripada `Parallel.For`.**
Kemungkinan bebannya timpang, atau mesin sedang sibuk. Dengan partisi statis, satu core yang
lambat atau di-deschedule menahan seluruh loop. Lihat lajurnya (di Galeri atau profiler), lalu coba
`StripedPartitioner` atau `WorkStealingPartitioner`.

**Nilai `AsyncLocal` bernilai null di dalam body.**
Itu memang disengaja. Lihat [keputusan desain §2](design-decisions.md#2-unsafequeueuserworkitem-executioncontext-tidak-mengalir).

**Jumlah floating-point sedikit berbeda dari loop `for`.**
Reduksi vektor dan paralel menjumlahkan dalam urutan berbeda. Hasilnya bisa diulang selama derajat
paralelisme dan partitioner-nya sama. Jumlah bilangan bulat selalu sama persis.

**`ArgumentException: Destination partially overlaps the source`.**
Tujuan sebuah transform harus persis memori sumber (transform di tempat), atau memori yang
benar-benar terpisah.

**`InvalidOperationException` dari partitioner saya.**
`GetPartitionCount` mengembalikan nilai kurang dari 1.

**Struct body saya sepertinya tidak di-inline.**
Kirim dengan tipe konkretnya (`Apl.For(0, n, new MyBody(...))`), jangan lewat variabel bertipe
`IWorkBody`. Jaga `Invoke` tetap kecil, atau tandai dengan
`[MethodImpl(MethodImplOptions.AggressiveInlining)]`. Periksa dengan `DOTNET_JitDisasm` seperti di
[keputusan desain §4](design-decisions.md#4-devirtualisasi-diverifikasi-bukan-diasumsikan).

## Pengembangan

**`dotnet test` gagal dengan "Testing with VSTest target is no longer supported".**
`global.json` di repositori sudah mengarahkan `dotnet test` ke Microsoft.Testing.Platform.
Jalankan dari root repositori, atau jalankan proyek test secara langsung:
`dotnet run --project tests/APL.Net.Tests -c Release`.

**Build gagal dengan MSB3021 (file terkunci).**
Galeri atau benchmark masih berjalan. Tutup dulu.

**Publish NativeAOT gagal dengan `'vswhere.exe' is not recognized`.**
Lihat [NativeAOT](native-aot.md#prasyarat-windows).
