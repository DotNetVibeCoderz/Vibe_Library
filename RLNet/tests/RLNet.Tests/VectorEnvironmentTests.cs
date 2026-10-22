// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using RLNet.Agents;
using RLNet.Buffers;
using RLNet.Environments;
using RLNet.Environments.Classic;
using RLNet.Environments.Control;
using RLNet.Training;
using Xunit;

namespace RLNet.Tests;

/// <summary>Checks automatic reset, the two observation buffers, and batched training.</summary>
public class VectorEnvironmentTests
{
    [Fact]
    public void SeededCopiesReplayTheirSingleEnvironmentEquivalents()
    {
        // Copy i seeded with seed + i must be indistinguishable from a lone environment seeded the
        // same way, or a vector run cannot be reproduced one copy at a time.
        var vector = new DiscreteVectorEnvironment(() => new CartPoleEnvironment(), 3);
        vector.Reset(100);

        var singles = Enumerable.Range(0, 3).Select(_ => new CartPoleEnvironment()).ToArray();
        for (int i = 0; i < 3; i++) singles[i].Reset(100 + i);

        int[] actions = [0, 1, 0];
        for (int step = 0; step < 5; step++)
        {
            vector.Step(actions);
            for (int i = 0; i < 3; i++)
            {
                singles[i].Step(actions[i]);
                Assert.Equal(singles[i].Observation.ToArray(), vector.Observations.Slice(i * 4, 4).ToArray());
            }
        }
    }

    [Fact]
    public void EndedEpisodeResetsButKeepsItsFinalStateInNextObservations()
    {
        // Pushing CartPole one way fails it within a few dozen steps.
        var vector = new DiscreteVectorEnvironment(() => new CartPoleEnvironment(), 2);
        vector.Reset(7);

        int[] actions = [1, 1];
        for (int step = 0; step < 200; step++)
        {
            vector.Step(actions);
            if (!vector.Terminated[0]) continue;

            var final = vector.NextObservations[..4].ToArray();
            var fresh = vector.Observations[..4].ToArray();

            Assert.NotEqual(final, fresh);
            Assert.Equal(0, vector[0].ElapsedSteps);

            // A fresh CartPole starts within ±0.05 on every dimension; the final state of a fall
            // is well outside that on the pole angle.
            Assert.All(fresh, value => Assert.InRange(value, -0.05f, 0.05f));
            Assert.True(MathF.Abs(final[2]) > 0.05f);
            return;
        }

        Assert.Fail("CartPole never terminated under a constant push.");
    }

    [Fact]
    public void TruncationIsReportedPerCopy()
    {
        var vector = new ContinuousVectorEnvironment(() => new PendulumEnvironment(), 2);
        vector.Reset(3);

        var actions = new float[2];
        int truncations = 0;
        for (int step = 0; step < vector[0].MaxEpisodeSteps; step++)
        {
            vector.Step(actions);
            Assert.False(vector.Terminated[0]);
            if (vector.Truncated[0]) truncations++;
        }

        Assert.Equal(1, truncations);
    }

    [Fact]
    public void ParallelSteppingMatchesSequential()
    {
        var sequential = new DiscreteVectorEnvironment(() => new LunarLanderEnvironment(), 6, parallel: false);
        var parallel = new DiscreteVectorEnvironment(() => new LunarLanderEnvironment(), 6, parallel: true);
        sequential.Reset(11);
        parallel.Reset(11);

        var random = new RLNet.Utils.FastRandom(5);
        var actions = new int[6];
        for (int step = 0; step < 300; step++)
        {
            for (int i = 0; i < actions.Length; i++) actions[i] = random.NextInt(sequential.ActionSpace.Count);
            sequential.Step(actions);
            parallel.Step(actions);

            Assert.Equal(sequential.Observations.ToArray(), parallel.Observations.ToArray());
            Assert.Equal(sequential.Rewards.ToArray(), parallel.Rewards.ToArray());
        }
    }

    [Fact]
    public void RejectsTheSameInstanceTwice()
    {
        var environment = new CartPoleEnvironment();
        Assert.Throws<ArgumentException>(() => new DiscreteVectorEnvironment([environment, environment]));
    }

