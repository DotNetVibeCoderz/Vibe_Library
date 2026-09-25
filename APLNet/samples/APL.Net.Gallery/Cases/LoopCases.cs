// APL.Net - Another Parallel Library for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Runtime.CompilerServices;
using AplNet.Core;
using AplNet.Gallery.Infrastructure;

namespace AplNet.Gallery.Cases;

// ------------------------------------------------------------------ trivial arithmetic

public sealed class TrivialArithmeticCase : GalleryCase
{
    private const int N = 250_000;
    private double[] _src = [];
    private double[] _dst = [];

    public override string Id => "trivial";
    public override CaseCategory Category => CaseCategory.Loops;
    public override string TitleEn => "Trivial arithmetic";
    public override string TitleId => "Aritmetika sederhana";
    public override string SummaryEn => "dst[i] = src[i] * 2 + 1 — about a nanosecond of work per element, so almost everything a loop costs beyond that is dispatch.";
    public override string SummaryId => "dst[i] = src[i] * 2 + 1 — sekitar satu nanodetik kerja per elemen, jadi hampir semua biaya loop di luar itu adalah dispatch.";
    public override string TakeawayEn => "Parallel.For pays a delegate call and a loop-state check on every element; the struct body pays neither. The data here fits in cache on purpose: at 1M+ elements every variant, sequential included, is limited by memory bandwidth and they converge (see docs/benchmark-results).";
    public override string TakeawayId => "Parallel.For membayar pemanggilan delegate dan pengecekan loop-state di setiap elemen; struct body tidak membayar keduanya. Data di sini sengaja muat di cache: pada 1M+ elemen semua varian, termasuk sekuensial, dibatasi bandwidth memori dan hasilnya menyatu (lihat docs/benchmark-results).";
    public override string SizeEn => "250,000 doubles (4 MB, fits in L3)";
    public override string SizeId => "250.000 double (4 MB, muat di L3)";

    public override string CodeApl => """
        // A struct body: the JIT compiles the loop for this exact type
        // and inlines Invoke - no delegate, no allocation.
        readonly struct Affine(double[] src, double[] dst) : IWorkBody
        {
            public void Invoke(int i) => dst[i] = src[i] * 2 + 1;
        }

        Apl.For(0, n, new Affine(src, dst));

        // Or keep the TPL shape and still skip the scheduler's overhead:
        Apl.For(0, n, i => dst[i] = src[i] * 2 + 1);
        """;

    public override string CodeTpl => """
        Parallel.For(0, n, i => dst[i] = src[i] * 2 + 1);
        """;

    public override void Setup()
    {
        if (_src.Length == N)
            return;
        _src = Enumerable.Range(0, N).Select(i => i * 0.25).ToArray();
        _dst = new double[N];
    }

    public override void Release() => (_src, _dst) = ([], []);

    public override IReadOnlyList<Variant> CreateVariants()
    {
        double[] s = _src, d = _dst;
        return
        [
            new("for loop", VariantKind.Sequential, () => { for (int i = 0; i < s.Length; i++) d[i] = s[i] * 2 + 1; }),
            new("Parallel.For", VariantKind.Tpl, () => Parallel.For(0, s.Length, i => d[i] = s[i] * 2 + 1)),
            new("Apl.For (lambda)", VariantKind.Apl, () => Apl.For(0, s.Length, i => d[i] = s[i] * 2 + 1)),
            new("Apl.For (struct)", VariantKind.Apl, () => Apl.For(0, s.Length, new AffineBody(s, d))),
        ];
    }

    public override bool Verify()
    {
        var expected = _src.Select(v => v * 2 + 1).ToArray();
        Array.Clear(_dst);
        Apl.For(0, _src.Length, new AffineBody(_src, _dst));
        return expected.AsSpan().SequenceEqual(_dst);
    }

    private readonly struct AffineBody(double[] src, double[] dst) : IWorkBody
    {
        public void Invoke(int i) => dst[i] = src[i] * 2 + 1;
    }
}

