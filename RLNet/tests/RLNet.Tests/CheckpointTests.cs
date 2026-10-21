// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using RLNet.Agents;
using RLNet.Environments;
using RLNet.Environments.Classic;
using RLNet.Environments.Control;
using RLNet.Environments.MultiAgent;
using RLNet.Persistence;
using RLNet.Training;
using Xunit;

namespace RLNet.Tests;

/// <summary>
/// Checks that a checkpoint carries an agent's complete state, and that a mismatched one is
/// refused before anything is overwritten.
/// </summary>
/// <remarks>
/// The strongest check here is <see cref="ResumedRunMatchesAnUninterruptedOne"/>: save mid-run,
/// load into a fresh agent with a different seed, and continue both. If any piece of state is
/// missing — an optimiser moment, the generator, the schedule position — the two runs diverge.
/// </remarks>
public class CheckpointTests
{
    [Fact]
    public void ResumedRunMatchesAnUninterruptedOne()
    {
        var environment = new CartPoleEnvironment();
        var options = new PpoOptions { RolloutLength = 256, Epochs = 4 };

        // Exactly four rollouts, so nothing is left half-collected at the save.
        var original = new PpoAgent(environment.ObservationSpace, environment.ActionSpace, options, seed: 1);
        Trainer.Train(environment, original, new TrainingOptions { MaxSteps = 1_024, MaxEpisodes = int.MaxValue, Seed = 1 });

        var restored = new PpoAgent(environment.ObservationSpace, environment.ActionSpace, options, seed: 999);
        RoundTrip(original, restored);

        var continued = Trainer.Train(new CartPoleEnvironment(), original,
            new TrainingOptions { MaxSteps = 2_048, MaxEpisodes = int.MaxValue, Seed = 2 });
        var resumed = Trainer.Train(new CartPoleEnvironment(), restored,
            new TrainingOptions { MaxSteps = 2_048, MaxEpisodes = int.MaxValue, Seed = 2 });

        Assert.Equal(continued.Returns, resumed.Returns);
        Assert.Equal(original.ExportParameters(), restored.ExportParameters());
        Assert.Equal(original.Metrics.UpdateCount, restored.Metrics.UpdateCount);
    }

    [Fact]
    public void ContinuousOnPolicyAgentKeepsItsSpreadAndNormalizers()
    {
        // The normalisers and the free log-std live outside the actor network; a checkpoint that
        // missed either would still restore the network and still evaluate differently.
        var environment = new PendulumEnvironment();
        var options = new PpoOptions { RolloutLength = 400, Epochs = 2, NormalizeObservations = true, NormalizeRewards = true };

        var original = new ContinuousPpoAgent(environment.ObservationSpace, environment.ActionSpace, options, seed: 3);
        Trainer.Train(environment, original, new TrainingOptions { MaxSteps = 1_200, MaxEpisodes = int.MaxValue, Seed = 3 });

        var restored = new ContinuousPpoAgent(environment.ObservationSpace, environment.ActionSpace, options, seed: 4);
        RoundTrip(original, restored);

        var a = Trainer.Train(new PendulumEnvironment(), original, new TrainingOptions { MaxSteps = 1_200, MaxEpisodes = int.MaxValue, Seed = 5 });
        var b = Trainer.Train(new PendulumEnvironment(), restored, new TrainingOptions { MaxSteps = 1_200, MaxEpisodes = int.MaxValue, Seed = 5 });

        Assert.Equal(a.Returns, b.Returns);
    }

    public static TheoryData<string> AgentKinds() => ["QLearning", "Dqn", "A2C", "Sac", "Td3", "ContinuousA2C"];

