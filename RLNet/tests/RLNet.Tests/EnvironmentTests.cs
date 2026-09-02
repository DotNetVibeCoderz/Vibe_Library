// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using RLNet.Environments;
using RLNet.Environments.Classic;
using RLNet.Environments.Control;
using RLNet.Environments.Domain;
using RLNet.Environments.MultiAgent;
using RLNet.Spaces;
using Xunit;

namespace RLNet.Tests;

/// <summary>Checks the properties every environment has to hold, across all of them at once.</summary>
public class EnvironmentTests
{
    public static TheoryData<string> DiscreteEnvironments() =>
        [.. Catalog.Environments.Where(e => e.Kind == EnvironmentKind.Discrete).Select(e => e.Name)];

    public static TheoryData<string> ContinuousEnvironments() =>
        [.. Catalog.Environments.Where(e => e.Kind == EnvironmentKind.Continuous).Select(e => e.Name)];

    /// <summary>
    /// The same seed must replay the same episode, exactly.
    /// </summary>
    /// <remarks>
    /// Reproducibility is the whole reason <c>Reset</c> takes a seed, and it is the first thing to
    /// break when an environment quietly reaches for a shared generator. Without it no result in
    /// the benchmark suite or the docs can be checked by anyone.
    /// </remarks>
    [Theory]
    [MemberData(nameof(DiscreteEnvironments))]
    public void Discrete_SameSeedReplaysTheSameEpisode(string name)
    {
        var first = RunScripted(Catalog.CreateDiscrete(name), seed: 4242);
        var second = RunScripted(Catalog.CreateDiscrete(name), seed: 4242);

        Assert.Equal(first, second);
    }

    [Theory]
    [MemberData(nameof(DiscreteEnvironments))]
    public void Discrete_DifferentSeedsDiverge(string name)
    {
        // GridWorld is deterministic by construction — a fixed start, a fixed layout — so it is
        // legitimately identical under any seed and is excluded rather than special-cased away.
        if (name == "GridWorld") return;

        var first = RunScripted(Catalog.CreateDiscrete(name), seed: 1);
        var second = RunScripted(Catalog.CreateDiscrete(name), seed: 999);

        Assert.NotEqual(first, second);
    }

    [Theory]
    [MemberData(nameof(DiscreteEnvironments))]
    public void Discrete_ObservationsStayInsideTheirSpace(string name)
    {
        var environment = Catalog.CreateDiscrete(name);
        environment.Reset(7);

        var space = environment.ObservationSpace;

        for (int step = 0; step < 2_000; step++)
        {
            Assert.Equal(space.FlatSize, environment.Observation.Length);

            foreach (float value in environment.Observation)
                Assert.True(float.IsFinite(value), $"{name} published a non-finite observation at step {step}.");

            var result = environment.Step(step % environment.ActionSpace.Count);
            Assert.True(float.IsFinite(result.Reward), $"{name} published a non-finite reward at step {step}.");

            if (result.Done) environment.Reset();
        }
    }

    /// <summary>
    /// An episode must never report both flags, and must never run past its own limit.
    /// </summary>
    /// <remarks>
    /// Terminated and truncated mean opposite things to a bootstrapping agent, so an environment
    /// that sets both leaves every agent to guess. The step limit is checked in the same test
    /// because both are enforced by the same code in <see cref="EnvironmentBase"/>.
    /// </remarks>
    [Theory]
    [MemberData(nameof(DiscreteEnvironments))]
    public void Discrete_TerminationAndTruncationAreExclusive(string name)
    {
        var environment = Catalog.CreateDiscrete(name);
        environment.Reset(13);

        int steps = 0;
        for (int i = 0; i < 5_000; i++)
        {
            var result = environment.Step(i % environment.ActionSpace.Count);
            steps++;

            Assert.False(result.Terminated && result.Truncated,
                $"{name} reported termination and truncation on the same step.");

            if (result.Truncated)
                Assert.Equal(environment.MaxEpisodeSteps, steps);

            if (result.Done)
            {
                Assert.True(
                    environment.MaxEpisodeSteps == 0 || steps <= environment.MaxEpisodeSteps,
                    $"{name} ran {steps} steps past its limit of {environment.MaxEpisodeSteps}.");

                environment.Reset();
                steps = 0;
            }
        }
    }

    [Theory]
    [MemberData(nameof(ContinuousEnvironments))]
    public void Continuous_ClampsOutOfRangeActions(string name)
    {
        var environment = Catalog.CreateContinuous(name);
        environment.Reset(5);

        // An untrained policy proposes absurd actions routinely. The environment clamps rather
        // than throwing, matching Gym, so this must not blow up or produce NaN.
        var wild = new float[environment.ActionSpace.FlatSize];
        Array.Fill(wild, 1e6f);

        for (int i = 0; i < 100; i++)
        {
            var result = environment.Step(wild);
            Assert.True(float.IsFinite(result.Reward));
            foreach (float value in environment.Observation) Assert.True(float.IsFinite(value));
            if (result.Done) environment.Reset();
        }
    }