// ------------------------------------------------------------------ medium compute

public sealed class MediumComputeCase : GalleryCase
{
    private const int N = 1_000_000;
    private double[] _src = [];
    private double[] _dst = [];

    public override string Id => "medium";
    public override CaseCategory Category => CaseCategory.Loops;
    public override string TitleEn => "Medium compute";
    public override string TitleId => "Komputasi sedang";
    public override string SummaryEn => "dst[i] = √x · sin(x) — tens of nanoseconds per element, the band the APL.Net spec calls \"granular\".";
    public override string SummaryId => "dst[i] = √x · sin(x) — puluhan nanodetik per elemen, rentang yang disebut \"granular\" oleh spesifikasi APL.Net.";
    public override string TakeawayEn => "As the work per element grows, the per-element overhead APL.Net removes becomes a smaller share of the total, so the gap narrows. Both scale with cores; APL.Net still wins by what Parallel.For spends on dispatch.";
    public override string TakeawayId => "Semakin besar kerja per elemen, overhead per elemen yang dihapus APL.Net menjadi porsi yang lebih kecil, jadi selisihnya menyempit. Keduanya berskala dengan jumlah core; APL.Net tetap unggul sebesar biaya dispatch Parallel.For.";
    public override string SizeEn => "1,000,000 doubles";
    public override string SizeId => "1.000.000 double";

    public override string CodeApl => """
        readonly struct SqrtSin(double[] src, double[] dst) : IWorkBody
        {
            public void Invoke(int i) => dst[i] = Math.Sqrt(src[i]) * Math.Sin(src[i]);
        }

        Apl.For(0, n, new SqrtSin(src, dst));
        """;

    public override string CodeTpl => """
        Parallel.For(0, n, i => dst[i] = Math.Sqrt(src[i]) * Math.Sin(src[i]));
        """;

    public override void Setup()
    {
        if (_src.Length == N)
            return;
        _src = Enumerable.Range(0, N).Select(i => i * 0.001).ToArray();
        _dst = new double[N];
    }

    public override void Release() => (_src, _dst) = ([], []);

    public override IReadOnlyList<Variant> CreateVariants()
    {
        double[] s = _src, d = _dst;
        return
        [
            new("for loop", VariantKind.Sequential, () => { for (int i = 0; i < s.Length; i++) d[i] = Math.Sqrt(s[i]) * Math.Sin(s[i]); }),
            new("Parallel.For", VariantKind.Tpl, () => Parallel.For(0, s.Length, i => d[i] = Math.Sqrt(s[i]) * Math.Sin(s[i]))),
            new("Apl.For (lambda)", VariantKind.Apl, () => Apl.For(0, s.Length, i => d[i] = Math.Sqrt(s[i]) * Math.Sin(s[i]))),
            new("Apl.For (struct)", VariantKind.Apl, () => Apl.For(0, s.Length, new SqrtSinBody(s, d))),
        ];
    }

    public override bool Verify()
    {
        Array.Clear(_dst);
        Apl.For(0, _src.Length, new SqrtSinBody(_src, _dst));
        for (int i = 0; i < _src.Length; i++)
        {
            if (_dst[i] != Math.Sqrt(_src[i]) * Math.Sin(_src[i]))
                return false;
        }

        return true;
    }

    private readonly struct SqrtSinBody(double[] src, double[] dst) : IWorkBody
    {
        public void Invoke(int i) => dst[i] = Math.Sqrt(src[i]) * Math.Sin(src[i]);
    }
}

// ------------------------------------------------------------------ call overhead

public sealed class CallOverheadCase : GalleryCase
{
    private const int Calls = 200;
    private const int N = 2_000;
    private double[] _src = [];
    private double[] _dst = [];

