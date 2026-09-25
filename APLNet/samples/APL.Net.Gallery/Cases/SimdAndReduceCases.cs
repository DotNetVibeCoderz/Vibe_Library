// APL.Net - Another Parallel Library for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Runtime.CompilerServices;
using Avalonia.Media.Imaging;
using AplNet.Core;
using AplNet.Gallery.Infrastructure;
using AplNet.Simd;

namespace AplNet.Gallery.Cases;

// ------------------------------------------------------------------ AXPY

public sealed class AxpyCase : GalleryCase
{
    private const int N = 1_000_000;
    private const float A = 0.5f;
    private const float B = 1f;
    private float[] _data = [];

    public override string Id => "axpy";
    public override CaseCategory Category => CaseCategory.Simd;
    public override string TitleEn => "Scale and shift (AXPY)";
    public override string TitleId => "Skala dan geser (AXPY)";
    public override string SummaryEn => "x = x · a + b over a million floats, in place — the shape of every normalisation, gain and unit conversion.";
    public override string SummaryId => "x = x · a + b pada sejuta float, di tempat — bentuk dari setiap normalisasi, gain, dan konversi satuan.";
    public override string TakeawayEn => "The JIT does not vectorise loops on its own. SimdOps does it explicitly with the widest vectors the CPU accelerates, so a single thread already outruns Parallel.For on all cores; spreading the SIMD kernel across cores adds the rest until memory bandwidth becomes the limit.";
    public override string TakeawayId => "JIT tidak memvektorisasi loop dengan sendirinya. SimdOps melakukannya secara eksplisit dengan vektor terlebar yang dipercepat CPU, sehingga satu thread saja sudah mengalahkan Parallel.For di semua core; menyebar kernel SIMD ke semua core menambah sisanya sampai bandwidth memori menjadi batas.";
    public override string SizeEn => "1,000,000 floats (4 MB)";
    public override string SizeId => "1.000.000 float (4 MB)";

    public override string CodeApl => """
        // One thread, vectorised: Vector512 / Vector256 / Vector128, whichever
        // the CPU accelerates, with a scalar tail.
        SimdOps.TransformInPlace(data.AsSpan(), new MultiplyAddOperator<float>(a, b));

        // Every core, each running the same vector kernel on its slice:
        SimdOps.ParallelTransformInPlace(data, new MultiplyAddOperator<float>(a, b));
        """;

    public override string CodeTpl => """
        Parallel.For(0, data.Length, i => data[i] = data[i] * a + b);
        """;

    public override void Setup()
    {
        if (_data.Length == N)
            return;
        var random = new Random(1);
        _data = Enumerable.Range(0, N).Select(_ => random.NextSingle()).ToArray();
    }

    public override void Release() => _data = [];

    public override IReadOnlyList<Variant> CreateVariants()
    {
        float[] d = _data;
        var op = new MultiplyAddOperator<float>(A, B);
        return
        [
            new("for loop", VariantKind.Sequential, () => { for (int i = 0; i < d.Length; i++) d[i] = d[i] * A + B; }),
            new("Parallel.For", VariantKind.Tpl, () => Parallel.For(0, d.Length, i => d[i] = d[i] * A + B)),
            new("SimdOps (1 thread)", VariantKind.Apl, () => SimdOps.TransformInPlace(d.AsSpan(), op)),
            new("SimdOps.Parallel…", VariantKind.Apl, () => SimdOps.ParallelTransformInPlace(d, op)),
        ];
    }

    public override bool Verify()
    {
        float[] expected = _data.Select(v => v * A + B).ToArray();
        float[] actual = (float[])_data.Clone();
        SimdOps.ParallelTransformInPlace(actual, new MultiplyAddOperator<float>(A, B));
        return expected.AsSpan().SequenceEqual(actual);
    }
}

// ------------------------------------------------------------------ sum

public sealed class SumCase : GalleryCase
{
    private const int N = 1_000_000;
    private double[] _data = [];

