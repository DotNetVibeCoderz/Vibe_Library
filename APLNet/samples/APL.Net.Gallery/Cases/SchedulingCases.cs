// APL.Net - Another Parallel Library for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using Avalonia.Media.Imaging;
using AplNet.Core;
using AplNet.Core.Partitioners;
using AplNet.Gallery.Infrastructure;

namespace AplNet.Gallery.Cases;

// ------------------------------------------------------------------ Mandelbrot

public sealed class MandelbrotCase : GalleryCase
{
    private const int Width = 960;
    private const int Height = 640;
    private const int MaxIterations = 400;

    private static readonly AplOptions s_striped = new() { Partitioner = new StripedPartitioner(1) };
    private static readonly AplOptions s_stealing = new() { Partitioner = new WorkStealingPartitioner(1) };

    private int[] _iterations = [];

    public override string Id => "mandelbrot";
    public override CaseCategory Category => CaseCategory.Partitioning;
    public override string TitleEn => "Mandelbrot set";
    public override string TitleId => "Himpunan Mandelbrot";
    public override string SummaryEn => "A 960 × 640 Mandelbrot render, one row per iteration. Rows through the middle of the set run all 400 iterations per pixel; rows at the edges escape almost at once.";
    public override string SummaryId => "Render Mandelbrot 960 × 640, satu baris per iterasi. Baris di tengah himpunan menjalankan 400 iterasi penuh per piksel; baris di tepi hampir langsung lolos.";
    public override string TakeawayEn => "The default static split gives the middle band to a few workers and the rest go idle — watch their lanes end early. Striped dealing and work stealing are both opt-in and both fix it; this is exactly why APL.Net's default stays static and the fix is a partitioner you choose.";
    public override string TakeawayId => "Pembagian statis bawaan memberi pita tengah ke beberapa worker dan sisanya menganggur — lihat lajurnya berakhir lebih awal. Pembagian bergaris (striped) dan work stealing sama-sama opt-in dan sama-sama memperbaikinya; inilah alasan default APL.Net tetap statis dan perbaikannya adalah partitioner yang Anda pilih.";
    public override string SizeEn => "960 × 640 pixels, up to 400 iterations each";
    public override string SizeId => "960 × 640 piksel, hingga 400 iterasi masing-masing";

    public override string CodeApl => """
        readonly struct MandelbrotRow(int[] output) : IWorkBody
        {
            public void Invoke(int y) => RenderRow(output, y);
        }

        // Uneven rows: opt in to dynamic balancing.
        var options = new AplOptions { Partitioner = new WorkStealingPartitioner(1) };
        Apl.For(0, height, new MandelbrotRow(output), options);

        // Or interleave rows statically, still with no shared counter:
        Apl.For(0, height, new MandelbrotRow(output),
            new AplOptions { Partitioner = new StripedPartitioner(1) });
        """;

    public override string CodeTpl => """
        Parallel.For(0, height, y => RenderRow(output, y));
        """;

    public override bool HasPreview => true;

    public override string? HeadlineVariant => "Apl.For (work stealing)";

    public override void Setup()
    {
        if (_iterations.Length == 0)
            _iterations = new int[Width * Height];
    }

    public override void Release() => _iterations = [];

    public override IReadOnlyList<Variant> CreateVariants()
    {
        int[] o = _iterations;
        return
        [
            new("for loop", VariantKind.Sequential, () => { for (int y = 0; y < Height; y++) Render.Row(o, y); }),
            new("Parallel.For", VariantKind.Tpl, () => Parallel.For(0, Height, y => Render.Row(o, y))),
            new("Apl.For (static)", VariantKind.Apl, () => Apl.For(0, Height, new RowBody(o))),
            new("Apl.For (striped)", VariantKind.Apl, () => Apl.For(0, Height, new RowBody(o), s_striped)),
            new("Apl.For (work stealing)", VariantKind.Apl, () => Apl.For(0, Height, new RowBody(o), s_stealing)),
        ];
    }