    [Fact]
    public void OneCopyReproducesTheSingleEnvironmentRunExactly()
    {
        // The batched path with one environment must be the unbatched path, draw for draw.
        // Anything else means the vector code changes what is learned, not just how fast.
        var options = new PpoOptions { RolloutLength = 256, Epochs = 4 };

        var single = new PpoAgent(new CartPoleEnvironment().ObservationSpace, new CartPoleEnvironment().ActionSpace, options, seed: 2);
        var singleReport = Trainer.Train(new CartPoleEnvironment(), single,
            new TrainingOptions { MaxSteps = 3_000, MaxEpisodes = int.MaxValue, Seed = 8 });

        var batched = new PpoAgent(new CartPoleEnvironment().ObservationSpace, new CartPoleEnvironment().ActionSpace, options, seed: 2);
        var batchedReport = Trainer.Train(new DiscreteVectorEnvironment(() => new CartPoleEnvironment(), 1), batched,
            new TrainingOptions { MaxSteps = 3_000, MaxEpisodes = int.MaxValue, Seed = 8 });

        Assert.Equal(singleReport.Returns, batchedReport.Returns);
        Assert.Equal(single.ExportParameters(), batched.ExportParameters());
    }

    [Fact]
    public void BatchedAgentCountsEpisodesPerCopy()
    {
        var vector = new DiscreteVectorEnvironment(() => new CartPoleEnvironment(), 4);
        var agent = new A2CAgent(vector.ObservationSpace, vector.ActionSpace, seed: 1);

        var report = Trainer.Train(vector, agent, new TrainingOptions { MaxEpisodes = 40, Seed = 1 });

        Assert.Equal(40, report.Episodes);
        Assert.Equal(40, report.Returns.Count);
        Assert.Equal(report.TotalSteps, agent.Metrics.StepCount);
    }

    [Fact]
    public void StreamCountCannotChangeMidRollout()
    {
        var environment = new CartPoleEnvironment();
        var agent = new PpoAgent(environment.ObservationSpace, environment.ActionSpace, new PpoOptions { RolloutLength = 64 }, seed: 1);

        environment.Reset(1);
        var observation = environment.Observation.ToArray();
        int action = agent.SelectAction(observation);
        environment.Step(action);
        agent.Observe(observation, action, 1f, environment.Observation, false, false);

        var two = new float[8];
        Assert.Throws<InvalidOperationException>(() => agent.SelectActions(two, new int[2]));

        // Greedy evaluation on any number of copies is always allowed; it collects nothing.
        agent.SelectActions(two, new int[2], deterministic: true);
    }

    [Fact]
    public void MultiStreamAdvantagesEqualEachStreamComputedAlone()
    {
        // Two interleaved streams must produce, slot for slot, what each would produce in a
        // buffer of its own. GAE that walks across the interleave would mix two trajectories.
        var random = new RLNet.Utils.FastRandom(3);
        const int steps = 12;

        var combined = new RolloutBuffer(steps * 2, 1, 1, streams: 2);
        var alone = new[] { new RolloutBuffer(steps, 1, 1), new RolloutBuffer(steps, 1, 1) };

        for (int t = 0; t < steps; t++)
        {
            for (int s = 0; s < 2; s++)
            {
                float value = random.NextGaussian(), reward = random.NextGaussian();
                bool terminated = random.NextSingle() < 0.1f;
                bool truncated = !terminated && random.NextSingle() < 0.1f;
                float bootstrap = random.NextGaussian();

                combined.Add([0f], [0f], 0f, value, reward, terminated, truncated, bootstrap);
                alone[s].Add([0f], [0f], 0f, value, reward, terminated, truncated, bootstrap);
            }
        }

        combined.ComputeAdvantages([0.5f, -0.25f], 0.99f, 0.95f);
        alone[0].ComputeAdvantages(0.5f, 0.99f, 0.95f);
        alone[1].ComputeAdvantages(-0.25f, 0.99f, 0.95f);

        for (int t = 0; t < steps; t++)
        {
            for (int s = 0; s < 2; s++)
            {
                Assert.Equal(alone[s].Advantages[t], combined.Advantages[t * 2 + s]);
                Assert.Equal(alone[s].Returns[t], combined.Returns[t * 2 + s]);
            }
        }
    }

    // --- Off-policy agents -------------------------------------------------------------------

    public static TheoryData<string> OffPolicy => ["Dqn", "Sac", "Td3"];

    [Theory]
    [MemberData(nameof(OffPolicy))]
    public void OffPolicyOneCopyReproducesTheSingleEnvironmentRunExactly(string algorithm)
    {
        // Small networks and an early start, so a short run takes hundreds of gradient steps and
        // any difference in draw order or update count would show.
        var options = new TrainingOptions { MaxSteps = 1_500, MaxEpisodes = int.MaxValue, Seed = 4 };

        TrainingReport single, batched;
        byte[] singleState, batchedState;

        if (algorithm == "Dqn")
        {
            var a = SmallDqn();
            single = Trainer.Train(new CartPoleEnvironment(), (IDiscreteAgent)a, options);
            var b = SmallDqn();
            batched = Trainer.Train(new DiscreteVectorEnvironment(() => new CartPoleEnvironment(), 1), b, options);
            (singleState, batchedState) = (Saved(a), Saved(b));
        }
        else
        {
            var a = SmallContinuous(algorithm);
            single = Trainer.Train(new PendulumEnvironment(), (IContinuousAgent)a, options);
            var b = SmallContinuous(algorithm);
            batched = Trainer.Train(new ContinuousVectorEnvironment(() => new PendulumEnvironment(), 1), b, options);
            (singleState, batchedState) = (Saved((Persistence.ICheckpointable)a), Saved((Persistence.ICheckpointable)b));
        }

        Assert.Equal(single.Returns, batched.Returns);
        Assert.True(single.Metrics!.UpdateCount > 100);
        Assert.Equal(single.Metrics.UpdateCount, batched.Metrics!.UpdateCount);
        Assert.Equal(singleState, batchedState);
    }

