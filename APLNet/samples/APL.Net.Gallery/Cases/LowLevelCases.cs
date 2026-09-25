// APL.Net - Another Parallel Library for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Runtime.CompilerServices;
using AplNet.Core;
using AplNet.Gallery.Infrastructure;
using AplNet.Unsafe;

namespace AplNet.Gallery.Cases;

// ------------------------------------------------------------------ unsafe pointers

public sealed unsafe class UnsafePointerCase : GalleryCase
{
    private const int N = 1_000_000;
    private float[] _managed = [];
    private NativeBuffer<float>? _native;

    public override string Id => "unsafe";
    public override CaseCategory Category => CaseCategory.LowLevel;
    public override string TitleEn => "Raw pointers and function pointers";
    public override string TitleId => "Pointer mentah dan function pointer";
    public override string SummaryEn => "A soft clip, x → clamp(1.5·x, −1, 1), over a million audio-style samples held in 64-byte-aligned native memory and driven by a static function pointer.";
    public override string SummaryId => "Soft clip, x → clamp(1,5·x, −1, 1), atas sejuta sampel bergaya audio di memori native yang rata 64 byte, dijalankan oleh function pointer statis.";
    public override string TakeawayEn => "UnsafeParallel is the \"you asked for it\" tier: no delegate, no bounds checks, memory the GC never scans or moves. Here it lands close to the safe struct body, which is the point — the safe API already removed most of the overhead. Reach for pointers when the data already lives in native memory.";
    public override string TakeawayId => "UnsafeParallel adalah tingkat \"Anda yang minta\": tanpa delegate, tanpa pengecekan batas, memori yang tidak pernah dipindai atau dipindah GC. Di sini hasilnya dekat dengan struct body yang aman, dan itulah intinya — API aman sudah menghapus sebagian besar overhead. Gunakan pointer bila data memang sudah berada di memori native.";
    public override string SizeEn => "1,000,000 floats (native, 64-byte aligned)";
    public override string SizeId => "1.000.000 float (native, rata 64 byte)";

    public override string CodeApl => """
        using var samples = new NativeBuffer<float>(n);   // aligned, off the GC heap

        static void SoftClip(float* data, int from, int to)
        {
            for (int i = from; i < to; i++)
                data[i] = Math.Clamp(data[i] * 1.5f, -1f, 1f);
        }

        UnsafeParallel.ForRangePtr(samples.Pointer, n, &SoftClip);
        """;

    public override string CodeTpl => """
        Parallel.For(0, samples.Length,
            i => samples[i] = Math.Clamp(samples[i] * 1.5f, -1f, 1f));
        """;

    public override void Setup()
    {
        if (_native is not null)
            return;
        var random = new Random(4);
        _managed = Enumerable.Range(0, N).Select(_ => random.NextSingle() * 2 - 1).ToArray();
        _native = new NativeBuffer<float>(N);
        _managed.CopyTo(_native.Span);
    }

    public override void Release()
    {
        _native?.Dispose();
        _native = null;
        _managed = [];
    }

    public override IReadOnlyList<Variant> CreateVariants()
    {
        float[] m = _managed;
        float* p = _native!.Pointer;
        return
        [
            new("for loop", VariantKind.Sequential, () => { for (int i = 0; i < m.Length; i++) m[i] = Math.Clamp(m[i] * 1.5f, -1f, 1f); }),
            new("Parallel.For", VariantKind.Tpl, () => Parallel.For(0, m.Length, i => m[i] = Math.Clamp(m[i] * 1.5f, -1f, 1f))),
            new("Apl.For (struct)", VariantKind.Apl, () => Apl.For(0, m.Length, new ClipBody(m))),
            new("UnsafeParallel.ForRangePtr", VariantKind.Apl, () => UnsafeParallel.ForRangePtr(p, N, &SoftClip)),
        ];
    }

    public override bool Verify()
    {
        using var copy = new NativeBuffer<float>(N);
        _managed.CopyTo(copy.Span);
        float[] expected = _managed.Select(v => Math.Clamp(v * 1.5f, -1f, 1f)).ToArray();
        UnsafeParallel.ForRangePtr(copy.Pointer, N, &SoftClip);
        return copy.Span.SequenceEqual(expected);
    }