    public override IReadOnlyList<LaneRun> CreateLaneRuns()
    {
        int[] o = _iterations;
        return
        [
            new("Parallel.For", VariantKind.Tpl, r => Parallel.For(0, Height, y => Traced(r, o, y))),
            new("Apl.For (static)", VariantKind.Apl, r => Apl.For(0, Height, y => Traced(r, o, y))),
            new("Apl.For (striped)", VariantKind.Apl, r => Apl.For(0, Height, y => Traced(r, o, y), s_striped)),
            new("Apl.For (work stealing)", VariantKind.Apl, r => Apl.For(0, Height, y => Traced(r, o, y), s_stealing)),
        ];

        static void Traced(LaneRecorder recorder, int[] output, int y)
        {
            long start = Stopwatch.GetTimestamp();
            Render.Row(output, y);
            recorder.Record(start, Stopwatch.GetTimestamp());
        }
    }

    public override bool Verify()
    {
        var expected = new int[_iterations.Length];
        for (int y = 0; y < Height; y++)
            Render.Row(expected, y);
        Array.Clear(_iterations);
        Apl.For(0, Height, new RowBody(_iterations), s_stealing);
        return expected.AsSpan().SequenceEqual(_iterations);
    }

    public override Bitmap? CreatePreview()
    {
        var pixels = new int[_iterations.Length];
        for (int i = 0; i < pixels.Length; i++)
        {
            int n = _iterations[i];
            if (n >= MaxIterations)
            {
                pixels[i] = Bitmaps.Rgb(16, 20, 32);
                continue;
            }

            double t = Math.Sqrt(n / (double)MaxIterations);
            pixels[i] = Bitmaps.Rgb((int)(45 + 210 * t), (int)(59 + 150 * t * t), (int)(219 - 120 * t));
        }

        return Bitmaps.FromBgra(pixels, Width, Height);
    }

    private readonly struct RowBody(int[] output) : IWorkBody
    {
        public void Invoke(int y) => Render.Row(output, y);
    }

    private static class Render
    {
        public static void Row(int[] output, int y)
        {
            Span<int> row = output.AsSpan(y * Width, Width);
            double ci = -1.1 + 2.2 * y / Height;
            for (int x = 0; x < row.Length; x++)
                row[x] = Escape(-2.25 + 3.3 * x / Width, ci);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int Escape(double cr, double ci)
        {
            double zr = 0, zi = 0;
            int n = 0;
            while (n < MaxIterations && zr * zr + zi * zi <= 4)
            {
                double t = zr * zr - zi * zi + cr;
                zi = 2 * zr * zi + ci;
                zr = t;
                n++;
            }

            return n;
        }
    }
}

// ------------------------------------------------------------------ Zipf workload

public sealed class ZipfWorkloadCase : GalleryCase
{
    private const int N = 10_000;

    private static readonly AplOptions s_striped = new() { Partitioner = new StripedPartitioner(16) };
    private static readonly AplOptions s_stealing = new() { Partitioner = new WorkStealingPartitioner(1) };

    private int[] _cost = [];
    private double[] _results = [];

    public override string Id => "zipf";
    public override CaseCategory Category => CaseCategory.Partitioning;
    public override string TitleEn => "Skewed workload (Zipf)";
    public override string TitleId => "Beban kerja timpang (Zipf)";
    public override string SummaryEn => "10,000 jobs whose cost follows Zipf's law, most expensive first: job 1 costs as much as the last 5,000 together. The benchmark the APL.Net spec keeps on purpose to show where static partitioning loses.";
    public override string SummaryId => "10.000 pekerjaan yang biayanya mengikuti hukum Zipf, termahal di depan: pekerjaan pertama sebanding dengan 5.000 terakhir digabung. Benchmark yang sengaja dipertahankan spesifikasi APL.Net untuk menunjukkan kapan partisi statis kalah.";
    public override string TakeawayEn => "Honest result: with the default static split one worker owns almost 80% of the work, and Parallel.For's dynamic chunking beats it. Opting into work stealing takes the lead back without making the default pay for balancing it rarely needs.";
    public override string TakeawayId => "Hasil jujur: dengan pembagian statis bawaan satu worker memegang hampir 80% pekerjaan, dan chunking dinamis Parallel.For mengalahkannya. Memilih work stealing merebut kembali keunggulan tanpa membuat default membayar penyeimbangan yang jarang dibutuhkan.";
    public override string SizeEn => "10,000 jobs, costs from ~2 ms down to ~0.2 µs";
    public override string SizeId => "10.000 pekerjaan, biaya dari ~2 ms hingga ~0,2 µs";

