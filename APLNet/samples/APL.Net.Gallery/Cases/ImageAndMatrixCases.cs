// APL.Net - Another Parallel Library for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using Avalonia.Media.Imaging;
using AplNet.Core;
using AplNet.Gallery.Infrastructure;
using AplNet.Simd;

namespace AplNet.Gallery.Cases;

// ------------------------------------------------------------------ image filter

public sealed class ImageFilterCase : GalleryCase
{
    private const int Width = 1920;
    private const int Height = 1080;
    private int[] _source = [];
    private int[] _target = [];

    public override string Id => "image";
    public override CaseCategory Category => CaseCategory.Loops;
    public override string TitleEn => "Image filter";
    public override string TitleId => "Filter gambar";
    public override string SummaryEn => "A sepia tone with a vignette over a full-HD frame: a colour matrix and a distance falloff per pixel, one row per iteration.";
    public override string SummaryId => "Nada sepia dengan vinyet pada frame full-HD: matriks warna dan peredupan berdasarkan jarak per piksel, satu baris per iterasi.";
    public override string TakeawayEn => "With a whole row per iteration, dispatch is already amortised and both libraries saturate the cores; the difference comes from the range body. ForRange hands a worker a contiguous run of rows, so it slices spans once and the per-pixel loop has no bounds checks.";
    public override string TakeawayId => "Dengan satu baris per iterasi, biaya dispatch sudah teramortisasi dan kedua pustaka memenuhi semua core; selisihnya datang dari range body. ForRange memberi worker sederet baris berurutan, sehingga span dibuat sekali dan loop per piksel tanpa pengecekan batas.";
    public override string SizeEn => "1920 × 1080 pixels (8 MB)";
    public override string SizeId => "1920 × 1080 piksel (8 MB)";

    public override string CodeApl => """
        readonly struct Sepia(int[] src, int[] dst, int width, int height) : IRangeWorkBody
        {
            public void Invoke(int fromRow, int toRow)
            {
                for (int y = fromRow; y < toRow; y++)
                {
                    ReadOnlySpan<int> input = src.AsSpan(y * width, width);
                    Span<int> output = dst.AsSpan(y * width, width);
                    for (int x = 0; x < input.Length; x++)
                        output[x] = Tone(input[x], Vignette(x, y));
                }
            }
        }

        Apl.ForRange(0, height, new Sepia(src, dst, width, height));
        """;

    public override string CodeTpl => """
        Parallel.For(0, height, y =>
        {
            for (int x = 0; x < width; x++)
                dst[y * width + x] = Tone(src[y * width + x], Vignette(x, y));
        });
        """;

    public override bool HasPreview => true;

