// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using RLNet.Policies;
using RLNet.Utils;
using Xunit;

namespace RLNet.Tests;

/// <summary>
/// Checks the gSDE head: its distribution, the gradients PPO and A2C climb through it, and that
/// its exploration really is a function of the state.
/// </summary>
/// <remarks>
/// Same standard as <see cref="GaussianPolicyTests"/>. The spreads here are a matrix, one per
/// feature and action, and each is credited by its share of the variance — exactly the kind of
/// term where a dropped square trains without complaint and plateaus lower.
/// </remarks>
public class StateDependentExplorationTests
{
    private const int Actions = 2;
    private const int Features = 3;

    private static readonly float[] Mean = [0.3f, -0.6f];
    private static readonly float[] FeatureRow = [0.8f, -0.25f, 0.5f];
    private static readonly float[] Action = [0.7f, -0.9f];
    private static readonly float[] Spreads = [-0.4f, 0.2f, -1.3f, -0.7f, 0.1f, -2f];

    [Fact]
    public void LogProbability_MatchesTheNormalDensityOfTheFeatureWeightedVariance()
    {
        var head = Head();

        double expected = 0, expectedEntropy = 0;
        for (int j = 0; j < Actions; j++)
        {
            double variance = 1e-6;
            for (int i = 0; i < Features; i++)
                variance += FeatureRow[i] * FeatureRow[i] * Math.Exp(2 * Spreads[i * Actions + j]);

            double d = Action[j] - Mean[j];
            expected += -0.5 * (d * d / variance + Math.Log(2 * Math.PI * variance));
            expectedEntropy += 0.5 * Math.Log(2 * Math.PI * Math.E * variance);
        }

        float logProbability = head.Evaluate(Mean, FeatureRow, Action, out float entropy);
        Assert.Equal(expected, logProbability, 4);
        Assert.Equal(expectedEntropy, entropy, 4);
    }

    [Fact]
    public void LogProbabilityGradient_MatchesFiniteDifferences()
    {
        const float coefficient = 0.7f;
        var head = Head();
        var meanGradient = new float[Actions];

        head.BeginUpdate();
        head.Evaluate(Mean, FeatureRow, Action, out _);
        head.AccumulateLogProbabilityGradient(meanGradient, Mean, Action, coefficient);

        for (int j = 0; j < Actions; j++)
        {
            float numeric = Difference(Mean, j, mean => coefficient * Head().Evaluate(mean, FeatureRow, Action, out _));
            AssertClose(numeric, meanGradient[j], $"mean[{j}]");
        }

        for (int k = 0; k < Spreads.Length; k++)
        {
            float numeric = Difference(Spreads, k, spreads => coefficient * Head(spreads).Evaluate(Mean, FeatureRow, Action, out _));
            AssertClose(numeric, head.LogStdGradient[k], $"logStd[{k}]");
        }
    }

    [Fact]
    public void EntropyGradient_MatchesFiniteDifferences()
    {
        // The head accumulates ∂(−c·H), the entropy bonus as a loss.
        const float coefficient = 0.3f;
        var head = Head();

        head.BeginUpdate();
        head.Evaluate(Mean, FeatureRow, Action, out float entropy);
        head.AccumulateEntropyGradient(new float[Actions], entropy, coefficient);

        for (int k = 0; k < Spreads.Length; k++)
        {
            float numeric = Difference(Spreads, k, spreads =>
            {
                Head(spreads).Evaluate(Mean, FeatureRow, Action, out float h);
                return -coefficient * h;
            });
            AssertClose(numeric, head.LogStdGradient[k], $"logStd[{k}]");
        }
    }

    [Fact]
    public void ExplorationIsAFunctionOfTheStateWithinARollout()
    {
        var head = Head();
        var random = new FastRandom(4);
        head.BeginRollout(streams: 2, random);

        var first = new float[Actions];
        var again = new float[Actions];
        var otherStream = new float[Actions];
        head.Sample(Mean, FeatureRow, 0, first, random, deterministic: false, out _);
        head.Sample(Mean, FeatureRow, 0, again, random, deterministic: false, out _);
        head.Sample(Mean, FeatureRow, 1, otherStream, random, deterministic: false, out _);

        // Same state, same stream, same rollout: the same perturbation, not a fresh draw.
        Assert.Equal(first, again);
        Assert.NotEqual(first, otherStream);
        Assert.NotEqual(Mean, first);

        // A new rollout draws a new perturbation.
        head.BeginRollout(streams: 2, random);
        var next = new float[Actions];
        head.Sample(Mean, FeatureRow, 0, next, random, deterministic: false, out _);
        Assert.NotEqual(first, next);

        var greedy = new float[Actions];
        head.Sample(Mean, FeatureRow, 0, greedy, random, deterministic: true, out _);
        Assert.Equal(Mean, greedy);
    }

    [Fact]
    public void SpreadAcrossRolloutsMatchesTheStatedVariance()
    {
        // Over many rollouts the perturbation of one state is Gaussian with exactly the variance
        // the log-probabilities assume — otherwise every importance ratio would be off.
        var head = Head();
        var random = new FastRandom(11);
        var action = new float[Actions];
        double[] sum = new double[Actions], sumSquares = new double[Actions];
        const int draws = 40_000;

        for (int n = 0; n < draws; n++)
        {
            head.BeginRollout(1, random);
            head.Sample(Mean, FeatureRow, 0, action, random, deterministic: false, out _);
            for (int j = 0; j < Actions; j++)
            {
                double d = action[j] - Mean[j];
                sum[j] += d;
                sumSquares[j] += d * d;
            }
        }

        for (int j = 0; j < Actions; j++)
        {
            double variance = 0;
            for (int i = 0; i < Features; i++)
                variance += FeatureRow[i] * FeatureRow[i] * Math.Exp(2 * Spreads[i * Actions + j]);

            double mean = sum[j] / draws;
            double measured = sumSquares[j] / draws - mean * mean;
            Assert.True(Math.Abs(mean) < 0.02, $"dimension {j}: mean perturbation {mean:F4}");
            Assert.True(Math.Abs(measured / variance - 1) < 0.04, $"dimension {j}: variance {measured:F4} against {variance:F4}");
        }
    }

    private static StateDependentHead Head(float[]? spreads = null)
    {
        var head = new StateDependentHead(Actions, Features, initialLogStd: 0f, learningRate: 1e-3f);
        (spreads ?? Spreads).CopyTo(head.LogStd);
        return head;
    }

    private static float Difference(float[] values, int index, Func<float[], float> function)
    {
        const float epsilon = 1e-3f;
        var plus = (float[])values.Clone();
        var minus = (float[])values.Clone();
        plus[index] += epsilon;
        minus[index] -= epsilon;
        return (function(plus) - function(minus)) / (2 * epsilon);
    }

    private static void AssertClose(float numeric, float analytic, string what)
    {
        float tolerance = 2e-2f * MathF.Max(1f, MathF.Abs(numeric));
        Assert.True(MathF.Abs(numeric - analytic) < tolerance, $"{what}: analytic {analytic}, numeric {numeric}");
    }
}
