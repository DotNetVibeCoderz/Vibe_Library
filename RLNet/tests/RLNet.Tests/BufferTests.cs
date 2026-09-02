// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using RLNet.Buffers;
using RLNet.Spaces;
using RLNet.Utils;
using Xunit;

namespace RLNet.Tests;

public class SumTreeTests
{
    [Fact]
    public void TracksSumAndMinimum()
    {
        var tree = new SumTree(8);
        float[] values = [3f, 1f, 4f, 1f, 5f, 9f, 2f, 6f];

        for (int i = 0; i < values.Length; i++) tree.Set(i, values[i]);

        Assert.Equal(values.Sum(), tree.Total, 3);
        Assert.Equal(values.Min(), tree.Min, 3);
        Assert.Equal(values.Max(), tree.Max, 3);
    }

    [Fact]
    public void MinimumRisesWhenTheSmallestLeafIsRaised()
    {
        // The minimum cannot be maintained with the sum's delta trick, so this is the case that
        // catches a min-tree updated the wrong way.
        var tree = new SumTree(4);
        tree.Set(0, 5f);
        tree.Set(1, 1f);
        tree.Set(2, 3f);
        tree.Set(3, 4f);

        Assert.Equal(1f, tree.Min, 3);

        tree.Set(1, 10f);
        Assert.Equal(3f, tree.Min, 3);
    }

    [Fact]
    public void UnwrittenLeavesDoNotWinTheMinimum()
    {
        // A buffer that is not yet full has untouched leaves. If those counted as zero, every
        // importance-sampling weight would be normalised against a transition that does not exist.
        var tree = new SumTree(1024);
        tree.Set(0, 2f);
        tree.Set(1, 7f);

        Assert.Equal(2f, tree.Min, 3);
    }

    [Fact]
    public void FindSelectsProportionallyToLeafValue()
    {
        var tree = new SumTree(4);
        tree.Set(0, 1f);
        tree.Set(1, 0f);
        tree.Set(2, 3f);
        tree.Set(3, 0f);

        // Leaf 1 and 3 have zero mass and must never be selected; leaf 2 should take three
        // quarters of the draws.
        var counts = new int[4];
        var random = new FastRandom(17);
        for (int i = 0; i < 4_000; i++) counts[tree.Find(random.NextSingle() * tree.Total)]++;

        Assert.Equal(0, counts[1]);
        Assert.Equal(0, counts[3]);
        Assert.InRange(counts[2] / 4000.0, 0.70, 0.80);
    }

    [Fact]
    public void FindHandlesATargetAtTheTotal()
    {
        // Float rounding puts a stratified draw exactly at Total often enough to matter; it must
        // return a valid leaf rather than walking into the padding.
        var tree = new SumTree(3);
        tree.Set(0, 1f);
        tree.Set(1, 1f);
        tree.Set(2, 1f);

        Assert.InRange(tree.Find(tree.Total), 0, 2);
    }
}

public class ReplayBufferTests
{
    [Fact]
    public void OverwritesOldestWhenFull()
    {
        var buffer = new UniformReplayBuffer(capacity: 4, observationSize: 1, actionSize: 1);

        for (int i = 0; i < 10; i++)
            buffer.AddDiscrete([i], 0, i, [i + 1], terminated: false);

        Assert.Equal(4, buffer.Count);
        Assert.Equal(4, buffer.Capacity);

        // Only the last four rewards should survive.
        var batch = new ReplayBatch(64, 1, 1);
        var random = new FastRandom(1);
        var seen = new HashSet<float>();

        for (int i = 0; i < 200; i++)
        {
            buffer.Sample(4, batch, random);
            for (int j = 0; j < batch.Count; j++) seen.Add(batch.Rewards[j]);
        }

        Assert.Equal(new HashSet<float> { 6, 7, 8, 9 }, seen);
    }

