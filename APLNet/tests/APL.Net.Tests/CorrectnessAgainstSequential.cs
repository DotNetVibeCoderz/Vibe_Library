// APL.Net - Another Parallel Library for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using AplNet.Core;
using AplNet.Core.Partitioners;

namespace AplNet.Tests;

/// <summary>
/// Property-style checks: for randomized sizes, degrees of parallelism and partitioners, every parallel
/// API must produce exactly what a sequential loop produces. Seeds are fixed so a failure reproduces.
/// </summary>
public class CorrectnessAgainstSequential
{
    private const int Cases = 60;

    private static IEnumerable<(int Seed, int Length, int Dop, IPartitioner Partitioner)> RandomCases()
    {
        for (int seed = 0; seed < Cases; seed++)
        {
            var random = new Random(seed);
            // Bias towards the interesting small sizes (0, 1, fewer items than workers) without
            // forgetting large ones.
            int length = random.Next(4) switch
            {
                0 => random.Next(0, 4),
                1 => random.Next(0, 33),
                2 => random.Next(0, 5_000),
                _ => random.Next(0, 300_000),
            };
            int dop = random.Next(1, 17);
            IPartitioner partitioner = TestSupport.Partitioners[random.Next(TestSupport.Partitioners.Length)];
            yield return (seed, length, dop, partitioner);
        }
    }

    [Fact]
    public void ForWithAStructBodyMatchesSequential()
    {
        foreach (var (seed, length, dop, partitioner) in RandomCases())
        {
            double[] source = RandomDoubles(seed, length);
            var expected = new double[length];
            for (int i = 0; i < length; i++)
                expected[i] = source[i] * 2 + 1;

            var actual = new double[length];
            Apl.For(0, length, new AffineBody(source, actual), TestSupport.Options(dop, partitioner));
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void ForWithADelegateMatchesSequential()
    {
        foreach (var (seed, length, dop, partitioner) in RandomCases())
        {
            double[] source = RandomDoubles(seed, length);
            var actual = new double[length];
            Apl.For(0, length, i => actual[i] = Math.Sqrt(Math.Abs(source[i])) * Math.Sin(source[i]), TestSupport.Options(dop, partitioner));

            for (int i = 0; i < length; i++)
                Assert.Equal(Math.Sqrt(Math.Abs(source[i])) * Math.Sin(source[i]), actual[i]);
        }
    }

    [Fact]
    public void ForEachByReferenceMatchesSequential()
    {
        foreach (var (seed, length, dop, partitioner) in RandomCases())
        {
            int[] data = RandomInts(seed, length);
            int[] expected = data.Select((v, i) => v ^ i).ToArray();

            Apl.ForEach(data, new XorIndexBody(), TestSupport.Options(dop, partitioner));
            Assert.Equal(expected, data);

            Apl.ForEach(data, (int i, ref int v) => v ^= i, TestSupport.Options(dop, partitioner));
            Apl.ForEach(data.AsMemory(), new XorIndexBody(), TestSupport.Options(dop, partitioner));
            Assert.Equal(expected, data);
        }
    }

    [Fact]
    public void ForEachByValueVisitsEveryElementOnce()
    {
        foreach (var (seed, length, dop, partitioner) in RandomCases())
        {
            int[] data = RandomInts(seed, length);
            long sum = 0;
            Apl.ForEach(data, v => Interlocked.Add(ref sum, v), TestSupport.Options(dop, partitioner));
            Assert.Equal(data.Sum(v => (long)v), sum);
        }
    }

    [Fact]
    public void ReduceMatchesSequentialExactlyForIntegers()
    {
        foreach (var (seed, length, dop, partitioner) in RandomCases())
        {
            long[] values = RandomInts(seed, length).Select(v => (long)v).ToArray();
            long expected = values.Sum();

            Assert.Equal(expected, Apl.Reduce(0, length, 0L, new LongSumBody(values), TestSupport.Options(dop, partitioner)));
            Assert.Equal(expected, Apl.Reduce(0, length, 0L, (from, to, acc) =>
            {
                for (int i = from; i < to; i++)
                    acc += values[i];
                return acc;
            }, (a, b) => a + b, TestSupport.Options(dop, partitioner)));
        }
    }

    [Fact]
    public void ReduceWithCancellationBlocksStillMatches()
    {
        using var cts = new CancellationTokenSource();
        long[] values = RandomInts(7, 123_457).Select(v => (long)v).ToArray();
        var options = new AplOptions { MaxDegreeOfParallelism = 5, CancellationToken = cts.Token, CancellationCheckInterval = 1000 };
        Assert.Equal(values.Sum(), Apl.Reduce(0, values.Length, 0L, new LongSumBody(values), options));
    }

    [Fact]
    public void ReduceWithANonCommutativeButAssociativeCombineKeepsOrderUnderStaticPartitioning()
    {
        // String concatenation is associative but not commutative: partial results must be combined
        // in partition order for the static and striped-free cases.
        const int n = 5000;
        string expected = string.Concat(Enumerable.Range(0, n).Select(i => (char)('a' + i % 26)));
        string actual = Apl.Reduce(0, n, string.Empty, (from, to, acc) =>
        {
            var chars = new char[to - from];
            for (int i = from; i < to; i++)
                chars[i - from] = (char)('a' + i % 26);
            return acc + new string(chars);
        }, (a, b) => a + b, TestSupport.Options(7));
        Assert.Equal(expected, actual);
    }

    private readonly struct XorIndexBody : IWorkBody<int>
    {
        public void Invoke(int index, ref int item) => item ^= index;
    }

    internal static double[] RandomDoubles(int seed, int length)
    {
        var random = new Random(seed);
        var result = new double[length];
        for (int i = 0; i < length; i++)
            result[i] = random.NextDouble() * 200 - 100;
        return result;
    }

    internal static int[] RandomInts(int seed, int length)
    {
        var random = new Random(seed);
        var result = new int[length];
        for (int i = 0; i < length; i++)
            result[i] = random.Next(int.MinValue, int.MaxValue);
        return result;
    }
}
