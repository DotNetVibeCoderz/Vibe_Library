// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using RLNet.Policies;
using RLNet.Utils;
using Xunit;

namespace RLNet.Tests;

/// <summary>
/// Checks the Gaussian policy's log-probability, entropy and their gradients.
/// </summary>
/// <remarks>
/// The same standard as <see cref="NeuralNetworkTests"/>: continuous PPO and A2C climb exactly
/// these gradients, and a wrong sign on the log-std term would give a policy that trains while
/// its spread drifts the wrong way — visible only as a worse plateau.
/// </remarks>
public class GaussianPolicyTests
{
    private static readonly float[] Mean = [0.3f, -1.1f, 0.05f];
    private static readonly float[] LogStd = [-0.4f, 0.2f, -1.3f];
    private static readonly float[] Action = [0.9f, -0.2f, 0.01f];

    [Fact]
    public void LogProbability_MatchesTheNormalDensity()
    {
        double expected = 0;
        for (int i = 0; i < Mean.Length; i++)
        {
            double std = Math.Exp(LogStd[i]);
            double z = (Action[i] - Mean[i]) / std;
            expected += Math.Log(Math.Exp(-0.5 * z * z) / (std * Math.Sqrt(2 * Math.PI)));
        }

        Assert.Equal(expected, GaussianPolicy.LogProbability(Mean, LogStd, Action), 4);
    }

    [Fact]
    public void LogProbabilityGradient_MatchesFiniteDifferences()
    {
        var meanGradient = new float[3];
        var logStdGradient = new float[3];
        const float coefficient = 0.7f;
        GaussianPolicy.AccumulateLogProbabilityGradient(meanGradient, logStdGradient, Mean, LogStd, Action, coefficient);

        const float epsilon = 1e-3f;
        for (int i = 0; i < Mean.Length; i++)
        {
            float numericMean = Difference(Mean, i, epsilon, m => coefficient * GaussianPolicy.LogProbability(m, LogStd, Action));
            float numericLogStd = Difference(LogStd, i, epsilon, s => coefficient * GaussianPolicy.LogProbability(Mean, s, Action));

            AssertClose(numericMean, meanGradient[i], $"mean[{i}]");
            AssertClose(numericLogStd, logStdGradient[i], $"logStd[{i}]");
        }
    }

    [Fact]
    public void EntropyGradient_MatchesFiniteDifferences()
    {
        var gradient = new float[3];
        const float coefficient = 0.25f;
        GaussianPolicy.AccumulateEntropyGradient(gradient, coefficient);

        for (int i = 0; i < LogStd.Length; i++)
        {
            float numeric = Difference(LogStd, i, 1e-3f, s => -coefficient * GaussianPolicy.Entropy(s));
            AssertClose(numeric, gradient[i], $"logStd[{i}]");
        }
    }

    [Fact]
    public void Entropy_MatchesTheClosedForm()
    {
        double expected = 0;
        foreach (float s in LogStd) expected += s + 0.5 * Math.Log(2 * Math.PI * Math.E);

        Assert.Equal(expected, GaussianPolicy.Entropy(LogStd), 4);
    }

    [Fact]
    public void DeterministicSampleIsTheMean()
    {
        var action = new float[3];
        GaussianPolicy.Sample(Mean, LogStd, action, new FastRandom(1), deterministic: true);
        Assert.Equal(Mean, action);
    }

    [Fact]
    public void Samples_HaveTheRequestedMeanAndSpread()
    {
        var random = new FastRandom(9);
        var action = new float[3];
        const int n = 40_000;
        double sum = 0, sumSquares = 0;

        for (int k = 0; k < n; k++)
        {
            GaussianPolicy.Sample(Mean, LogStd, action, random, deterministic: false);
            sum += action[1];
            sumSquares += action[1] * action[1];
        }

        double mean = sum / n;
        double std = Math.Sqrt(sumSquares / n - mean * mean);
        Assert.Equal(Mean[1], mean, 1);
        Assert.Equal(Math.Exp(LogStd[1]), std, 1);
    }

    private static float Difference(float[] point, int index, float epsilon, Func<float[], float> function)
    {
        var plus = (float[])point.Clone();
        var minus = (float[])point.Clone();
        plus[index] += epsilon;
        minus[index] -= epsilon;
        return (function(plus) - function(minus)) / (2 * epsilon);
    }

    private static void AssertClose(float numeric, float analytic, string what)
    {
        float tolerance = 0.02f * Math.Max(1f, Math.Abs(numeric));
        Assert.True(Math.Abs(numeric - analytic) <= tolerance,
            $"{what}: analytic {analytic} vs numeric {numeric}");
    }
}
