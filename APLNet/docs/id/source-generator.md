# Source generator

[English](../en/source-generator.md) · [Indeks](README.md)

API struct-invoker cepat, tetapi Anda harus menulis satu struct untuk setiap tempat pemanggilan.
Generator di dalam paket `APL.Net` menuliskan struct itu untuk Anda saat kompilasi. Tidak ada
reflection maupun pembangkitan kode saat runtime, sehingga generator ini juga berfungsi di
NativeAOT.

```csharp
using AplNet;

internal static class Kernels
{
    [AplBody]
    internal static void Scale(int i, double[] data, double factor) => data[i] *= factor;

    [AplRangeBody]
    internal static void Clear(int from, int to, int[] data) => Array.Clear(data, from, to - from);
}

Apl.For(0, data.Length, AplGen.Scale(data, 2.0));
Apl.ForRange(0, ints.Length, AplGen.Clear(ints));
```

## Yang dihasilkan

Untuk setiap method yang diberi atribut, generator menghasilkan:

- `AplNet.Generated.__<Tipe>_<Method>_Generated`: sebuah `readonly struct` yang mengimplementasikan
  `IWorkBody` (atau `IRangeWorkBody` untuk `[AplRangeBody]`). Struct ini punya satu field readonly
  per parameter tambahan, dan `Invoke` bertanda `AggressiveInlining` yang memanggil method Anda.
- `AplNet.AplGen.<Method>(...)`: factory yang menerima parameter tambahan dan mengembalikan struct.

Karena `Invoke` pada struct hanya meneruskan panggilan ke method Anda, JIT meng-inline method itu
ke dalam loop, sama seperti body yang ditulis tangan.

## Aturan dan diagnostik

| Id | Aturan |
|---|---|
| APL001 | Method harus `static` |
| APL002 | Harus mengembalikan `void` |
| APL003 | `[AplBody]`: parameter pertama harus `int` (indeks) |
| APL004 | `[AplRangeBody]`: dua parameter pertama harus `int, int` (rentang) |
| APL005 | Method dan semua tipe yang memuatnya tidak boleh generik |
| APL006 | Method dan semua tipe yang memuatnya harus `internal` atau `public` (struct hasil generator berada di luarnya) |
| APL007 | Parameter tambahan tidak boleh `ref`/`out`/`in`, `params`, atau ref struct seperti `Span<T>`. Gunakan array atau `Memory<T>` |
| APL008 | Dua body akan menghasilkan factory `AplGen` dengan nama dan tipe parameter yang sama |

Overload dengan parameter berbeda tetap boleh. Struct hasil generator-nya diberi nomor.

## Memakai dari source (bukan dari paket)

Referensi analyzer tidak menurun (transitif), jadi tambahkan secara eksplisit:

```xml
<ProjectReference Include="../../src/APL.Net.SourceGen/APL.Net.SourceGen.csproj"
                  OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
```

![Kasus source generator](../images/gallery-sourcegen.png)
