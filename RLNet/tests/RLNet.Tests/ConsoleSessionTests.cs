// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using RLNet.Agents;
using RLNet.Environments;
using RLNet.Environments.MultiAgent;
using RLNet.Training;
using RLNet.Visualizer;
using Xunit;

namespace RLNet.Tests;

/// <summary>
/// The console's run controls — watch, save, load, metrics log — exercised through
/// <see cref="TrainingSession"/> directly, which has no UI dependency.
/// </summary>
/// <remarks>
/// The property that matters most is <see cref="WatchingLeavesTheTrainingRunUntouched"/>: switching
/// to watch mode mid-episode and back must resume training exactly as if nobody had looked.
/// Watching on the training environment itself would splice two trajectories into one rollout,
/// and the agent would quietly learn from a transition that never happened.
/// </remarks>
public sealed class ConsoleSessionTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("rlnet-console-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    public static TheoryData<string, Algorithm> Pairs => new()
    {
        { "CartPole", Algorithm.Ppo },
        { "CartPole", Algorithm.A2C },
        { "CartPole", Algorithm.Dqn },
        { "CartPole", Algorithm.QLearning },
        { "Pendulum", Algorithm.Sac },
        { "Pendulum", Algorithm.Td3 },
        { "Pendulum", Algorithm.Ppo },
        { "PredatorPrey", Algorithm.Dqn },
    };

    [Theory]
    [MemberData(nameof(Pairs))]
    public void WatchingLeavesTheTrainingRunUntouched(string environment, Algorithm algorithm)
    {
        var uninterrupted = Create(environment, algorithm);
        Run(uninterrupted, 3_000);

        var watched = Create(environment, algorithm);
        Run(watched, 1_500);
        watched.SetWatching(true);
        Run(watched, 700);
        watched.SetWatching(false);
        Run(watched, 1_500);

        Assert.Equal(uninterrupted.TotalSteps, watched.TotalSteps);
        Assert.Equal(uninterrupted.Episode, watched.Episode);
        Assert.Equal(uninterrupted.EpisodeReturn, watched.EpisodeReturn);
        Assert.Equal(File.ReadAllBytes(Save(uninterrupted, "a")), File.ReadAllBytes(Save(watched, "b")));
    }

    [Fact]
    public void WatchModeRunsOnItsOwnCopy()
    {
        var session = Create("CartPole", Algorithm.Ppo);
        Run(session, 50);
        object training = session.Environment;

        session.SetWatching(true);
        Assert.NotSame(training, session.Environment);
        Assert.Equal(0, session.EpisodeSteps);

        Run(session, 10);
        Assert.Equal(50, session.TotalSteps); // watched steps are not training steps
        Assert.Equal(10, session.EpisodeSteps);

        session.SetWatching(false);
        Assert.Same(training, session.Environment);
    }

    [Fact]
    public void LoadPicksUpTheAgentsStepCountAndStartsAFreshRecord()
    {
        var trained = Create("CartPole", Algorithm.Dqn);
        Run(trained, 4_000);
        string path = Save(trained, "dqn");

        var fresh = Create("CartPole", Algorithm.Dqn);
        Run(fresh, 300);
        fresh.Load(path);

        // The step count drives the console's schedules, so epsilon carries on from where it was
        // saved rather than resetting to full exploration — and reads correctly straight away.
        Assert.Equal(4_000, fresh.TotalSteps);
        Assert.Equal(trained.Metrics.Epsilon, fresh.Metrics.Epsilon);
        Assert.True(fresh.Metrics.Epsilon < 0.99f);
        Assert.Equal(0, fresh.Episode);
        Assert.Equal(0, fresh.ReturnTrace.Count);
        Assert.Equal(0, fresh.EpisodeSteps);
    }

    [Fact]
    public void LoadIntoADifferentAlgorithmIsRefusedAndChangesNothing()
    {
        string path = Save(Create("CartPole", Algorithm.Ppo), "ppo");

        var session = Create("CartPole", Algorithm.Dqn);
        Run(session, 500);
        string before = Save(session, "before");

        Assert.Throws<InvalidDataException>(() => session.Load(path));
        Assert.Equal(500, session.TotalSteps);
        Assert.Equal(File.ReadAllBytes(before), File.ReadAllBytes(Save(session, "after")));
    }

    [Fact]
    public void CatalogCheckpointDoesNotFitTheConsolePresetButFitsACatalogAgent()
    {
        // What the console's LOAD falls back on. SAC at Catalog settings is two 256-unit layers
        // and the console's preset is far narrower, so a policy trained in code is refused by the
        // console agent and accepted by a Catalog-built one, which then acts identically.
        var environment = Catalog.CreateContinuous("Pendulum");
        var agent = Catalog.CreateAgent(Algorithm.Sac, environment.ObservationSpace, environment.ActionSpace, seed: 1);
        string path = Path.Combine(_folder, "catalog.rlnet");
        Persistence.Checkpoint.Save(path, (Persistence.ICheckpointable)agent);

        Assert.Throws<InvalidDataException>(() => Create("Pendulum", Algorithm.Sac).Load(path));

        var catalogEnvironment = Catalog.CreateContinuous("Pendulum");
        var catalogAgent = Catalog.CreateAgent(Algorithm.Sac, catalogEnvironment.ObservationSpace, catalogEnvironment.ActionSpace, seed: 2);
        var session = new TrainingSession(catalogEnvironment, catalogAgent, "SAC", () => Catalog.CreateContinuous("Pendulum"));
        session.Load(path);

        var observation = new float[catalogEnvironment.ObservationSpace.FlatSize];
        float[] expected = new float[1], actual = new float[1];
        var random = new Random(5);
        for (int i = 0; i < 50; i++)
        {
            for (int j = 0; j < observation.Length; j++) observation[j] = random.NextSingle() * 2f - 1f;
            agent.SelectAction(observation, expected, deterministic: true);
            catalogAgent.SelectAction(observation, actual, deterministic: true);
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void LogGetsOneRowPerTrainingEpisodeAndNoneWhileWatching()
    {
        var session = Create("CartPole", Algorithm.Ppo);
        string path = Path.Combine(_folder, "run.csv");
        session.Log = MetricsLog.Create(path);

        Run(session, 2_000);
        int episodes = session.Episode;
        session.SetWatching(true);
        Run(session, 2_000);
        Assert.True(session.WatchEpisodes > 0);
        session.Dispose();

        var lines = File.ReadAllLines(path);
        Assert.Equal(episodes + 1, lines.Length); // header + one row per training episode
        var last = lines[^1].Split(',');
        Assert.Equal(episodes.ToString(), last[0]);
        Assert.True(long.Parse(last[4]) <= 2_000);
    }

    [Fact]
    public void ReplacingTheLogClosesThePreviousFile()
    {
        var session = Create("CartPole", Algorithm.Ppo);
        string first = Path.Combine(_folder, "first.csv");
        session.Log = MetricsLog.Create(first);
        Run(session, 500);

        session.Log = null;

        // Closed, so it can be opened exclusively — and everything written made it to disk.
        using (var stream = new FileStream(first, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Assert.True(stream.Length > 0);
        Assert.Equal(session.Episode + 1, File.ReadAllLines(first).Length);
    }

    private static TrainingSession Create(string environmentName, Algorithm algorithm)
    {
        var entry = Catalog.Find(environmentName);
        Func<object> copy = entry.Create;

        var session = entry.Kind switch
        {
            EnvironmentKind.Discrete => Discrete((IDiscreteEnvironment)entry.Create()),
            EnvironmentKind.Continuous => Continuous((IContinuousEnvironment)entry.Create()),
            _ => MultiAgent((IMultiAgentEnvironment)entry.Create()),
        };

        // Whole slices, never cut short by the frame budget, so a run is a fixed number of steps.
        session.FrameBudget = TimeSpan.MaxValue;
        session.Start();
        return session;

        TrainingSession Discrete(IDiscreteEnvironment environment) => new(
            environment,
            DemoPresets.CreateDiscrete(algorithm, environment.ObservationSpace, environment.ActionSpace, TrainingSession.Seed),
            algorithm.ToString(), copy);

        TrainingSession Continuous(IContinuousEnvironment environment) => new(
            environment,
            DemoPresets.CreateContinuous(algorithm, environment.ObservationSpace, environment.ActionSpace, TrainingSession.Seed),
            algorithm.ToString(), copy);

        TrainingSession MultiAgent(IMultiAgentEnvironment environment) => new(
            environment,
            IndependentLearners.ShareParameters(
                DemoPresets.CreateDiscrete(algorithm, environment.ObservationSpace, environment.ActionSpace, TrainingSession.Seed),
                environment.AgentCount, environment.ObservationSpace.FlatSize),
            algorithm.ToString(), copy);
    }

    private static void Run(TrainingSession session, int steps)
    {
        session.StepsPerFrame = steps;
        Assert.Equal(steps, session.Advance());
    }

    private string Save(TrainingSession session, string name)
    {
        string path = Path.Combine(_folder, name + ".rlnet");
        session.Save(path);
        return path;
    }
}