    public override string Id => "sum";
    public override CaseCategory Category => CaseCategory.Reduce;
    public override string TitleEn => "Sum of an array";
    public override string TitleId => "Jumlah sebuah array";
    public override string SummaryEn => "The sum of a million doubles. The obvious parallel version needs a lock or thread-local subtotals; the obvious sequential one is a dependency chain the CPU cannot overlap.";
    public override string SummaryId => "Jumlah sejuta double. Versi paralel yang biasa butuh lock atau subtotal per thread; versi sekuensial yang biasa adalah rantai dependensi yang tidak bisa ditumpuk CPU.";
    public override string TakeawayEn => "SimdOps keeps four independent vector accumulators, so it adds 16 doubles per step instead of one and never waits on the previous add. ParallelSum gives each worker a private accumulator a cache line apart and combines them in a fixed order — no lock, no allocation.";
    public override string TakeawayId => "SimdOps menjaga empat akumulator vektor yang independen, sehingga menjumlahkan 16 double per langkah, bukan satu, dan tidak pernah menunggu penjumlahan sebelumnya. ParallelSum memberi setiap worker akumulator pribadi berjarak satu cache line dan menggabungkannya dalam urutan tetap — tanpa lock, tanpa alokasi.";
    public override string SizeEn => "1,000,000 doubles (8 MB)";
    public override string SizeId => "1.000.000 double (8 MB)";

    public override string CodeApl => """
        double total = SimdOps.ParallelSum(data);

        // Any monoid works the same way:
        double peak = SimdOps.ParallelMax(data);
        double energy = SimdOps.ParallelMapReduce(
            data, new SquareOperator<double>(), new AddOperator<double>());
        """;

    public override string CodeTpl => """
        double total = 0;
        object gate = new();
        Parallel.For(0, data.Length,
            () => 0.0,
            (i, _, subtotal) => subtotal + data[i],
            subtotal => { lock (gate) total += subtotal; });
        """;

    public override void Setup()
    {
        if (_data.Length == N)
            return;
        var random = new Random(2);
        _data = Enumerable.Range(0, N).Select(_ => random.NextDouble()).ToArray();
    }

    public override void Release() => _data = [];

    public override IReadOnlyList<Variant> CreateVariants()
    {
        double[] d = _data;
        double sink = 0;
        return
        [
            new("LINQ Sum()", VariantKind.Sequential, () => sink += d.Sum()),
            new("Parallel.For + subtotals", VariantKind.Tpl, () =>
            {
                double total = 0;
                object gate = new();
                Parallel.For(0, d.Length, () => 0.0, (i, _, local) => local + d[i], local => { lock (gate) total += local; });
                sink += total;
            }),
            new("SimdOps.Sum (1 thread)", VariantKind.Apl, () => sink += SimdOps.Sum<double>(d)),
            new("SimdOps.ParallelSum", VariantKind.Apl, () => sink += SimdOps.ParallelSum(d)),
        ];
    }

    public override bool Verify()
    {
        double expected = _data.Sum();
        return Math.Abs(expected - SimdOps.ParallelSum(_data)) < 1e-9 * Math.Abs(expected);
    }
}

// ------------------------------------------------------------------ dot product

public sealed class DotProductCase : GalleryCase
{
    private const int N = 1_000_000;
    private float[] _x = [];
    private float[] _y = [];

    public override string Id => "dot";
    public override CaseCategory Category => CaseCategory.Reduce;
    public override string TitleEn => "Dot product";
    public override string TitleId => "Hasil kali titik (dot product)";
    public override string SummaryEn => "Σ x[i]·y[i] over two million-element float vectors — the core of every similarity search, neural layer and least-squares fit.";
    public override string SummaryId => "Σ x[i]·y[i] atas dua vektor float sejuta elemen — inti dari setiap pencarian kemiripan, lapisan neural, dan regresi kuadrat terkecil.";
    public override string TakeawayEn => "A binary map-reduce: multiply lane by lane, add into vector accumulators, reduce at the end. On this machine a single SIMD thread is already close to what all cores achieve with scalar code.";
    public override string TakeawayId => "Map-reduce biner: kalikan per lane, jumlahkan ke akumulator vektor, reduksi di akhir. Di mesin ini satu thread SIMD sudah mendekati hasil semua core dengan kode skalar.";
    public override string SizeEn => "2 × 1,000,000 floats (8 MB)";
    public override string SizeId => "2 × 1.000.000 float (8 MB)";