    [Theory]
    [MemberData(nameof(ContinuousEnvironments))]
    public void Continuous_SameSeedReplaysTheSameEpisode(string name)
    {
        static List<float> Run(IContinuousEnvironment environment)
        {
            environment.Reset(321);
            var action = new float[environment.ActionSpace.FlatSize];
            var trace = new List<float>();

            for (int i = 0; i < 200; i++)
            {
                // A fixed action script, so any divergence is the environment's, not the policy's.
                for (int j = 0; j < action.Length; j++) action[j] = MathF.Sin(i * 0.1f + j);

                var result = environment.Step(action);
                trace.Add(result.Reward);
                trace.AddRange(environment.Observation.ToArray());
                if (result.Done) environment.Reset();
            }
            return trace;
        }

        Assert.Equal(
            Run(Catalog.CreateContinuous(name)),
            Run(Catalog.CreateContinuous(name)));
    }

    [Fact]
    public void Pendulum_NeverTerminates()
    {
        // Pendulum has no terminal state at all, which makes it the environment where bootstrapping
        // through truncation matters most. If this ever starts terminating, SAC's targets silently
        // become wrong.
        var environment = new PendulumEnvironment();
        environment.Reset(1);

        var action = new float[] { 0f };
        for (int i = 0; i < 199; i++)
        {
            var result = environment.Step(action);
            Assert.False(result.Terminated);
            Assert.False(result.Truncated);
        }

        var last = environment.Step(action);
        Assert.True(last.Truncated);
        Assert.False(last.Terminated);
    }

    [Fact]
    public void CartPole_TerminatesWhenPushedOneWay()
    {
        // Pushing right every step must topple the pole well inside the step limit. A CartPole
        // that survives this is not simulating anything.
        var environment = new CartPoleEnvironment();
        environment.Reset(1);

        for (int i = 0; i < 500; i++)
        {
            if (environment.Step(1).Terminated)
            {
                Assert.True(i < 100, $"CartPole survived {i} steps of constant push.");
                return;
            }
        }

        Assert.Fail("CartPole never terminated under a constant push.");
    }

    [Fact]
    public void GridWorld_GoalTerminatesWithReward()
    {
        var environment = new GridWorldEnvironment();
        environment.Reset(1);

        // Down the left column, then along the bottom row: a trap-free route to the goal.
        for (int i = 0; i < 4; i++) Assert.False(environment.Step(1).Done);

        StepResult result = default;
        for (int i = 0; i < 4; i++) result = environment.Step(3);

        Assert.True(result.Terminated);
        Assert.Equal(10f, result.Reward, 3);
    }

    [Fact]
    public void SupplyChain_BaseStockPolicyTurnsAProfit()
    {
        // The documented baseline has to actually be a baseline. If the base-stock policy loses
        // money, comparing a learned policy against it says nothing.
        var environment = new SupplyChainEnvironment();
        environment.Reset(2024);

        for (int i = 0; i < 180; i++)
            if (environment.Step(environment.BaseStockAction()).Done) break;

        Assert.True(environment.CumulativeProfit > 0f,
            $"Base-stock policy lost money: {environment.CumulativeProfit:F1}");
    }

    [Fact]
    public void PredatorPrey_CaptureNeedsTwoPredators()
    {
        // The defining property of the task. A single predator standing on the prey must not
        // score the capture reward, or the environment is not a coordination problem at all.
        var environment = new PredatorPreyEnvironment(gridSize: 9, predatorCount: 3);
        environment.Reset(11);

        var actions = new int[3];
        float best = float.MinValue;

        for (int step = 0; step < 200; step++)
        {
            for (int i = 0; i < 3; i++) actions[i] = 0; // everybody stays put

            var result = environment.Step(actions);
            foreach (float reward in environment.LastRewards) best = MathF.Max(best, reward);

            if (result.Done) break;
        }

        // Stationary predators cannot corner a fleeing prey, so no capture reward should appear.
        Assert.True(best < 5f, $"A capture was scored without coordination; best reward {best:F2}");
        Assert.Equal(0, environment.Captures);
    }

    [Fact]
    public void Trading_ObservationCarriesNoAbsolutePrice()
    {
        // The observation is scale-free by design, so an agent cannot memorise a price series.
        // Every component stays inside [-1, 1] whatever the price does.
        var environment = new TradingEnvironment();
        environment.Reset(5);

        for (int i = 0; i < 400; i++)
        {
            foreach (float value in environment.Observation)
                Assert.InRange(value, -1.001f, 1.001f);

            if (environment.Step(i % 3).Done) break;
        }
    }

    [Fact]
    public void Reacher_TargetIsAlwaysReachable()
    {
        var environment = new ReacherEnvironment();

        for (int episode = 0; episode < 50; episode++)
        {
            environment.Reset(episode);

            double distance = Math.Sqrt(
                environment.TargetX * environment.TargetX +
                environment.TargetY * environment.TargetY);

            // Arm reach is the sum of both links; a target outside it could never be reached and
            // the episode would be unwinnable by construction.
            Assert.True(distance <= 1.0, $"Target at distance {distance:F3} is outside the arm's reach.");
        }
    }

    private static List<float> RunScripted(IDiscreteEnvironment environment, int seed)
    {
        environment.Reset(seed);
        var trace = new List<float>();

        for (int i = 0; i < 300; i++)
        {
            var result = environment.Step(i % environment.ActionSpace.Count);
            trace.Add(result.Reward);
            trace.AddRange(environment.Observation.ToArray());
            if (result.Done) environment.Reset();
        }

        return trace;
    }
}