    [Theory]
    [MemberData(nameof(OffPolicy))]
    public void OffPolicyTrainsOncePerTransitionWhateverTheCopyCount(string algorithm)
    {
        // TrainFrequency counts transitions: eight copies for 1,600 steps take as many gradient
        // steps as one copy for 1,600, so the same options mean the same replay ratio. (A multiple
        // of eight, because a vector run always completes the vector step it is in.)
        var options = new TrainingOptions { MaxSteps = 1_600, MaxEpisodes = int.MaxValue, Seed = 4 };

        IAgent one, eight;
        if (algorithm == "Dqn")
        {
            var a = SmallDqn();
            Trainer.Train(new DiscreteVectorEnvironment(() => new CartPoleEnvironment(), 1), a, options);
            var b = SmallDqn();
            Trainer.Train(new DiscreteVectorEnvironment(() => new CartPoleEnvironment(), 8), b, options);
            (one, eight) = (a, b);
        }
        else
        {
            var a = SmallContinuous(algorithm);
            Trainer.Train(new ContinuousVectorEnvironment(() => new PendulumEnvironment(), 1), a, options);
            var b = SmallContinuous(algorithm);
            Trainer.Train(new ContinuousVectorEnvironment(() => new PendulumEnvironment(), 8), b, options);
            (one, eight) = (a, b);
        }

        Assert.Equal(1_600, eight.Metrics.StepCount);
        Assert.Equal(one.Metrics.UpdateCount, eight.Metrics.UpdateCount);
    }

    [Theory]
    [MemberData(nameof(OffPolicy))]
    public void BatchedGreedyActionsMatchOneAtATime(string algorithm)
    {
        // Seventy rows, past the networks' 64-row batch, so the chunked path is exercised too.
        const int count = 70;
        var random = new Random(9);

        if (algorithm == "Dqn")
        {
            var agent = SmallDqn();
            var observations = RandomObservations(random, count, 4);
            var actions = new int[count];
            agent.SelectActions(observations, actions, deterministic: true);

            for (int i = 0; i < count; i++)
                Assert.Equal(agent.SelectAction(observations.AsSpan(i * 4, 4), deterministic: true), actions[i]);
        }
        else
        {
            var agent = SmallContinuous(algorithm);
            var observations = RandomObservations(random, count, 3);
            var actions = new float[count];
            agent.SelectActions(observations, actions, deterministic: true);

            var one = new float[1];
            for (int i = 0; i < count; i++)
            {
                agent.SelectAction(observations.AsSpan(i * 3, 3), one, deterministic: true);
                Assert.Equal(one[0], actions[i]);
            }
        }
    }

    private static DqnAgent SmallDqn()
    {
        var environment = new CartPoleEnvironment();
        return new DqnAgent(
            environment.ObservationSpace, environment.ActionSpace,
            new DqnOptions { HiddenSizes = [32, 32], BatchSize = 64, LearningStarts = 200, TargetUpdateInterval = 100 },
            seed: 3);
    }

    private static IVectorContinuousAgent SmallContinuous(string algorithm)
    {
        var environment = new PendulumEnvironment();
        return algorithm == "Sac"
            ? new SacAgent(environment.ObservationSpace, environment.ActionSpace,
                new SacOptions { HiddenSizes = [32, 32], BatchSize = 64, LearningStarts = 200 }, seed: 3)
            : new Td3Agent(environment.ObservationSpace, environment.ActionSpace,
                new Td3Options { HiddenSizes = [32, 32], BatchSize = 64, LearningStarts = 200 }, seed: 3);
    }

    private static float[] RandomObservations(Random random, int count, int size)
    {
        var observations = new float[count * size];
        for (int i = 0; i < observations.Length; i++) observations[i] = random.NextSingle() * 2f - 1f;
        return observations;
    }

    private static byte[] Saved(Persistence.ICheckpointable agent)
    {
        using var stream = new MemoryStream();
        Persistence.Checkpoint.Save(stream, agent);
        return stream.ToArray();
    }
}
