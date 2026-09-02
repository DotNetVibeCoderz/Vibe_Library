# Extending RLNet

[← Documentation index](README.md) · [Bahasa Indonesia](../id/10-memperluas.md)

Every seam in the library is a public interface. This page covers the four things worth extending.

## A new environment

Derive from `DiscreteEnvironmentBase` or `ContinuousEnvironmentBase`. The base class owns the
observation buffer, the seeded generator, the step counter and the time limit — so no environment
can forget to report truncation, which is exactly the bug the terminated/truncated split exists to
prevent.

```csharp
using RLNet.Environments;
using RLNet.Spaces;

public sealed class ThermostatEnvironment : DiscreteEnvironmentBase
{
    private double _temperature;
    private double _target;

    public ThermostatEnvironment() : base(
        // Labels are optional but the console uses them, and they cost nothing.
        new BoxSpace([-1f, -1f, 0f], [1f, 1f, 1f],
                     ["Temperature error", "Rate of change", "Heater state"]),
        new DiscreteSpace(3, ["Off", "Low", "High"]),
        maxEpisodeSteps: 500)
    {
        Reset();
    }

    public override string Name => "Thermostat";

    protected override void OnReset()
    {
        // `Random` is the base class's seeded generator. Use it for everything stochastic, or
        // Reset(seed) will not actually reproduce an episode.
        _temperature = Random.NextRange(15f, 25f);
        _target = 21.0;
    }

    protected override void WriteObservation(Span<float> destination)
    {
        destination[0] = (float)Math.Clamp((_temperature - _target) / 10.0, -1, 1);
        destination[1] = /* rate of change */ 0f;
        destination[2] = /* heater state */ 0f;
    }

    protected override StepResult OnStep(int action)
    {
        _temperature += action * 0.1 - 0.05;   // heat in, ambient loss out

        float error = (float)Math.Abs(_temperature - _target);
        float reward = -error - action * 0.01f;   // comfort, minus energy

        bool terminated = _temperature < 0 || _temperature > 40;   // a real failure

        // Advance handles the counter, the time limit and the truncation flag.
        return Advance(reward, terminated);
    }
}
```

### Getting the details right

**Observation scaling.** Keep values roughly in `[-1, 1]`. The agents' default learning rates assume
it, and an observation in the thousands will need a different rate for an otherwise identical
problem. `Trading` and `SupplyChain` both normalise for exactly this reason.

**Periodic quantities go in as sine-cosine pairs.** An angle, a day of the week, a phase — feeding
the raw value tells the network that the two ends of the cycle are maximally far apart, which is
false, and it mostly fails to learn otherwise. `Pendulum`, `Reacher` and `SupplyChain` all do this.

**Terminated means a real terminal state.** Not "the episode is over" — a step limit is
`Advance`'s job, and reporting it as termination teaches the agent that the world ends at the limit.
If your environment has no failure state at all, always pass `false`, like `Pendulum`.

**Expose whatever the renderer needs** as public properties. `WorldView` downcasts to the concrete
type and reads them.

### Registering it

Add an entry to `Catalog.Entries` so it appears in the console and in `Catalog.CreateDiscrete`:

```csharp
new("Thermostat", EnvironmentKind.Discrete, "Operations",
    "Hold a room at temperature without running the heater harder than needed.")
{
    Create = () => new ThermostatEnvironment(),
    SupportedAlgorithms = [Algorithm.Dqn, Algorithm.Ppo, Algorithm.A2C, Algorithm.QLearning],
},
```

Then add a `case` to `WorldView.Render` and a `DrawThermostat` method if you want it drawn.

Nothing requires registration — a bare class works fine with `Trainer` — but the console only shows
what the catalog knows about.

## A new replay buffer

```csharp
public interface IReplayBuffer
{
    int Capacity { get; }
    int Count { get; }
    int ObservationSize { get; }
    int ActionSize { get; }

    void Add(ReadOnlySpan<float> observation, ReadOnlySpan<float> action, float reward,
             ReadOnlySpan<float> nextObservation, bool terminated);
    void Sample(int batchSize, ReplayBatch batch, FastRandom random);
    void UpdatePriorities(ReadOnlySpan<int> indices, ReadOnlySpan<float> tdErrors);
    void Clear();
}
```

The easiest route is to derive from `UniformReplayBuffer`, which already handles circular storage as
a structure of arrays, and override the two methods that matter. `PrioritizedReplayBuffer` is that
pattern in about 100 lines:

```csharp
public sealed class DemonstrationBuffer(int capacity, int obs, int act)
    : UniformReplayBuffer(capacity, obs, act)
{
    private readonly UniformReplayBuffer _demonstrations = new(10_000, obs, act);

    /// <summary>Adds a transition from an expert, which is never evicted by ordinary experience.</summary>
    public void AddDemonstration(ReadOnlySpan<float> obs, ReadOnlySpan<float> act,
                                 float reward, ReadOnlySpan<float> next, bool terminated) =>
        _demonstrations.Add(obs, act, reward, next, terminated);

    public override void Sample(int batchSize, ReplayBatch batch, FastRandom random)
    {
        // A quarter of every batch from demonstrations, the rest from the agent's own experience.
        int expert = Math.Min(batchSize / 4, _demonstrations.Count);
        if (expert > 0) _demonstrations.Sample(expert, batch, random);
        base.Sample(batchSize - expert, batch, random);
    }
}
```