    [Fact]
    public void RoundTripsEveryField()
    {
        var buffer = new UniformReplayBuffer(capacity: 2, observationSize: 3, actionSize: 2);
        buffer.Add([1f, 2f, 3f], [0.5f, -0.5f], 1.5f, [4f, 5f, 6f], terminated: true);

        var batch = new ReplayBatch(1, 3, 2);
        buffer.Sample(1, batch, new FastRandom(1));

        Assert.Equal([1f, 2f, 3f], batch.Observation(0).ToArray());
        Assert.Equal([4f, 5f, 6f], batch.NextObservation(0).ToArray());
        Assert.Equal([0.5f, -0.5f], batch.Action(0).ToArray());
        Assert.Equal(1.5f, batch.Rewards[0], 4);
        Assert.True(batch.Terminated[0]);
        Assert.Equal(1f, batch.Weights[0], 4);
    }

    [Fact]
    public void UniformSamplingIsUnbiased()
    {
        var buffer = new UniformReplayBuffer(capacity: 10, observationSize: 1, actionSize: 1);
        for (int i = 0; i < 10; i++) buffer.AddDiscrete([i], 0, i, [i], terminated: false);

        var counts = new int[10];
        var batch = new ReplayBatch(32, 1, 1);
        var random = new FastRandom(42);

        for (int i = 0; i < 1_000; i++)
        {
            buffer.Sample(32, batch, random);
            for (int j = 0; j < batch.Count; j++) counts[(int)batch.Rewards[j]]++;
        }

        // 32,000 draws over 10 slots: every slot should land near 3,200.
        foreach (int count in counts) Assert.InRange(count, 2_800, 3_600);
    }

    [Fact]
    public void PrioritizedSamplingFavoursHighError()
    {
        var buffer = new PrioritizedReplayBuffer(capacity: 10, observationSize: 1, actionSize: 1, beta: 0.4f);
        for (int i = 0; i < 10; i++) buffer.AddDiscrete([i], 0, i, [i], terminated: false);

        // One transition is far more surprising than the rest.
        Span<int> indices = stackalloc int[10];
        Span<float> errors = stackalloc float[10];
        for (int i = 0; i < 10; i++)
        {
            indices[i] = i;
            errors[i] = i == 7 ? 100f : 0.01f;
        }
        buffer.UpdatePriorities(indices, errors);

        var counts = new int[10];
        var batch = new ReplayBatch(8, 1, 1);
        var random = new FastRandom(3);

        for (int i = 0; i < 500; i++)
        {
            buffer.Sample(8, batch, random);
            for (int j = 0; j < batch.Count; j++) counts[batch.Indices[j]]++;
        }

        Assert.True(counts[7] > counts.Where((_, i) => i != 7).Max() * 3,
            $"High-error transition drawn {counts[7]} times, not clearly more than the rest.");
    }

    [Fact]
    public void PrioritizedWeightsAreNormalisedToAtMostOne()
    {
        // The weights are divided by the largest in the buffer, so none may exceed 1 — otherwise
        // prioritised replay silently scales up the learning rate.
        var buffer = new PrioritizedReplayBuffer(capacity: 32, observationSize: 1, actionSize: 1);
        for (int i = 0; i < 32; i++) buffer.AddDiscrete([i], 0, i, [i], terminated: false);

        Span<int> indices = stackalloc int[32];
        Span<float> errors = stackalloc float[32];
        var random = new FastRandom(8);
        for (int i = 0; i < 32; i++)
        {
            indices[i] = i;
            errors[i] = random.NextSingle() * 10f;
        }
        buffer.UpdatePriorities(indices, errors);

        var batch = new ReplayBatch(16, 1, 1);
        for (int i = 0; i < 50; i++)
        {
            buffer.Sample(16, batch, random);
            for (int j = 0; j < batch.Count; j++)
                Assert.InRange(batch.Weights[j], 0f, 1.0001f);
        }
    }