    public override string Id => "overhead";
    public override CaseCategory Category => CaseCategory.Loops;
    public override string TitleEn => "Many small loops";
    public override string TitleId => "Banyak loop kecil";
    public override string SummaryEn => "200 parallel loops of 2,000 elements each, back to back — what a simulation step or a per-frame update looks like. The cost here is starting and joining loops, not running them.";
    public override string SummaryId => "200 loop paralel masing-masing 2.000 elemen, berurutan — seperti satu langkah simulasi atau update per frame. Biayanya di sini adalah memulai dan menggabungkan loop, bukan menjalankannya.";
    public override string TakeawayEn => "APL.Net reuses a pooled job per body type and queues it without a Task or a captured context, so a call costs a few microseconds and allocates nothing. Compare the allocation figures too. Loops this small can still lose to a plain for loop — parallelism has a floor.";
    public override string TakeawayId => "APL.Net memakai ulang job dari pool per tipe body dan mengantrikannya tanpa Task maupun context yang ditangkap, sehingga satu panggilan hanya beberapa mikrodetik dan tanpa alokasi. Bandingkan juga angka alokasinya. Loop sekecil ini tetap bisa kalah dari for biasa — paralelisme punya batas bawah.";
    public override string SizeEn => "200 calls × 2,000 doubles per run";
    public override string SizeId => "200 panggilan × 2.000 double per run";

    public override string CodeApl => """
        var body = new Affine(src, dst);    // a struct: nothing to allocate
        for (int step = 0; step < 200; step++)
            Apl.For(0, 2_000, body);        // pooled job, no Task, no closure
        """;

    public override string CodeTpl => """
        for (int step = 0; step < 200; step++)
            Parallel.For(0, 2_000, i => dst[i] = src[i] * 2 + 1);
        """;

    public override void Setup()
    {
        if (_src.Length == N)
            return;
        _src = Enumerable.Range(0, N).Select(i => (double)i).ToArray();
        _dst = new double[N];
    }

    public override void Release() => (_src, _dst) = ([], []);

    public override IReadOnlyList<Variant> CreateVariants()
    {
        double[] s = _src, d = _dst;
        return
        [
            new("for loop", VariantKind.Sequential, () =>
            {
                for (int c = 0; c < Calls; c++)
                    for (int i = 0; i < s.Length; i++)
                        d[i] = s[i] * 2 + 1;
            }),
            new("Parallel.For", VariantKind.Tpl, () =>
            {
                for (int c = 0; c < Calls; c++)
                    Parallel.For(0, s.Length, i => d[i] = s[i] * 2 + 1);
            }),
            new("Apl.For (lambda)", VariantKind.Apl, () =>
            {
                for (int c = 0; c < Calls; c++)
                    Apl.For(0, s.Length, i => d[i] = s[i] * 2 + 1);
            }),
            new("Apl.For (struct)", VariantKind.Apl, () =>
            {
                var body = new AffineBody(s, d);
                for (int c = 0; c < Calls; c++)
                    Apl.For(0, s.Length, body);
            }),
        ];
    }

    public override bool Verify()
    {
        Array.Clear(_dst);
        Apl.For(0, _src.Length, new AffineBody(_src, _dst));
        return _src.Select(v => v * 2 + 1).SequenceEqual(_dst);
    }

    private readonly struct AffineBody(double[] src, double[] dst) : IWorkBody
    {
        public void Invoke(int i) => dst[i] = src[i] * 2 + 1;
    }
}

// ------------------------------------------------------------------ ForEach by reference

public sealed class ParticlesCase : GalleryCase
{
    private const int N = 1_000_000;
    private Particle[] _particles = [];

