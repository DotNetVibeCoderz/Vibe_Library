# NativeAOT

[English](../en/native-aot.md) · [Indeks](README.md)

APL.Net dibangun dengan `IsAotCompatible` dan `IsTrimmable`. Jalur panasnya tidak memakai
reflection, `Reflection.Emit`, maupun `Activator.CreateInstance`. Struct body diselesaikan lewat
spesialisasi generik saat kompilasi, dan source generator berjalan di dalam compiler.

## Terverifikasi

`tests/APL.Net.AotTests` menguji setiap API publik terhadap referensi sekuensial: ketiga
partitioner, `For`/`ForRange`/`ForEach`/`Reduce`, body hasil generator, SIMD untuk
`float`/`double`/`int`/`long`, tingkat pointer, pembatalan, agregasi exception, dan `ForEachAsync`.
Aplikasi ini di-publish dengan `PublishAot=true`, dan warning trim/AOT diperlakukan sebagai error:

```sh
dotnet publish tests/APL.Net.AotTests -c Release -r win-x64 -o out/aot
out/aot/APL.Net.AotTests            # exit code 0 = semua pengecekan lulus
```

Di mesin referensi hasilnya executable 3,5 MB, nol warning trim atau AOT, dan semua pengecekan
lulus. CI mengulanginya di Linux.

## Lebar vektor di AOT

JIT memilih instruksi sesuai mesin tempat ia berjalan, sedangkan compiler AOT harus memilihnya saat
build. Secara default, NativeAOT .NET 10 menargetkan baseline x64 yang konservatif, sehingga
`SimdCapabilities` melaporkan **Vector128** bahkan di mesin AVX2. Agar memakai vektor yang lebih
lebar, beri tahu compiler CPU apa yang Anda targetkan:

```xml
<PropertyGroup>
  <!-- CPU era AVX2 (Haswell, 2013+ / Zen): Vector256 -->
  <IlcInstructionSet>x86-x64-v3</IlcInstructionSet>
  <!-- atau: native (mesin build), x86-x64-v4 (AVX-512) -->
</PropertyGroup>
```

Binary yang dibuat untuk `x86-x64-v3` tidak akan berjalan di CPU tanpa AVX2.

## Prasyarat Windows

NativeAOT melakukan linking dengan toolchain MSVC, jadi pasang workload "Desktop development with
C++". Jika publish gagal dengan `'vswhere.exe' is not recognized`, tambahkan
`C:\Program Files (x86)\Microsoft Visual Studio\Installer` ke `PATH`.

## Unit test di AOT?

Runner test xunit.v3 menemukan test lewat reflection, jadi suite xunit dijalankan di JIT. Separuh
AOT dari definition of done dicakup oleh aplikasi pengecekan di atas.
