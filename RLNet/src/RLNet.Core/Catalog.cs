// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using RLNet.Agents;
using RLNet.Environments;
using RLNet.Environments.Classic;
using RLNet.Environments.Control;
using RLNet.Environments.Domain;
using RLNet.Environments.MultiAgent;
using RLNet.Neural;
using RLNet.Spaces;

namespace RLNet;

/// <summary>Which family an environment belongs to.</summary>
public enum EnvironmentKind
{
    /// <summary>One agent, a finite action set.</summary>
    Discrete,

    /// <summary>One agent, a continuous action vector.</summary>
    Continuous,

    /// <summary>Several agents acting simultaneously.</summary>
    MultiAgent,
}

/// <summary>The learning algorithms RLNet ships.</summary>
public enum Algorithm
{
    /// <summary>Tabular Q-learning. Discrete observations only.</summary>
    QLearning,

    /// <summary>Deep Q-Network with double and dueling heads.</summary>
    Dqn,

    /// <summary>Advantage Actor-Critic.</summary>
    A2C,

    /// <summary>Proximal Policy Optimization.</summary>
    Ppo,

    /// <summary>Soft Actor-Critic. Continuous actions only.</summary>
    Sac,

    /// <summary>Twin Delayed DDPG. Continuous actions only.</summary>
    Td3,
}

/// <summary>One entry in the environment catalog.</summary>
/// <param name="Name">Stable identifier, also the display name.</param>
/// <param name="Kind">Which family it belongs to.</param>
/// <param name="Category">Grouping for the visualizer's picker.</param>
/// <param name="Description">One line explaining what the agent is being asked to do.</param>
public sealed record EnvironmentEntry(
    string Name,
    EnvironmentKind Kind,
    string Category,
    string Description)
{
    /// <summary>Builds a fresh instance.</summary>
    public required Func<object> Create { get; init; }

    /// <summary>Algorithms that can be pointed at this environment.</summary>
    public required Algorithm[] SupportedAlgorithms { get; init; }
}

/// <summary>
/// The registry of everything RLNet ships, and the shortest path from a name to a running agent.
/// </summary>
/// <remarks>
/// This is the ease-of-use surface. Without it, pairing an environment with an agent means
/// knowing that SAC needs a <see cref="BoxSpace"/>, that tabular Q-learning needs a
/// discretizer sized for the observation, and that PPO wants a rollout longer than an episode.
/// The <c>CreateAgent</c> overloads encode those decisions so that a first run is two lines, while
/// every constructor stays public for when the defaults are not what is wanted.
/// </remarks>
public static class Catalog
{
    private static readonly Algorithm[] DiscreteAlgorithms =
        [Algorithm.Dqn, Algorithm.Ppo, Algorithm.A2C, Algorithm.QLearning];

    private static readonly Algorithm[] ContinuousAlgorithms =
        [Algorithm.Sac, Algorithm.Td3];

    private static readonly EnvironmentEntry[] Entries =
    [
        new("GridWorld", EnvironmentKind.Discrete, "Classic",
            "Reach the goal without stepping in a trap. The smallest problem here, and the one to debug against.")
        {
            Create = () => new GridWorldEnvironment(),
            SupportedAlgorithms = DiscreteAlgorithms,
        },
        new("CartPole", EnvironmentKind.Discrete, "Classic",
            "Balance a pole by pushing the cart. The reference discrete benchmark; 475 counts as solved.")
        {
            Create = () => new CartPoleEnvironment(),
            SupportedAlgorithms = DiscreteAlgorithms,
        },
        new("MountainCar", EnvironmentKind.Discrete, "Classic",
            "Rock an underpowered car out of a valley. Reward is flat until the first success, so this is the exploration test.")
        {
            Create = () => new MountainCarEnvironment(),
            SupportedAlgorithms = DiscreteAlgorithms,
        },
        new("LunarLander", EnvironmentKind.Discrete, "Classic",
            "Land on the pad without crashing, spending fuel to do it.")
        {
            Create = () => new LunarLanderEnvironment(),
            SupportedAlgorithms = DiscreteAlgorithms,
        },
        new("Pendulum", EnvironmentKind.Continuous, "Control",
            "Swing up and hold, with torque too weak to lift directly. The continuous-control reference.")
        {
            Create = () => new PendulumEnvironment(),
            SupportedAlgorithms = ContinuousAlgorithms,
        },
        new("Reacher", EnvironmentKind.Continuous, "Robotics",
            "Drive a two-joint arm to a moving target. Most targets have two solutions, so the value function is multi-modal.")
        {
            Create = () => new ReacherEnvironment(),
            SupportedAlgorithms = ContinuousAlgorithms,
        },
        new("Trading", EnvironmentKind.Discrete, "Finance",
            "Trade a mean-reverting series. A teaching environment, not a trading system.")
        {
            Create = () => new TradingEnvironment(),
            SupportedAlgorithms = DiscreteAlgorithms,
        },
        new("SupplyChain", EnvironmentKind.Discrete, "Operations",
            "Reorder stock under seasonal demand and a three-day delivery lag.")
        {
            Create = () => new SupplyChainEnvironment(),
            SupportedAlgorithms = DiscreteAlgorithms,
        },
        new("PredatorPrey", EnvironmentKind.MultiAgent, "Multi-agent",
            "Three predators corner a fleeing prey. Capture needs two at once, so it cannot be solved alone.")
        {
            Create = () => new PredatorPreyEnvironment(),
            SupportedAlgorithms = [Algorithm.Dqn, Algorithm.Ppo, Algorithm.A2C],
        },
    ];