    public override string CodeApl => """
        // Static (the default): cheapest scheduling, blind to cost.
        Apl.For(0, jobs.Length, new RunJob(jobs, results));

        // Work stealing: idle workers take the back half of the
        // fullest remaining range - one CAS per steal.
        Apl.For(0, jobs.Length, new RunJob(jobs, results),
            new AplOptions { Partitioner = new WorkStealingPartitioner(1) });
        """;

    public override string CodeTpl => """
        Parallel.For(0, jobs.Length, i => results[i] = Run(jobs[i]));
        """;

    public override string? HeadlineVariant => "Apl.For (work stealing)";

    public override void Setup()
    {
        if (_cost.Length == N)
            return;
        _cost = Enumerable.Range(1, N).Select(rank => 1_500_000 / rank).ToArray();
        _results = new double[N];
    }

    public override void Release() => (_cost, _results) = ([], []);

    public override IReadOnlyList<Variant> CreateVariants()
    {
        int[] c = _cost;
        double[] r = _results;
        return
        [
            new("for loop", VariantKind.Sequential, () => { for (int i = 0; i < c.Length; i++) r[i] = Spin(c[i], i); }),
            new("Parallel.For", VariantKind.Tpl, () => Parallel.For(0, c.Length, i => r[i] = Spin(c[i], i))),
            new("Apl.For (static)", VariantKind.Apl, () => Apl.For(0, c.Length, new JobBody(c, r))),
            new("Apl.For (striped)", VariantKind.Apl, () => Apl.For(0, c.Length, new JobBody(c, r), s_striped)),
            new("Apl.For (work stealing)", VariantKind.Apl, () => Apl.For(0, c.Length, new JobBody(c, r), s_stealing)),
        ];
    }

    public override IReadOnlyList<LaneRun> CreateLaneRuns()
    {
        int[] c = _cost;
        double[] r = _results;
        return
        [
            new("Parallel.For", VariantKind.Tpl, rec => Parallel.For(0, c.Length, i => Traced(rec, c, r, i))),
            new("Apl.For (static)", VariantKind.Apl, rec => Apl.For(0, c.Length, i => Traced(rec, c, r, i))),
            new("Apl.For (work stealing)", VariantKind.Apl, rec => Apl.For(0, c.Length, i => Traced(rec, c, r, i), s_stealing)),
        ];

        static void Traced(LaneRecorder recorder, int[] cost, double[] results, int i)
        {
            long start = Stopwatch.GetTimestamp();
            results[i] = Spin(cost[i], i);
            recorder.Record(start, Stopwatch.GetTimestamp());
        }
    }

    public override bool Verify()
    {
        var expected = new double[N];
        for (int i = 0; i < N; i++)
            expected[i] = Spin(_cost[i], i);
        Array.Clear(_results);
        Apl.For(0, N, new JobBody(_cost, _results), s_stealing);
        return expected.AsSpan().SequenceEqual(_results);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static double Spin(int iterations, double x)
    {
        for (int i = 0; i < iterations; i++)
            x = x * 0.999999 + 1e-7;
        return x;
    }

    private readonly struct JobBody(int[] cost, double[] results) : IWorkBody
    {
        public void Invoke(int i) => results[i] = Spin(cost[i], i);
    }
}