    [Theory]
    [MemberData(nameof(AgentKinds))]
    public void EveryAgentEvaluatesIdenticallyAfterARoundTrip(string kind)
    {
        // Off-policy agents do not save their replay buffer, so their continued training is not
        // expected to match; what they act on — every network, the schedule — must.
        switch (kind)
        {
            case "QLearning":
            {
                var environment = new GridWorldEnvironment();
                QTableAgent Make(int seed) => new(environment.ActionSpace, StateDiscretizer.OneHot(), seed: seed);
                AssertDiscreteRoundTrip(environment, Make(1), Make(2), steps: 3_000);
                break;
            }
            case "Dqn":
            {
                var environment = new CartPoleEnvironment();
                DqnAgent Make(int seed) => new(environment.ObservationSpace, environment.ActionSpace,
                    new DqnOptions { HiddenSizes = [32, 32], LearningStarts = 200 }, seed: seed);
                AssertDiscreteRoundTrip(environment, Make(1), Make(2), steps: 1_500);
                break;
            }
            case "A2C":
            {
                var environment = new CartPoleEnvironment();
                A2CAgent Make(int seed) => new(environment.ObservationSpace, environment.ActionSpace, seed: seed);
                AssertDiscreteRoundTrip(environment, Make(1), Make(2), steps: 1_500);
                break;
            }
            case "Sac":
            {
                var environment = new PendulumEnvironment();
                SacAgent Make(int seed) => new(environment.ObservationSpace, environment.ActionSpace,
                    new SacOptions { HiddenSizes = [32, 32], BatchSize = 32, LearningStarts = 200 }, seed: seed);
                AssertContinuousRoundTrip(environment, Make(1), Make(2), steps: 600);
                break;
            }
            case "Td3":
            {
                var environment = new PendulumEnvironment();
                Td3Agent Make(int seed) => new(environment.ObservationSpace, environment.ActionSpace,
                    new Td3Options { HiddenSizes = [32, 32], BatchSize = 32, LearningStarts = 200 }, seed: seed);
                AssertContinuousRoundTrip(environment, Make(1), Make(2), steps: 600);
                break;
            }
            case "ContinuousA2C":
            {
                var environment = new ReacherEnvironment();
                ContinuousA2CAgent Make(int seed) => new(environment.ObservationSpace, environment.ActionSpace,
                    new A2COptions { NormalizeObservations = true }, seed: seed);
                AssertContinuousRoundTrip(environment, Make(1), Make(2), steps: 1_000);
                break;
            }
        }
    }

    [Fact]
    public void SacRestoresItsLearnedTemperature()
    {
        var environment = new PendulumEnvironment();
        var options = new SacOptions { HiddenSizes = [32, 32], BatchSize = 32, LearningStarts = 100 };

        var original = new SacAgent(environment.ObservationSpace, environment.ActionSpace, options, seed: 1);
        Trainer.Train(environment, original, new TrainingOptions { MaxSteps = 600, MaxEpisodes = int.MaxValue, Seed = 1 });

        var restored = new SacAgent(environment.ObservationSpace, environment.ActionSpace, options, seed: 2);
        RoundTrip(original, restored);

        Assert.NotEqual(options.Temperature, original.Temperature);
        Assert.Equal(original.Temperature, restored.Temperature);
    }

    [Fact]
    public void LoadIntoADifferentAgentTypeIsRefused()
    {
        var environment = new CartPoleEnvironment();
        var ppo = new PpoAgent(environment.ObservationSpace, environment.ActionSpace, seed: 1);
        var a2c = new A2CAgent(environment.ObservationSpace, environment.ActionSpace, seed: 1);

        using var stream = new MemoryStream();
        Checkpoint.Save(stream, ppo);
        stream.Position = 0;

        var error = Assert.Throws<InvalidDataException>(() => Checkpoint.Load(stream, a2c));
        Assert.Contains(nameof(PpoAgent), error.Message);
    }

    [Fact]
    public void SameSizeDifferentLayoutIsRefusedAndNothingIsOverwritten()
    {
        // [24, 12] and [12, 24] differ in parameter count here, but the shape check fires first
        // and names the network, which is the message worth having.
        var environment = new CartPoleEnvironment();
        var saved = new DqnAgent(environment.ObservationSpace, environment.ActionSpace,
            new DqnOptions { HiddenSizes = [24, 12] }, seed: 1);
        var target = new DqnAgent(environment.ObservationSpace, environment.ActionSpace,
            new DqnOptions { HiddenSizes = [12, 24] }, seed: 2);

        var before = target.ExportParameters();

        using var stream = new MemoryStream();
        Checkpoint.Save(stream, saved);
        stream.Position = 0;

        var error = Assert.Throws<InvalidDataException>(() => Checkpoint.Load(stream, target));
        Assert.Contains("online", error.Message);
        Assert.Equal(before, target.ExportParameters());
    }