    [Fact]
    public void NewTransitionsEnterAtMaximumPriority()
    {
        // Every transition must be replayed at least once before it can be down-ranked, or a
        // transition added while others have high error may never be seen at all.
        var buffer = new PrioritizedReplayBuffer(capacity: 8, observationSize: 1, actionSize: 1);
        for (int i = 0; i < 4; i++) buffer.AddDiscrete([i], 0, i, [i], terminated: false);

        Span<int> indices = [0, 1, 2, 3];
        Span<float> errors = [50f, 50f, 50f, 50f];
        buffer.UpdatePriorities(indices, errors);

        buffer.AddDiscrete([99], 0, 99, [99], terminated: false);

        var counts = new int[8];
        var batch = new ReplayBatch(5, 1, 1);
        var random = new FastRandom(4);
        for (int i = 0; i < 200; i++)
        {
            buffer.Sample(5, batch, random);
            for (int j = 0; j < batch.Count; j++) counts[batch.Indices[j]]++;
        }

        Assert.True(counts[4] > 0, "A freshly added transition was never sampled.");
    }
}

public class RolloutBufferTests
{
    /// <summary>
    /// Checks GAE against a return computed by hand on a short deterministic rollout.
    /// </summary>
    [Fact]
    public void GaeMatchesAHandComputedRollout()
    {
        var buffer = new RolloutBuffer(3, observationSize: 1, actionSize: 1);
        const float gamma = 0.9f, lambda = 0.5f;

        // Three steps, values 1, 2, 3, rewards 1 each, episode still running at the end.
        buffer.AddDiscrete([0], 0, 0f, value: 1f, reward: 1f, terminated: false, truncated: false);
        buffer.AddDiscrete([0], 0, 0f, value: 2f, reward: 1f, terminated: false, truncated: false);
        buffer.AddDiscrete([0], 0, 0f, value: 3f, reward: 1f, terminated: false, truncated: false);

        buffer.ComputeAdvantages(lastValue: 4f, gamma, lambda);

        // delta_2 = 1 + 0.9*4 - 3 = 1.6 ; A_2 = 1.6
        // delta_1 = 1 + 0.9*3 - 2 = 1.7 ; A_1 = 1.7 + 0.45*1.6 = 2.42
        // delta_0 = 1 + 0.9*2 - 1 = 1.8 ; A_0 = 1.8 + 0.45*2.42 = 2.889
        Assert.Equal(1.6f, buffer.Advantages[2], 3);
        Assert.Equal(2.42f, buffer.Advantages[1], 3);
        Assert.Equal(2.889f, buffer.Advantages[0], 3);

        // Returns are advantages plus the old value estimate.
        Assert.Equal(4.6f, buffer.Returns[2], 3);
        Assert.Equal(4.42f, buffer.Returns[1], 3);
        Assert.Equal(3.889f, buffer.Returns[0], 3);
    }

    /// <summary>
    /// The distinction the whole terminated/truncated split exists for.
    /// </summary>
    [Fact]
    public void TerminationZeroesTheBootstrapButTruncationDoesNot()
    {
        const float gamma = 0.99f, lambda = 0.95f;

        var terminated = new RolloutBuffer(1, 1, 1);
        terminated.AddDiscrete([0], 0, 0f, value: 5f, reward: 1f, terminated: true, truncated: false);
        terminated.ComputeAdvantages(lastValue: 0f, gamma, lambda);

        var truncated = new RolloutBuffer(1, 1, 1);
        truncated.AddDiscrete([0], 0, 0f, value: 5f, reward: 1f, terminated: false, truncated: true,
            bootstrapValue: 10f);
        truncated.ComputeAdvantages(lastValue: 0f, gamma, lambda);

        // Terminated: A = 1 + 0 - 5 = -4.
        Assert.Equal(-4f, terminated.Advantages[0], 3);

        // Truncated: A = 1 + 0.99*10 - 5 = 5.9. Treating this as terminal would report -4 and
        // teach the agent that the time limit is a catastrophe.
        Assert.Equal(5.9f, truncated.Advantages[0], 3);
    }

