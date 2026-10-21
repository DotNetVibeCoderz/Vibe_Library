// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using RLNet.Normalization;
using RLNet.Persistence;
using RLNet.Utils;
using Xunit;

namespace RLNet.Tests;

public class NormalizationTests
{
    [Fact]
    public void RunningStatisticsMatchTwoPassStatistics()
    {
        var random = new FastRandom(1);
        var samples = new float[5_000][];
        var statistics = new RunningMeanStd(2);

        for (int i = 0; i < samples.Length; i++)
        {
            // A large offset is where the naive sum-of-squares form loses its precision.
            samples[i] = [1_000f + random.NextGaussian() * 3f, random.NextGaussian() * 0.01f];
            statistics.Update(samples[i]);
        }

        for (int d = 0; d < 2; d++)
        {
            double mean = samples.Average(s => (double)s[d]);
            double variance = samples.Average(s => (s[d] - mean) * (s[d] - mean));

            Assert.Equal(mean, statistics.Mean(d), 3);
            Assert.Equal(variance, statistics.Variance(d), variance * 1e-3);
        }
    }

    [Fact]
    public void NormalizedObservationsAreStandardAndClipped()
    {
        var random = new FastRandom(2);
        var normalizer = new ObservationNormalizer(1, clip: 5f);
        for (int i = 0; i < 10_000; i++) normalizer.Update([50f + random.NextGaussian() * 10f]);

        Span<float> output = stackalloc float[1];
        normalizer.Normalize([60f], output);
        Assert.Equal(1f, output[0], 1);

        normalizer.Normalize([1e6f], output);
        Assert.Equal(5f, output[0]);
    }

    [Fact]
    public void RewardScalerDividesByTheReturnSpreadWithoutShifting()
    {
        var scaler = new RewardScaler(gamma: 0.99f);
        for (int i = 0; i < 2_000; i++) scaler.Scale(0, -16f, episodeEnded: i % 200 == 199);

        // A constant negative reward must stay negative: scaling, never centring.
        float scaled = scaler.Scale(0, -16f, episodeEnded: false);
        Assert.True(scaled < 0f && scaled > -16f, $"Scaled reward {scaled} was not a shrunken negative.");
    }

    [Fact]
    public void StatisticsSurviveACheckpointBitForBit()
    {
        var random = new FastRandom(3);
        var original = new ObservationNormalizer(3);
        for (int i = 0; i < 777; i++) original.Update([random.NextGaussian(), random.NextSingle(), 7f]);

        var restored = new ObservationNormalizer(3);
        using var stream = new MemoryStream();
        Checkpoint.Save(stream, original);
        stream.Position = 0;
        Checkpoint.Load(stream, restored);

        float[] probe = [0.3f, 0.9f, 7f];
        var a = new float[3];
        var b = new float[3];
        original.Normalize(probe, a);
        restored.Normalize(probe, b);
        Assert.Equal(a, b);
        Assert.Equal(original.Statistics.Count, restored.Statistics.Count);
    }
}
