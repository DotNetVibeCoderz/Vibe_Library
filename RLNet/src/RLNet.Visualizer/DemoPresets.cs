// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using RLNet.Agents;
using RLNet.Spaces;

namespace RLNet.Visualizer;

/// <summary>
/// Agent settings tuned for watching rather than for final score.
/// </summary>
/// <remarks>
/// <para>
/// The library defaults follow the published hyper-parameters, which are chosen to reach the best
/// result over a long run. They are the wrong settings for a console: SAC's default is two
/// 256-unit hidden layers with a gradient step on every environment step, which is about
/// 20 steps a second here — a viewer would watch a still image while the first episode crawled by.
/// </para>
/// <para>
/// These presets trade final performance for a curve that moves within seconds: narrower
/// networks, smaller batches, and for the continuous agents a gradient step every second or
/// fourth environment step rather than every one. The result still learns Pendulum and CartPole,
/// just to a slightly worse plateau — which is the right trade for something whose job is to
/// show what learning looks like.
/// </para>
/// <para>
/// Nothing here is the library speaking. Anyone training for real should use
/// <see cref="Catalog"/>'s defaults, or their own.
/// </para>
/// </remarks>
public static class DemoPresets
{
    /// <summary>Builds a discrete agent sized for live viewing.</summary>
    public static IDiscreteAgent CreateDiscrete(
        Algorithm algorithm, Space observationSpace, DiscreteSpace actionSpace, int seed) => algorithm switch
        {
            // Tabular Q-learning is already the fastest thing here; only the schedule is shortened
            // so the console reaches low exploration within a demonstration rather than an hour.
            Algorithm.QLearning => new QTableAgent(
                actionSpace,
                observationSpace is BoxSpace box && !IsOneHot(box)
                    ? StateDiscretizer.ForBox(box, DefaultBins(box))
                    : StateDiscretizer.OneHot(),
                new QTableOptions { LearningRate = 0.2f, Epsilon = Schedule.Exponential(1f, 0.05f, 0.25f) },
                seed),

            Algorithm.Dqn => new DqnAgent(
                observationSpace, actionSpace,
                new DqnOptions
                {
                    HiddenSizes = [64, 64],
                    BatchSize = 32,
                    LearningStarts = 500,
                    TargetUpdateInterval = 200,
                    LearningRate = 1e-3f,

                    // The single biggest lever on how fast the console runs: one gradient step per
                    // two environment steps roughly doubles the visible step rate, and DQN learns
                    // CartPole comfortably at this ratio.
                    TrainFrequency = 2,
                    Epsilon = Schedule.Linear(1f, 0.05f, 0.35f),
                },
                seed: seed),

            Algorithm.Ppo => new PpoAgent(
                observationSpace, actionSpace,
                // A short rollout so the first policy update lands seconds in. At the library
                // default of 2048 the console would collect for a while showing nothing changing.
                new PpoOptions { HiddenSizes = [64, 64], RolloutLength = 512, Epochs = 6 },
                seed: seed),

            Algorithm.A2C => new A2CAgent(
                observationSpace, actionSpace,
                new A2COptions { HiddenSizes = [64, 64] },
                seed: seed),

            _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, "Not a discrete algorithm."),
        };

    /// <summary>Builds a continuous agent sized for live viewing.</summary>
    public static IContinuousAgent CreateContinuous(
        Algorithm algorithm, Space observationSpace, BoxSpace actionSpace, int seed) => algorithm switch
        {
            Algorithm.Sac => new SacAgent(
                observationSpace, actionSpace,
                new SacOptions
                {
                    // 64 units and a batch of 64, against the library's 256 and 256. SAC runs a
                    // dozen network passes per gradient step across its actor and four critics,
                    // so the cost of a step scales sharply with both.
                    HiddenSizes = [64, 64],
                    BatchSize = 64,
                    LearningStarts = 500,
                    TrainFrequency = 2,
                },
                seed: seed),

            Algorithm.Td3 => new Td3Agent(
                observationSpace, actionSpace,
                new Td3Options
                {
                    HiddenSizes = [64, 64],
                    BatchSize = 64,
                    LearningStarts = 500,
                    TrainFrequency = 2,
                },
                seed: seed),

            _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, "Not a continuous algorithm."),
        };

    private static bool IsOneHot(BoxSpace space)
    {
        if (space.FlatSize < 4) return false;
        for (int i = 0; i < space.FlatSize; i++)
            if (space.Low[i] != 0f || space.High[i] != 1f) return false;
        return true;
    }

    private static int[] DefaultBins(BoxSpace space)
    {
        var bins = new int[space.FlatSize];
        Array.Fill(bins, space.FlatSize switch { <= 2 => 20, <= 4 => 10, <= 6 => 6, _ => 4 });
        return bins;
    }
}