    [Fact]
    public void AdvantageChainBreaksAtEpisodeBoundaries()
    {
        // Two episodes packed into one rollout. The first episode's advantage must not leak
        // backwards into the step before it.
        var buffer = new RolloutBuffer(3, 1, 1);
        buffer.AddDiscrete([0], 0, 0f, value: 0f, reward: 1f, terminated: false, truncated: false);
        buffer.AddDiscrete([0], 0, 0f, value: 0f, reward: 1f, terminated: true, truncated: false);
        buffer.AddDiscrete([0], 0, 0f, value: 0f, reward: 100f, terminated: false, truncated: false);

        buffer.ComputeAdvantages(lastValue: 0f, gamma: 0.99f, lambda: 0.95f);

        // Step 1 terminates, so step 0 sees only its own delta plus nothing from step 1's chain.
        Assert.Equal(1f, buffer.Advantages[1], 3);
        Assert.Equal(1f + 0.99f * 0.95f * 1f, buffer.Advantages[0], 3);
    }

    [Fact]
    public void NormalizeAdvantagesGivesZeroMeanUnitVariance()
    {
        var buffer = new RolloutBuffer(4, 1, 1);
        float[] values = [1f, 5f, 9f, 3f];
        foreach (float v in values)
            buffer.AddDiscrete([0], 0, 0f, value: 0f, reward: v, terminated: true, truncated: false);

        buffer.ComputeAdvantages(0f, gamma: 0f, lambda: 0f);
        buffer.NormalizeAdvantages();

        var advantages = buffer.Advantages.AsSpan(0, 4);
        float mean = 0f;
        for (int i = 0; i < 4; i++) mean += advantages[i];
        mean /= 4;

        float variance = 0f;
        for (int i = 0; i < 4; i++) variance += (advantages[i] - mean) * (advantages[i] - mean);
        variance /= 4;

        Assert.Equal(0f, mean, 4);
        Assert.Equal(1f, variance, 3);
    }
}

public class SpaceTests
{
    [Fact]
    public void BoxClampBoundsEveryDimension()
    {
        var space = new BoxSpace([-1f, 0f], [1f, 10f]);
        Span<float> value = [5f, -5f];

        space.Clamp(value);

        Assert.Equal(1f, value[0]);
        Assert.Equal(0f, value[1]);
    }

    [Fact]
    public void ScaleFromUnitMapsTheUnitIntervalOntoTheBounds()
    {
        // SAC and TD3 both emit tanh output, so this mapping sits between every continuous
        // policy and every continuous environment.
        var space = new BoxSpace([-2f, 0f], [2f, 10f]);

        Span<float> low = [-1f, -1f];
        space.ScaleFromUnit(low);
        Assert.Equal(-2f, low[0], 4);
        Assert.Equal(0f, low[1], 4);

        Span<float> mid = [0f, 0f];
        space.ScaleFromUnit(mid);
        Assert.Equal(0f, mid[0], 4);
        Assert.Equal(5f, mid[1], 4);

        Span<float> high = [1f, 1f];
        space.ScaleFromUnit(high);
        Assert.Equal(2f, high[0], 4);
        Assert.Equal(10f, high[1], 4);
    }

    [Fact]
    public void UnboundedDimensionsStillSample()
    {
        var space = BoxSpace.Unbounded(3);
        Span<float> sample = stackalloc float[3];

        space.Sample(new FastRandom(1), sample);
        foreach (float value in sample) Assert.True(float.IsFinite(value));
    }

    [Fact]
    public void DiscreteContainsRejectsFractionsAndOutOfRange()
    {
        var space = new DiscreteSpace(3);

        Assert.True(space.Contains([0f]));
        Assert.True(space.Contains([2f]));
        Assert.False(space.Contains([3f]));
        Assert.False(space.Contains([-1f]));
        Assert.False(space.Contains([1.5f]));
    }
}