    /// <summary>Every registered environment.</summary>
    public static IReadOnlyList<EnvironmentEntry> Environments => Entries;

    /// <summary>Looks up an entry by name.</summary>
    public static EnvironmentEntry Find(string name) =>
        Entries.FirstOrDefault(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException(
            $"Unknown environment '{name}'. Known: {string.Join(", ", Entries.Select(e => e.Name))}.",
            nameof(name));

    /// <summary>Creates a single-agent discrete environment by name.</summary>
    public static IDiscreteEnvironment CreateDiscrete(string name) =>
        Find(name).Create() as IDiscreteEnvironment
        ?? throw new ArgumentException($"'{name}' is not a discrete single-agent environment.", nameof(name));

    /// <summary>Creates a single-agent continuous environment by name.</summary>
    public static IContinuousEnvironment CreateContinuous(string name) =>
        Find(name).Create() as IContinuousEnvironment
        ?? throw new ArgumentException($"'{name}' is not a continuous single-agent environment.", nameof(name));

    /// <summary>Creates a multi-agent environment by name.</summary>
    public static IMultiAgentEnvironment CreateMultiAgent(string name) =>
        Find(name).Create() as IMultiAgentEnvironment
        ?? throw new ArgumentException($"'{name}' is not a multi-agent environment.", nameof(name));

    /// <summary>
    /// Builds a discrete agent configured for an environment, with defaults chosen per task.
    /// </summary>
    public static IDiscreteAgent CreateAgent(
        Algorithm algorithm,
        Space observationSpace,
        DiscreteSpace actionSpace,
        int? seed = null,
        IComputeBackend? backend = null) => algorithm switch
        {
            Algorithm.QLearning => new QTableAgent(
                actionSpace,
                // A one-hot observation is a discrete state already; anything else has to be
                // bucketed, and a modest number of bins per dimension keeps the table populated
                // densely enough to actually learn from.
                observationSpace is BoxSpace box && !LooksOneHot(box)
                    ? StateDiscretizer.ForBox(box, DefaultBins(box))
                    : StateDiscretizer.OneHot(),
                seed: seed),

            Algorithm.Dqn => new DqnAgent(observationSpace, actionSpace, seed: seed, backend: backend),
            Algorithm.Ppo => new PpoAgent(observationSpace, actionSpace, seed: seed, backend: backend),
            Algorithm.A2C => new A2CAgent(observationSpace, actionSpace, seed: seed, backend: backend),

            Algorithm.Sac or Algorithm.Td3 => throw new ArgumentException(
                $"{algorithm} is a continuous-action algorithm; use the continuous overload.", nameof(algorithm)),

            _ => throw new ArgumentOutOfRangeException(nameof(algorithm)),
        };

    /// <summary>Builds a continuous agent configured for an environment.</summary>
    public static IContinuousAgent CreateAgent(
        Algorithm algorithm,
        Space observationSpace,
        BoxSpace actionSpace,
        int? seed = null,
        IComputeBackend? backend = null) => algorithm switch
        {
            Algorithm.Sac => new SacAgent(observationSpace, actionSpace, seed: seed, backend: backend),
            Algorithm.Td3 => new Td3Agent(observationSpace, actionSpace, seed: seed, backend: backend),

            _ => throw new ArgumentException(
                $"{algorithm} is a discrete-action algorithm; use the discrete overload.", nameof(algorithm)),
        };

    /// <summary>
    /// Detects the one-hot observations GridWorld publishes, which want an index rather than
    /// per-dimension bucketing.
    /// </summary>
    private static bool LooksOneHot(BoxSpace space)
    {
        if (space.FlatSize < 4) return false;
        for (int i = 0; i < space.FlatSize; i++)
            if (space.Low[i] != 0f || space.High[i] != 1f) return false;
        return true;
    }

    /// <summary>
    /// Bucket counts for tabular Q-learning on a continuous space.
    /// </summary>
    /// <remarks>
    /// Coarse on purpose. The table's size is the product of these, so a handful of dimensions at
    /// a dozen bins each is already past what a short run can visit often enough to learn from.
    /// Anything that needs finer resolution wants DQN, not more bins.
    /// </remarks>
    private static int[] DefaultBins(BoxSpace space)
    {
        var bins = new int[space.FlatSize];
        int perDimension = space.FlatSize switch
        {
            <= 2 => 20,
            <= 4 => 10,
            <= 6 => 6,
            _ => 4,
        };
        Array.Fill(bins, perDimension);
        return bins;
    }
}