    private static void SoftClip(float* data, int from, int to)
    {
        for (int i = from; i < to; i++)
            data[i] = Math.Clamp(data[i] * 1.5f, -1f, 1f);
    }

    private readonly struct ClipBody(float[] data) : IWorkBody
    {
        public void Invoke(int i) => data[i] = Math.Clamp(data[i] * 1.5f, -1f, 1f);
    }
}

// ------------------------------------------------------------------ source generator

public sealed class SourceGeneratorCase : GalleryCase
{
    private const int N = 500_000;
    private double[] _x = [];
    private double[] _y = [];

    public override string Id => "sourcegen";
    public override CaseCategory Category => CaseCategory.LowLevel;
    public override string TitleEn => "[AplBody] source generator";
    public override string TitleId => "Source generator [AplBody]";
    public override string SummaryEn => "Evaluates a degree-8 polynomial at half a million points. The body is an ordinary static method marked [AplBody]; the compiler writes the struct for it.";
    public override string SummaryId => "Mengevaluasi polinomial derajat 8 di setengah juta titik. Body-nya adalah method statis biasa bertanda [AplBody]; compiler yang menuliskan struct-nya.";
    public override string TakeawayEn => "The generated body and a hand-written struct are the same code, so they time the same — the generator buys the struct-invoker's speed with the ergonomics of a method. It runs at compile time, so it also works under NativeAOT, where runtime code generation is not available.";
    public override string TakeawayId => "Body hasil generator dan struct tulisan tangan adalah kode yang sama, jadi waktunya sama — generator memberi kecepatan struct-invoker dengan kemudahan sebuah method. Ia berjalan saat kompilasi, sehingga juga berfungsi di NativeAOT yang tidak mendukung pembangkitan kode saat runtime.";
    public override string SizeEn => "500,000 doubles, 8 multiply-adds each";
    public override string SizeId => "500.000 double, masing-masing 8 perkalian-penjumlahan";

    public override string CodeApl => """
        [AplBody]
        internal static void Polynomial(int i, double[] x, double[] y) =>
            y[i] = Horner(x[i]);

        // Generated at compile time: a readonly struct implementing IWorkBody,
        // plus this factory.
        Apl.For(0, n, AplGen.Polynomial(x, y));
        """;

    public override string CodeTpl => """
        Parallel.For(0, n, i => y[i] = Horner(x[i]));
        """;

    public override void Setup()
    {
        if (_x.Length == N)
            return;
        _x = Enumerable.Range(0, N).Select(i => i / (double)N - 0.5).ToArray();
        _y = new double[N];
    }

    public override void Release() => (_x, _y) = ([], []);

    public override IReadOnlyList<Variant> CreateVariants()
    {
        double[] x = _x, y = _y;
        return
        [
            new("for loop", VariantKind.Sequential, () => { for (int i = 0; i < x.Length; i++) y[i] = Kernels.Horner(x[i]); }),
            new("Parallel.For", VariantKind.Tpl, () => Parallel.For(0, x.Length, i => y[i] = Kernels.Horner(x[i]))),
            new("Apl.For (lambda)", VariantKind.Apl, () => Apl.For(0, x.Length, i => y[i] = Kernels.Horner(x[i]))),
            new("Apl.For (AplGen)", VariantKind.Apl, () => Apl.For(0, x.Length, AplGen.Polynomial(x, y))),
        ];
    }

    public override bool Verify()
    {
        Array.Clear(_y);
        Apl.For(0, _x.Length, AplGen.Polynomial(_x, _y));
        for (int i = 0; i < _x.Length; i++)
        {
            if (_y[i] != Kernels.Horner(_x[i]))
                return false;
        }

        return true;
    }
}

internal static class Kernels
{
    [AplBody]
    internal static void Polynomial(int i, double[] x, double[] y) => y[i] = Horner(x[i]);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static double Horner(double x) =>
        ((((((((0.5 * x - 1.2) * x + 0.7) * x - 0.3) * x + 2.1) * x - 1.7) * x + 0.9) * x - 0.4) * x) + 1.0;
}
