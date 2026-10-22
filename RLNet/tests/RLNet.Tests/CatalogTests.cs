// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using RLNet.Agents;
using RLNet.Environments;
using RLNet.Persistence;
using RLNet.Training;
using Xunit;

namespace RLNet.Tests;

/// <summary>
/// Every pairing the catalog advertises has to actually run — the console offers each of them in
/// its picker, so an advertised pair that throws is a broken menu item.
/// </summary>
public class CatalogTests
{
    public static TheoryData<string, Algorithm> SingleAgentPairs()
    {
        var data = new TheoryData<string, Algorithm>();
        foreach (var entry in Catalog.Environments.Where(e => e.Kind != EnvironmentKind.MultiAgent))
            foreach (var algorithm in entry.SupportedAlgorithms)
                data.Add(entry.Name, algorithm);
        return data;
    }

    [Theory]
    [MemberData(nameof(SingleAgentPairs))]
    public void EveryAdvertisedPairTrainsAndCheckpoints(string name, Algorithm algorithm)
    {
        var entry = Catalog.Find(name);
        IAgent agent;

        if (entry.Kind == EnvironmentKind.Discrete)
        {
            var environment = Catalog.CreateDiscrete(name);
            var discrete = Catalog.CreateAgent(algorithm, environment.ObservationSpace, environment.ActionSpace, seed: 1);
            Trainer.Train(environment, discrete, new TrainingOptions { MaxSteps = 300, MaxEpisodes = int.MaxValue, Seed = 1 });
            agent = discrete;
        }
        else
        {
            var environment = Catalog.CreateContinuous(name);
            var continuous = Catalog.CreateAgent(algorithm, environment.ObservationSpace, environment.ActionSpace, seed: 1);
            Trainer.Train(environment, continuous, new TrainingOptions { MaxSteps = 300, MaxEpisodes = int.MaxValue, Seed = 1 });
            agent = continuous;
        }

        Assert.Equal(300L, agent.Metrics.StepCount);

        // Every agent the catalog builds supports checkpoints.
        var checkpointable = Assert.IsAssignableFrom<ICheckpointable>(agent);
        using var stream = new MemoryStream();
        Checkpoint.Save(stream, checkpointable);
        Assert.True(stream.Length > 0);
    }

    [Fact]
    public void ContinuousEnvironmentsOfferTheOnPolicyAlgorithms()
    {
        foreach (var entry in Catalog.Environments.Where(e => e.Kind == EnvironmentKind.Continuous))
        {
            Assert.Contains(Algorithm.Ppo, entry.SupportedAlgorithms);
            Assert.Contains(Algorithm.A2C, entry.SupportedAlgorithms);
        }
    }

    [Fact]
    public void EveryAlgorithmButTabularQLearningHasABatchedForm()
    {
        foreach (var entry in Catalog.Environments)
        {
            if (entry.Kind == EnvironmentKind.MultiAgent) continue;

            foreach (var algorithm in entry.SupportedAlgorithms)
            {
                var vector = entry.Kind == EnvironmentKind.Discrete
                    ? (VectorEnvironment)Catalog.CreateDiscreteVector(entry.Name, 2)
                    : Catalog.CreateContinuousVector(entry.Name, 2);

                if (algorithm == Algorithm.QLearning)
                {
                    var discrete = (DiscreteVectorEnvironment)vector;
                    Assert.Throws<ArgumentException>(() =>
                        Catalog.CreateVectorAgent(algorithm, discrete.ObservationSpace, discrete.ActionSpace));
                    continue;
                }

                IAgent agent = vector switch
                {
                    DiscreteVectorEnvironment d => Catalog.CreateVectorAgent(algorithm, d.ObservationSpace, d.ActionSpace, seed: 1),
                    ContinuousVectorEnvironment c => Catalog.CreateVectorAgent(algorithm, c.ObservationSpace, c.ActionSpace, seed: 1),
                    _ => throw new InvalidOperationException(),
                };

                // A few hundred steps across two copies, so the off-policy agents pass
                // LearningStarts on the short environments and actually reach a gradient step.
                var options = new TrainingOptions { MaxSteps = 1_200, MaxEpisodes = int.MaxValue, Seed = 1 };
                var report = vector switch
                {
                    DiscreteVectorEnvironment d => Trainer.Train(d, (IVectorDiscreteAgent)agent, options),
                    ContinuousVectorEnvironment c => Trainer.Train(c, (IVectorContinuousAgent)agent, options),
                    _ => throw new InvalidOperationException(),
                };

                Assert.Equal(1_200, report.TotalSteps);
                Assert.Equal(1_200, agent.Metrics.StepCount);
            }
        }
    }
}