    public override void Setup()
    {
        if (_source.Length == Width * Height)
            return;
        _source = new int[Width * Height];
        _target = new int[Width * Height];
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                double u = x / (double)Width, v = y / (double)Height;
                double ring = Math.Sin(Math.Sqrt((u - 0.62) * (u - 0.62) * 3 + (v - 0.45) * (v - 0.45) * 3) * 38);
                int r = (int)(40 + 180 * u + 30 * ring);
                int g = (int)(70 + 120 * v + 40 * ring);
                int b = (int)(200 - 120 * u + 50 * Math.Cos(v * 9));
                _source[y * Width + x] = Bitmaps.Rgb(r, g, b);
            }
        }
    }

    public override void Release() => (_source, _target) = ([], []);

    public override IReadOnlyList<Variant> CreateVariants()
    {
        int[] s = _source, d = _target;
        return
        [
            new("for loop", VariantKind.Sequential, () => { for (int y = 0; y < Height; y++) Filter.Row(s, d, y); }),
            new("Parallel.For (rows)", VariantKind.Tpl, () => Parallel.For(0, Height, y => Filter.Row(s, d, y))),
            new("Apl.For (rows)", VariantKind.Apl, () => Apl.For(0, Height, y => Filter.Row(s, d, y))),
            new("Apl.ForRange (struct)", VariantKind.Apl, () => Apl.ForRange(0, Height, new SepiaBody(s, d))),
        ];
    }

    public override bool Verify()
    {
        var expected = new int[_source.Length];
        for (int y = 0; y < Height; y++)
            Filter.Row(_source, expected, y);
        Array.Clear(_target);
        Apl.ForRange(0, Height, new SepiaBody(_source, _target));
        return expected.AsSpan().SequenceEqual(_target);
    }

    public override Bitmap? CreatePreview()
    {
        // Left half as it came in, right half filtered - the before and after in one frame.
        var composite = new int[_source.Length];
        for (int y = 0; y < Height; y++)
        {
            Array.Copy(_source, y * Width, composite, y * Width, Width / 2);
            Array.Copy(_target, y * Width + Width / 2, composite, y * Width + Width / 2, Width / 2);
            composite[y * Width + Width / 2] = Bitmaps.Rgb(255, 255, 255);
        }

        return Bitmaps.FromBgra(composite, Width, Height);
    }

    private static class Filter
    {
        public static void Row(int[] src, int[] dst, int y)
        {
            ReadOnlySpan<int> input = src.AsSpan(y * Width, Width);
            Span<int> output = dst.AsSpan(y * Width, Width);
            float dy = (y - Height * 0.5f) / Height;
            for (int x = 0; x < input.Length; x++)
            {
                float dx = (x - Width * 0.5f) / Width;
                output[x] = Tone(input[x], 1f - 1.1f * (dx * dx + dy * dy));
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int Tone(int argb, float light)
        {
            float r = (argb >> 16) & 0xFF, g = (argb >> 8) & 0xFF, b = argb & 0xFF;
            float tr = (0.393f * r + 0.769f * g + 0.189f * b) * light;
            float tg = (0.349f * r + 0.686f * g + 0.168f * b) * light;
            float tb = (0.272f * r + 0.534f * g + 0.131f * b) * light;
            return Bitmaps.Rgb((int)tr, (int)tg, (int)tb);
        }
    }

    private readonly struct SepiaBody(int[] src, int[] dst) : IRangeWorkBody
    {
        public void Invoke(int fromInclusive, int toExclusive)
        {
            for (int y = fromInclusive; y < toExclusive; y++)
                Filter.Row(src, dst, y);
        }
    }
}

// ------------------------------------------------------------------ matrix multiply

public sealed class MatrixMultiplyCase : GalleryCase
{
    private const int N = 384;
    private double[] _a = [];
    private double[] _b = [];
    private double[] _c = [];

    public override string Id => "matrix";
    public override CaseCategory Category => CaseCategory.Loops;
    public override string TitleEn => "Matrix multiply";
    public override string TitleId => "Perkalian matriks";
    public override string SummaryEn => "C = A × B for 384 × 384 doubles, one output row per iteration: 57 million multiply-adds, all of them in the innermost loop.";
    public override string SummaryId => "C = A × B untuk 384 × 384 double, satu baris hasil per iterasi: 57 juta perkalian-penjumlahan, semuanya di loop terdalam.";
    public override string TakeawayEn => "Rows are heavy, so dispatch hardly matters and Parallel.For and Apl.For finish close together. The jump comes from the last variant: the inner loop \"row of C += a·row of B\" handed to SimdOps with a custom operator, 4 doubles per instruction.";
    public override string TakeawayId => "Setiap baris berat, jadi dispatch hampir tidak berpengaruh dan Parallel.For serta Apl.For selesai berdekatan. Lompatan datang dari varian terakhir: loop dalam \"baris C += a·baris B\" diserahkan ke SimdOps dengan operator kustom, 4 double per instruksi.";
    public override string SizeEn => "384 × 384 doubles (three 1.2 MB matrices)";
    public override string SizeId => "384 × 384 double (tiga matriks 1,2 MB)";

    public override string CodeApl => """
        // Row i of C accumulates a[i,k] * (row k of B) - an AXPY, done in SIMD.
        readonly struct AddScaled(double a) : IBinaryOperator<double>
        {
            public double Invoke(double c, double b) => c + a * b;
            public Vector256<double> Invoke(Vector256<double> c, Vector256<double> b)
                => c + Vector256.Create(a) * b;
            // ... Vector128 / Vector512 overloads alike
        }

        Apl.For(0, n, i =>
        {
            Span<double> cRow = C.AsSpan(i * n, n);
            for (int k = 0; k < n; k++)
                SimdOps.Transform(cRow, B.AsSpan(k * n, n), cRow, new AddScaled(A[i * n + k]));
        });
        """;

    public override string CodeTpl => """
        Parallel.For(0, n, i =>
        {
            for (int k = 0; k < n; k++)
            {
                double a = A[i * n + k];
                for (int j = 0; j < n; j++)
                    C[i * n + j] += a * B[k * n + j];
            }
        });
        """;

    public override void Setup()
    {
        if (_a.Length == N * N)
            return;
        var random = new Random(9);
        _a = Enumerable.Range(0, N * N).Select(_ => random.NextDouble() - 0.5).ToArray();
        _b = Enumerable.Range(0, N * N).Select(_ => random.NextDouble() - 0.5).ToArray();
        _c = new double[N * N];
    }

    public override void Release() => (_a, _b, _c) = ([], [], []);

    public override IReadOnlyList<Variant> CreateVariants()
    {
        double[] a = _a, b = _b, c = _c;
        return
        [
            new("for loop", VariantKind.Sequential, () => { for (int i = 0; i < N; i++) Kernels.Row(a, b, c, i); }),
            new("Parallel.For (rows)", VariantKind.Tpl, () => Parallel.For(0, N, i => Kernels.Row(a, b, c, i))),
            new("Apl.For (rows)", VariantKind.Apl, () => Apl.For(0, N, new RowBody(a, b, c))),
            new("Apl.For + SimdOps", VariantKind.Apl, () => Apl.For(0, N, new SimdRowBody(a, b, c))),
        ];
    }

    public override bool Verify()
    {
        var expected = new double[N * N];
        for (int i = 0; i < N; i++)
            Kernels.Row(_a, _b, expected, i);
        Apl.For(0, N, new SimdRowBody(_a, _b, _c));
        for (int i = 0; i < expected.Length; i++)
        {
            if (Math.Abs(expected[i] - _c[i]) > 1e-9)
                return false;
        }

        return true;
    }

    private static class Kernels
    {
        public static void Row(double[] a, double[] b, double[] c, int i)
        {
            Span<double> cRow = c.AsSpan(i * N, N);
            cRow.Clear();
            for (int k = 0; k < N; k++)
            {
                double aik = a[i * N + k];
                ReadOnlySpan<double> bRow = b.AsSpan(k * N, N);
                for (int j = 0; j < cRow.Length; j++)
                    cRow[j] += aik * bRow[j];
            }
        }

        public static void SimdRow(double[] a, double[] b, double[] c, int i)
        {
            Span<double> cRow = c.AsSpan(i * N, N);
            cRow.Clear();
            for (int k = 0; k < N; k++)
                SimdOps.Transform(cRow, b.AsSpan(k * N, N), cRow, new AddScaled(a[i * N + k]));
        }
    }

    private readonly struct RowBody(double[] a, double[] b, double[] c) : IWorkBody
    {
        public void Invoke(int i) => Kernels.Row(a, b, c, i);
    }

    private readonly struct SimdRowBody(double[] a, double[] b, double[] c) : IWorkBody
    {
        public void Invoke(int i) => Kernels.SimdRow(a, b, c, i);
    }

    /// <summary><c>c + a * b</c> - a custom binary operator, as a user of SimdOps would write one.</summary>
    private readonly struct AddScaled(double a) : IBinaryOperator<double>
    {
        public double Invoke(double c, double b) => c + a * b;

        public Vector128<double> Invoke(Vector128<double> c, Vector128<double> b) => c + Vector128.Create(a) * b;

        public Vector256<double> Invoke(Vector256<double> c, Vector256<double> b) => c + Vector256.Create(a) * b;

        public Vector512<double> Invoke(Vector512<double> c, Vector512<double> b) => c + Vector512.Create(a) * b;
    }
}