    public override string Id => "particles";
    public override CaseCategory Category => CaseCategory.Loops;
    public override string TitleEn => "Particles, updated in place";
    public override string TitleId => "Partikel, diperbarui di tempat";
    public override string SummaryEn => "One physics step for a million particles stored as structs: gravity, drag, a bounce off the floor. ForEach hands each element to the body by reference, so it is updated where it lies.";
    public override string SummaryId => "Satu langkah fisika untuk sejuta partikel yang disimpan sebagai struct: gravitasi, hambatan, pantulan di lantai. ForEach memberikan setiap elemen ke body secara referensi, sehingga diperbarui di tempatnya.";
    public override string TakeawayEn => "Parallel.ForEach over an array of structs would hand each body a copy; the TPL version has to index back into the array. Apl.ForEach with IWorkBody<T> gets a ref to the element and loops over a span, so the bounds checks disappear as well.";
    public override string TakeawayId => "Parallel.ForEach atas array struct akan memberi body sebuah salinan; versi TPL harus mengindeks kembali ke array. Apl.ForEach dengan IWorkBody<T> mendapat ref ke elemen dan berjalan di atas span, sehingga pengecekan batas array juga hilang.";
    public override string SizeEn => "1,000,000 particles × 16 bytes";
    public override string SizeId => "1.000.000 partikel × 16 byte";

    public override string CodeApl => """
        struct Particle { public float X, Y, Vx, Vy; }

        readonly struct Step(float dt) : IWorkBody<Particle>
        {
            public void Invoke(int index, ref Particle p)
            {
                p.Vy -= 9.81f * dt;
                p.Vx *= 0.999f;
                p.X += p.Vx * dt;
                p.Y += p.Vy * dt;
                if (p.Y < 0) { p.Y = -p.Y; p.Vy = -p.Vy * 0.8f; }
            }
        }

        Apl.ForEach(particles, new Step(1f / 60));
        """;

    public override string CodeTpl => """
        Parallel.For(0, particles.Length, i =>
        {
            ref Particle p = ref particles[i];
            p.Vy -= 9.81f * dt;
            // ... same body
        });
        """;

    public override void Setup()
    {
        if (_particles.Length == N)
            return;
        var random = new Random(5);
        _particles = new Particle[N];
        for (int i = 0; i < N; i++)
            _particles[i] = new Particle { X = random.NextSingle() * 100, Y = random.NextSingle() * 100, Vx = random.NextSingle() - 0.5f, Vy = 0 };
    }

    public override void Release() => _particles = [];

    public override IReadOnlyList<Variant> CreateVariants()
    {
        Particle[] p = _particles;
        const float dt = 1f / 60;
        return
        [
            new("for loop", VariantKind.Sequential, () => { for (int i = 0; i < p.Length; i++) Particle.Step(ref p[i], dt); }),
            new("Parallel.For", VariantKind.Tpl, () => Parallel.For(0, p.Length, i => Particle.Step(ref p[i], dt))),
            new("Apl.ForEach (ref lambda)", VariantKind.Apl, () => Apl.ForEach(p, (int _, ref Particle q) => Particle.Step(ref q, dt))),
            new("Apl.ForEach (struct)", VariantKind.Apl, () => Apl.ForEach(p, new StepBody(dt))),
        ];
    }

    public override bool Verify()
    {
        Particle[] a = (Particle[])_particles.Clone();
        Particle[] b = (Particle[])_particles.Clone();
        for (int i = 0; i < a.Length; i++)
            Particle.Step(ref a[i], 1f / 60);
        Apl.ForEach(b, new StepBody(1f / 60));
        return a.AsSpan().SequenceEqual(b);
    }

    public struct Particle : IEquatable<Particle>
    {
        public float X, Y, Vx, Vy;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Step(ref Particle p, float dt)
        {
            p.Vy -= 9.81f * dt;
            p.Vx *= 0.999f;
            p.X += p.Vx * dt;
            p.Y += p.Vy * dt;
            if (p.Y < 0)
            {
                p.Y = -p.Y;
                p.Vy = -p.Vy * 0.8f;
            }
        }

        public readonly bool Equals(Particle other) => X == other.X && Y == other.Y && Vx == other.Vx && Vy == other.Vy;

        public override readonly bool Equals(object? obj) => obj is Particle other && Equals(other);

        public override readonly int GetHashCode() => HashCode.Combine(X, Y, Vx, Vy);
    }

    private readonly struct StepBody(float dt) : IWorkBody<Particle>
    {
        public void Invoke(int index, ref Particle item) => Particle.Step(ref item, dt);
    }
}