Then pass it in — no agent needs changing:

```csharp
var agent = new DqnAgent(obs, act, buffer: new DemonstrationBuffer(100_000, 4, 1));
```

`OnAdded(int slot)` is the hook for per-slot bookkeeping; it is called before the head advances.

## A new compute backend

Implement `IComputeBackend` — two methods, the forward and backward of a dense layer:

```csharp
public interface IComputeBackend : IDisposable
{
    string Name { get; }
    bool IsAccelerated { get; }

    void DenseForward(ReadOnlySpan<float> weights, ReadOnlySpan<float> biases,
                      ReadOnlySpan<float> input, Span<float> output,
                      int batch, int inputSize, int outputSize, Activation activation);

    void DenseBackward(ReadOnlySpan<float> weights, ReadOnlySpan<float> input,
                       ReadOnlySpan<float> output, Span<float> gradOutput, Span<float> gradInput,
                       Span<float> weightGrad, Span<float> biasGrad,
                       int batch, int inputSize, int outputSize, Activation activation);
}
```

Three contracts to honour:

1. **Weights are row-major `[inputSize, outputSize]`.**
2. **`gradInput` may be empty** — that is the first layer of a network, and you can skip that
   product entirely.
3. **`weightGrad` and `biasGrad` accumulate**, they are not overwritten. `ZeroGradients` clears
   them between updates.

Verify against the CPU backend the way `GpuBackendTests` does: identical input through both,
agreement to about 1e-3 relative. A backend that is subtly wrong produces an agent that trains
slightly worse, which no benchmark will catch.

## A new algorithm

Implement `IDiscreteAgent` or `IContinuousAgent`. The interface is small; the work is the algorithm.

```csharp
public sealed class MyAgent : IDiscreteAgent
{
    public string Name => "MyAgent";
    public AgentMetrics Metrics { get; } = new();

    public int SelectAction(ReadOnlySpan<float> observation, bool deterministic = false) { ... }

    public void Observe(ReadOnlySpan<float> observation, int action, float reward,
                        ReadOnlySpan<float> nextObservation, bool terminated, bool truncated)
    {
        // Only `terminated` zeroes a bootstrap. Never `terminated || truncated`.
        float target = terminated ? reward : reward + gamma * ValueOf(nextObservation);
        ...
        Metrics.StepCount++;
    }

    public void OnEpisodeEnd() { }
    public void SetProgress(float progress) { /* move your schedules */ }
}
```

Things that are easy to get wrong, all of which the existing agents demonstrate:

- **Do not store the observation span.** It is invalidated by the next step. `Observe` receives
  a copy from the caller, but if you buffer it, copy again.
- **`deterministic` must actually suppress exploration.** It is what evaluation depends on.
- **Fill `Metrics`.** The console reads it, and `float.NaN` means "not meaningful for this
  algorithm" — which is how a chart knows not to draw a trace.
- **Read `SetProgress`.** Without it your schedules never move.

Read `A2CAgent` first — it is the shortest complete actor-critic here, and the same skeleton scales
up to PPO.

For a value-based algorithm, `MlpNetwork` gives you everything except the loss:

```csharp
_online = new MlpNetwork(obsSize, [128, 128], actionCount,
                         Activation.ReLU, Activation.Linear, batchSize, random);
_target = new MlpNetwork(obsSize, [128, 128], actionCount,
                         Activation.ReLU, Activation.Linear, batchSize, random);
_target.CopyFrom(_online);
```

## A new state encoder

For tabular agents, an encoder maps an observation to one integer key:

```csharp
public delegate long StateKeyEncoder(ReadOnlySpan<float> observation);
```

```csharp
// Only the two dimensions that matter, finely bucketed.
StateKeyEncoder poleOnly = observation =>
    StateDiscretizer.Bucket(observation[2], -0.21f, 0.21f, 24) * 12 +
    StateDiscretizer.Bucket(observation[3], -3f, 3f, 12);

var agent = new QTableAgent(actionSpace, poleOnly);
```

Keys are packed into a `long` as a mixed-radix number rather than built as a string: a string key
allocates on every step and hashes by walking characters, while an integer key does neither and is
collision-free by construction rather than by hope.

## Testing your extension

The existing suite is the template:

| What you added | Test it like |
|---|---|
| Environment | `EnvironmentTests` — seeded determinism, observation bounds, flag exclusivity |
| Compute backend | `GpuBackendTests` — parity against the CPU backend |
| Algorithm | `LearningTests` — does it actually learn, on a seeded short run |
| Anything with a gradient | `NeuralNetworkTests` — finite differences |

`EnvironmentTests` is driven by `Catalog`, so a registered environment is covered by every property
test automatically — determinism, bounds, and the terminated/truncated contract — with no new test
code.

## Next

- [Architecture](02-architecture.md) — the contracts you are extending
- [Algorithms](03-algorithms.md) — how the existing six are built
