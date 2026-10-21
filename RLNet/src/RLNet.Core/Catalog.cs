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

    /// <summary>Advantage Actor-Critic. Discrete or continuous actions.</summary>
    A2C,

    /// <summary>Proximal Policy Optimization. Discrete or continuous actions.</summary>
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
        [Algorithm.Sac, Algorithm.Td3, Algorithm.Ppo, Algorithm.A2C];

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

    /// <summary>
    /// Creates <paramref name="count"/> copies of a discrete environment, stepped together, for
    /// training a batched agent from <see cref="CreateVectorAgent(Algorithm, Space, DiscreteSpace, int?, IComputeBackend?)"/>.
    /// </summary>
    public static DiscreteVectorEnvironment CreateDiscreteVector(string name, int count, bool parallel = false) =>
        new(() => CreateDiscrete(name), count, parallel);

    /// <summary>Creates <paramref name="count"/> copies of a continuous environment, stepped together.</summary>
    public static ContinuousVectorEnvironment CreateContinuousVector(string name, int count, bool parallel = false) =>
        new(() => CreateContinuous(name), count, parallel);

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
            Algorithm.Ppo or Algorithm.A2C => CreateVectorAgent(algorithm, observationSpace, actionSpace, seed, backend),

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
            Algorithm.Ppo or Algorithm.A2C => CreateVectorAgent(algorithm, observationSpace, actionSpace, seed, backend),

            _ => throw new ArgumentException(
                $"{algorithm} is a discrete-action algorithm; use the discrete overload.", nameof(algorithm)),
        };

    /// <summary>
    /// Builds a batched discrete agent — PPO or A2C — for a <see cref="DiscreteVectorEnvironment"/>.
    /// It is also an ordinary <see cref="IDiscreteAgent"/>, so it evaluates on a single copy.
    /// </summary>
    public static IVectorDiscreteAgent CreateVectorAgent(
        Algorithm algorithm,
        Space observationSpace,
        DiscreteSpace actionSpace,
        int? seed = null,
        IComputeBackend? backend = null) => algorithm switch
        {
            Algorithm.Ppo => new PpoAgent(observationSpace, actionSpace, seed: seed, backend: backend),
            Algorithm.A2C => new A2CAgent(observationSpace, actionSpace, seed: seed, backend: backend),

            _ => throw new ArgumentException(
                $"{algorithm} learns off-policy from a replay buffer and is not batched; only PPO and A2C are.", nameof(algorithm)),
        };

    /// <summary>
    /// Builds a batched continuous agent — PPO or A2C — for a <see cref="ContinuousVectorEnvironment"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both normalise observations and rewards and drop the entropy bonus, which is what continuous
    /// control needs from them: a Gaussian policy already explores through its spread, and reward
    /// scales here range from Reacher's fractions to Pendulum's -16 per step.
    /// </para>
    /// <para>
    /// A2C additionally uses a short horizon (γ = 0.9, λ = 0.9). Without a trust region its
    /// gradient is noisy, and a shorter horizon is the cheapest way to cut that noise — on Reacher
    /// one seed reached -31 with it against -141 without. It remains seed-sensitive (-28 to -125
    /// across six seeds); PPO is the dependable choice for continuous actions.
    /// </para>
    /// </remarks>
    public static IVectorContinuousAgent CreateVectorAgent(
        Algorithm algorithm,
        Space observationSpace,
        BoxSpace actionSpace,
        int? seed = null,
        IComputeBackend? backend = null) => algorithm switch
        {
            Algorithm.Ppo => new ContinuousPpoAgent(
                observationSpace, actionSpace,
                new PpoOptions
                {
                    LearningRate = 1e-3f,
                    EntropyCoefficient = 0f,
                    NormalizeObservations = true,
                    NormalizeRewards = true,
                },
                seed, backend),

            Algorithm.A2C => new ContinuousA2CAgent(
                observationSpace, actionSpace,
                new A2COptions
                {
                    LearningRate = 1e-3f,
                    Gamma = 0.9f,
                    GaeLambda = 0.9f,
                    EntropyCoefficient = 0f,
                    NormalizeObservations = true,
                    NormalizeRewards = true,
                },
                seed, backend),

            _ => throw new ArgumentException(
                $"{algorithm} has no batched continuous form; only PPO and A2C do.", nameof(algorithm)),
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