    [Fact]
    public void ForeignBytesAreRefused()
    {
        var environment = new CartPoleEnvironment();
        var agent = new PpoAgent(environment.ObservationSpace, environment.ActionSpace, seed: 1);

        using var stream = new MemoryStream("definitely not a checkpoint"u8.ToArray());
        Assert.Throws<InvalidDataException>(() => Checkpoint.Load(stream, agent));
    }

    [Fact]
    public void SharedParameterLearnersSaveTheirAgentOnce()
    {
        var environment = new PredatorPreyEnvironment(gridSize: 5, predatorCount: 3);
        IndependentLearners Make(int seed) => IndependentLearners.ShareParameters(
            new DqnAgent(environment.ObservationSpace, environment.ActionSpace, new DqnOptions { HiddenSizes = [16] }, seed: seed),
            environment.AgentCount, environment.ObservationSpace.FlatSize);

        var original = Make(1);
        var restored = Make(2);

        using var stream = new MemoryStream();
        Checkpoint.Save(stream, original);
        stream.Position = 0;
        Checkpoint.Load(stream, restored);

        Assert.Equal(
            ((DqnAgent)original.Agents[0]).ExportParameters(),
            ((DqnAgent)restored.Agents[0]).ExportParameters());
    }

    [Fact]
    public void FileRoundTripWritesAtomically()
    {
        var environment = new CartPoleEnvironment();
        var agent = new A2CAgent(environment.ObservationSpace, environment.ActionSpace, seed: 1);
        var copy = new A2CAgent(environment.ObservationSpace, environment.ActionSpace, seed: 2);

        string directory = Directory.CreateTempSubdirectory("rlnet-checkpoint").FullName;
        try
        {
            string path = Path.Combine(directory, "agent.rlnet");
            Checkpoint.Save(path, agent);
            Checkpoint.Save(path, agent);   // overwrites in place
            Checkpoint.Load(path, copy);

            Assert.Equal(agent.ExportParameters(), copy.ExportParameters());
            Assert.Equal(["agent.rlnet"], Directory.GetFiles(directory).Select(Path.GetFileName));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void RoundTrip(ICheckpointable from, ICheckpointable to)
    {
        using var stream = new MemoryStream();
        Checkpoint.Save(stream, from);
        stream.Position = 0;
        Checkpoint.Load(stream, to);
    }

    private static void AssertDiscreteRoundTrip<TAgent>(IDiscreteEnvironment environment, TAgent original, TAgent restored, int steps)
        where TAgent : IDiscreteAgent, ICheckpointable
    {
        Trainer.Train(environment, original, new TrainingOptions { MaxSteps = steps, MaxEpisodes = int.MaxValue, Seed = 1 });
        RoundTrip(original, restored);

        Assert.Equal(
            Trainer.Evaluate(environment, original, episodes: 3, seed: 77),
            Trainer.Evaluate(environment, restored, episodes: 3, seed: 77));
        Assert.Equal(original.Metrics.StepCount, restored.Metrics.StepCount);
    }

    private static void AssertContinuousRoundTrip<TAgent>(IContinuousEnvironment environment, TAgent original, TAgent restored, int steps)
        where TAgent : IContinuousAgent, ICheckpointable
    {
        Trainer.Train(environment, original, new TrainingOptions { MaxSteps = steps, MaxEpisodes = int.MaxValue, Seed = 1 });
        RoundTrip(original, restored);

        Assert.Equal(
            Trainer.Evaluate(environment, original, episodes: 2, seed: 77),
            Trainer.Evaluate(environment, restored, episodes: 2, seed: 77));
        Assert.Equal(original.Metrics.StepCount, restored.Metrics.StepCount);
    }
}