    public override string CodeApl => """
        float dot = SimdOps.ParallelDot(x, y);

        // The general form - any map, any reduction:
        float dot2 = SimdOps.ParallelMapReduce(
            x, y, new MultiplyOperator<float>(), new AddOperator<float>());
        """;

    public override string CodeTpl => """
        float total = 0;
        object gate = new();
        Parallel.For(0, x.Length,
            () => 0f,
            (i, _, subtotal) => subtotal + x[i] * y[i],
            subtotal => { lock (gate) total += subtotal; });
        """;

    public override void Setup()
    {
        if (_x.Length == N)
            return;
        var random = new Random(3);
        _x = Enumerable.Range(0, N).Select(_ => random.NextSingle() - 0.5f).ToArray();
        _y = Enumerable.Range(0, N).Select(_ => random.NextSingle() - 0.5f).ToArray();
    }

    public override void Release() => (_x, _y) = ([], []);

    public override IReadOnlyList<Variant> CreateVariants()
    {
        float[] x = _x, y = _y;
        float sink = 0;
        return
        [
            new("for loop", VariantKind.Sequential, () =>
            {
                float total = 0;
                for (int i = 0; i < x.Length; i++)
                    total += x[i] * y[i];
                sink += total;
            }),
            new("Parallel.For + subtotals", VariantKind.Tpl, () =>
            {
                float total = 0;
                object gate = new();
                Parallel.For(0, x.Length, () => 0f, (i, _, local) => local + x[i] * y[i], local => { lock (gate) total += local; });
                sink += total;
            }),
            new("SimdOps.Dot (1 thread)", VariantKind.Apl, () => sink += SimdOps.Dot<float>(x, y)),
            new("SimdOps.ParallelDot", VariantKind.Apl, () => sink += SimdOps.ParallelDot(x, y)),
        ];
    }

    public override bool Verify()
    {
        double expected = 0;
        for (int i = 0; i < _x.Length; i++)
            expected += (double)_x[i] * _y[i];
        return Math.Abs(expected - SimdOps.ParallelDot(_x, _y)) < 1e-2;
    }
}

// ------------------------------------------------------------------ Monte Carlo pi

public sealed class MonteCarloPiCase : GalleryCase
{
    private const int Samples = 8_000_000;
    private const int Shown = 6_000;

    public override string Id => "montecarlo";
    public override CaseCategory Category => CaseCategory.Reduce;
    public override string TitleEn => "Monte Carlo π";
    public override string TitleId => "Monte Carlo π";
    public override string SummaryEn => "Throw 8 million points at a unit square and count those inside the quarter circle: π ≈ 4 · inside / total. Each point comes from a counter-based hash of its index, so every variant sees exactly the same points.";
    public override string SummaryId => "Lempar 8 juta titik ke persegi satuan dan hitung yang jatuh di dalam seperempat lingkaran: π ≈ 4 · di dalam / total. Setiap titik berasal dari hash berbasis counter dari indeksnya, sehingga semua varian melihat titik yang persis sama.";
    public override string TakeawayEn => "Apl.Reduce with a struct body: each worker folds its range into a private count and the counts are summed at the end. The counts are integers, so every variant returns the identical estimate — a reduction you can check exactly.";
    public override string TakeawayId => "Apl.Reduce dengan struct body: setiap worker melipat rentangnya ke hitungan pribadi lalu hitungan dijumlahkan di akhir. Hitungannya bilangan bulat, sehingga semua varian mengembalikan estimasi yang identik — reduksi yang bisa diperiksa dengan tepat.";
    public override string SizeEn => "8,000,000 random points";
    public override string SizeId => "8.000.000 titik acak";

    public override string CodeApl => """
        readonly struct CountInside : IReduceBody<long>
        {
            public long Accumulate(int from, int to, long inside)
            {
                for (int i = from; i < to; i++)
                    if (IsInside(i)) inside++;
                return inside;
            }

            public long Combine(long a, long b) => a + b;
        }

        long inside = Apl.Reduce(0, samples, 0L, new CountInside());
        double pi = 4.0 * inside / samples;
        """;

