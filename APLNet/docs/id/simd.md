# SIMD (`AplNet.Simd`)

[English](../en/simd.md) · [Indeks](README.md)

JIT tidak memvektorisasi loop secara otomatis, jadi `SimdOps` melakukannya secara eksplisit. Di
dalam potongan setiap worker, ia memakai vektor terlebar yang dipercepat CPU:

| Jalur | Syarat | float per vektor |
|---|---|---|
| `Vector512` | `Vector512.IsHardwareAccelerated` (AVX-512) | 16 |
| `Vector256` | `Vector256.IsHardwareAccelerated` (AVX2) | 8 |
| `Vector128` | `Vector128.IsHardwareAccelerated` (SSE, Arm AdvSimd) | 4 |
| skalar | tidak satu pun di atas, atau tipe elemen yang tidak didukung tipe vektor | 1 |

Sisa setelah vektor penuh terakhir selalu diproses dengan bentuk skalar dari operator, sehingga
hasilnya tidak bergantung pada perangkat keras. Satu-satunya pengecualian adalah *reduksi*
floating-point, yang menjumlahkan dalam urutan berbeda. `SimdCapabilities.Describe()` memberi tahu
jalur mana yang aktif. Setiap kernel diuji di setiap jalur: CI menjalankan ulang suite dengan
`DOTNET_EnableAVX512F=0`, `DOTNET_EnableAVX2=0`, dan `DOTNET_EnableHWIntrinsic=0`.

## Operasi

| Satu thread (span) | Paralel (array / `Memory<T>`) |
|---|---|
| `TransformInPlace(span, op)` | `ParallelTransformInPlace(array, op)` |
| `Transform(source, destination, op)` | `ParallelTransform(source, destination, op)` |
| `Transform(x, y, destination, binaryOp)` | `ParallelTransform(x, y, destination, binaryOp)` |
| `TransformInPlace(span, Func<Vector256<T>,…>, Func<T,T>)` | `ParallelTransformInPlace(array, vecFunc, scalarFunc)` |
| `Reduce(source, reduceOp)` | `ParallelReduce(source, reduceOp)` |
| `MapReduce(source, map, reduce)` | `ParallelMapReduce(source, map, reduce)` |
| `MapReduce(x, y, binaryMap, reduce)` | `ParallelMapReduce(x, y, binaryMap, reduce)` |
| `Sum`, `SumOfSquares`, `Min`, `Max`, `Dot` | `ParallelSum`, `ParallelMin`, `ParallelMax`, `ParallelDot` |

Tipe elemen yang didukung adalah tipe numerik primitif yang didukung tipe vektor .NET: minimal
`float`, `double`, `int`, dan `long` (FR2), ditambah lebar bilangan bulat lainnya. Metode paralel
tidak pernah memberi satu worker kurang dari **32.768** elemen (`SimdOps.DefaultMinChunkSize`),
kecuali `AplOptions.MinChunkSize` menentukan lain. Loop vektor menyelesaikan sebanyak itu float
dalam beberapa mikrodetik, kira-kira sebesar biaya membangunkan satu thread pool.

Tujuan (destination) boleh berupa memori yang persis sama dengan sumbernya (transform di tempat),
tetapi tidak boleh tumpang tindih sebagian; kasus itu melempar `ArgumentException`. `Min`/`Max`
atas input kosong melempar `InvalidOperationException`, seperti LINQ. `Sum` dan `Reduce` atas
input kosong mengembalikan identity.

## Operator bawaan

| Unary (`IUnaryOperator<T>`) | Biner (`IBinaryOperator<T>`) | Reduksi (`IReduceOperator<T>`) |
|---|---|---|
| `IdentityOperator`, `NegateOperator`, `AbsOperator`, `SquareOperator`, `SqrtOperator`, `ScaleOperator(f)`, `AddScalarOperator(a)`, `MultiplyAddOperator(a, b)`, `ClampOperator(min, max)` | `AddOperator`, `SubtractOperator`, `MultiplyOperator`, `DivideOperator`, `MinOperator`, `MaxOperator` | `AddOperator` (jumlah), `MultiplyOperator` (hasil kali), `MinOperator`, `MaxOperator` |

`MultiplyAddOperator` menghitung perkalian lalu penjumlahan, bukan fused multiply-add, sehingga
hasilnya identik bit per bit dengan ekspresi skalarnya.

## Menulis operator

```csharp
/// c + a * b: langkah dalam perkalian matriks.
readonly struct AddScaled(double a) : IBinaryOperator<double>
{
    public double Invoke(double c, double b) => c + a * b;
    public Vector128<double> Invoke(Vector128<double> c, Vector128<double> b) => c + Vector128.Create(a) * b;
    public Vector256<double> Invoke(Vector256<double> c, Vector256<double> b) => c + Vector256.Create(a) * b;
    public Vector512<double> Invoke(Vector512<double> c, Vector512<double> b) => c + Vector512.Create(a) * b;
}

SimdOps.Transform(cRow, bRow, cRow, new AddScaled(aik));
```

Setiap bentuk ditulis sekali per lebar vektor. Interface-nya sengaja tanpa default method, karena
default interface method yang dipanggil pada struct berjalan pada salinan yang di-boxing. Operator
reduksi juga menyediakan `Identity` dan `Reduce(VectorN<T>)` horizontal.

## Bentuk delegate

`TransformInPlace(data, v => v * Vector256.Create(a) + Vector256.Create(b), x => x * a + b)`
sesuai dengan tanda tangan di spesifikasi dan praktis dipakai. Biayanya satu pemanggilan delegate
per vektor, dan fungsi skalar dipakai untuk semua elemen bila vektor 256-bit tidak dipercepat
perangkat keras. Untuk kode panas, utamakan struct operator.

## Mengapa reduksi cepat

`Sum` menjaga empat akumulator vektor yang independen. Tanpa itu, latensi penjumlahan (sekitar 4
siklus) membuat sum hanya bisa menyelesaikan satu vektor setiap empat siklus. Dengan empat
akumulator, penjumlahan saling tumpang tindih. Bentuk paralelnya memberi setiap worker akumulator
pribadi, berjarak satu cache line dari yang lain, lalu menggabungkannya dalam urutan tetap. Tanpa
lock, tanpa `Interlocked`.

![Jumlah sebuah array](../images/gallery-sum.png)