    public override string CodeTpl => """
        long inside = 0;
        Parallel.For(0, samples,
            () => 0L,
            (i, _, local) => IsInside(i) ? local + 1 : local,
            local => Interlocked.Add(ref inside, local));
        """;

    public override bool HasPreview => true;

    public override void Setup()
    {
    }

    public override IReadOnlyList<Variant> CreateVariants()
    {
        long sink = 0;
        return
        [
            new("for loop", VariantKind.Sequential, () =>
            {
                long inside = 0;
                for (int i = 0; i < Samples; i++)
                    if (Points.IsInside(i))
                        inside++;
                sink += inside;
            }),
            new("Parallel.For + locals", VariantKind.Tpl, () =>
            {
                long inside = 0;
                Parallel.For(0, Samples, () => 0L, (i, _, local) => Points.IsInside(i) ? local + 1 : local, local => Interlocked.Add(ref inside, local));
                sink += inside;
            }),
            new("Apl.Reduce (lambdas)", VariantKind.Apl, () => sink += Apl.Reduce(0, Samples, 0L, (from, to, acc) =>
            {
                for (int i = from; i < to; i++)
                    if (Points.IsInside(i))
                        acc++;
                return acc;
            }, (a, b) => a + b)),
            new("Apl.Reduce (struct)", VariantKind.Apl, () => sink += Apl.Reduce(0, Samples, 0L, new CountInside())),
        ];
    }

    public override bool Verify()
    {
        long expected = 0;
        for (int i = 0; i < Samples; i++)
            if (Points.IsInside(i))
                expected++;
        return expected == Apl.Reduce(0, Samples, 0L, new CountInside());
    }

    public override Bitmap? CreatePreview()
    {
        const int size = 520;
        var pixels = new int[size * size];
        Array.Fill(pixels, Bitmaps.Rgb(20, 26, 38));

        long inside = Apl.Reduce(0, Samples, 0L, new CountInside());
        for (int i = 0; i < Shown; i++)
        {
            var (x, y) = Points.At(i);
            int px = (int)(x * (size - 4)) + 2, py = size - 3 - (int)(y * (size - 4));
            int color = Points.IsInside(i) ? Bitmaps.Rgb(120, 140, 255) : Bitmaps.Rgb(230, 160, 80);
            pixels[py * size + px] = color;
            pixels[py * size + px + 1] = color;
            pixels[(py + 1) * size + px] = color;
            pixels[(py + 1) * size + px + 1] = color;
        }

        // The quarter circle itself.
        for (int a = 0; a < 2000; a++)
        {
            double t = a / 2000.0 * Math.PI / 2;
            int px = (int)(Math.Cos(t) * (size - 4)) + 2, py = size - 3 - (int)(Math.Sin(t) * (size - 4));
            pixels[py * size + px] = Bitmaps.Rgb(235, 238, 245);
        }

        _ = inside;
        return Bitmaps.FromBgra(pixels, size, size);
    }

    /// <summary>π from the same count the struct variant produces - shown under the preview.</summary>
    public static double Estimate() => 4.0 * Apl.Reduce(0, Samples, 0L, new CountInside()) / Samples;

    private readonly struct CountInside : IReduceBody<long>
    {
        public long Accumulate(int fromInclusive, int toExclusive, long accumulator)
        {
            for (int i = fromInclusive; i < toExclusive; i++)
                if (Points.IsInside(i))
                    accumulator++;
            return accumulator;
        }

        public long Combine(long left, long right) => left + right;
    }

    private static class Points
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static (double X, double Y) At(int index)
        {
            // SplitMix64: a counter-based generator, so point i is the same whichever thread draws it.
            ulong z = (ulong)index * 0x9E3779B97F4A7C15UL + 0x632BE59BD9B4E019UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            z ^= z >> 31;
            return ((z >> 40) * (1.0 / (1UL << 24)), (z & 0xFFFFFF) * (1.0 / (1UL << 24)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsInside(int index)
        {
            var (x, y) = At(index);
            return x * x + y * y <= 1.0;
        }
    }
}
