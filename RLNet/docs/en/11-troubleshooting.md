# Troubleshooting

[← Documentation index](README.md) · [Bahasa Indonesia](../id/11-pemecahan-masalah.md)

Reinforcement learning fails quietly. An agent with a broken update still runs, still reports a
loss, and still produces a curve — it just never gets better. This page is ordered by how often each
cause is the real one.

## Start here

Before tuning anything, run the same algorithm on **GridWorld**:

```csharp
var environment = new GridWorldEnvironment();
var agent = new QTableAgent(environment.ActionSpace, StateDiscretizer.OneHot(),
                            new QTableOptions { LearningRate = 0.2f });
Trainer.Train(environment, agent, new TrainingOptions { MaxEpisodes = 1_500 });

Console.WriteLine(Trainer.Evaluate(environment, agent, 20));   // should print ~9.3
```

GridWorld's optimal policy can be worked out by hand. **An agent that fails here has a bug, not a
hyper-parameter problem** — and that distinction saves more time than anything else on this page.

## "It trains but never improves"

### 1. The observation was captured after the step

By far the most common cause in a hand-written loop.

```csharp
// WRONG - `observation` is a live view, so by the time Observe sees it,
// it already holds the successor state. Every transition says nothing changed.
var observation = environment.Observation;
var step = environment.Step(action);
agent.Observe(observation, action, step.Reward, environment.Observation, ...);

// RIGHT
environment.Observation.CopyTo(buffer);     // copy BEFORE stepping
var step = environment.Step(action);
agent.Observe(buffer, action, step.Reward, environment.Observation, ...);
```

**Symptom:** the loss falls to near zero — the network learns the trivial mapping perfectly — while
the return never moves.

Use `Trainer.Train` and this cannot happen.

### 2. Truncation was treated as termination

```csharp
// WRONG
agent.Observe(obs, action, reward, next, step.Done, false);

// RIGHT
agent.Observe(obs, action, reward, next, step.Terminated, step.Truncated);
```

**Symptom:** the agent plateaus well below where it should, most visibly on Pendulum, which has no
terminal state at all. Every value estimate near the step limit is pulled toward zero, and the
policy learns to treat the time limit as a catastrophe.

See [architecture](02-architecture.md#termination-versus-truncation).

### 3. Schedules never moved

```csharp
agent.SetProgress(step / (float)totalSteps);   // required in a hand-written loop
```

**Symptom:** epsilon sits at 1.0 forever, so the agent is acting uniformly at random for the whole
run. Check the exploration trace in the console — a flat line at the top is this.

`Trainer` calls it for you.

### 4. The observation is badly scaled

The agents' default learning rates assume observations roughly in `[-1, 1]`. An observation in the
thousands needs a different learning rate for an otherwise identical problem.

**Symptom:** the loss is enormous from the first update, or the network outputs `NaN` within a few
hundred steps.

Normalise in `WriteObservation`. `Trading` and `SupplyChain` both do.

### 5. A periodic quantity went in raw

An angle, a phase, a day of the week. Feeding the raw value tells the network the two ends of the
cycle are maximally far apart, which is false.

**Symptom:** the agent learns most of the state space but behaves erratically near the wrap point.

Feed `[cos θ, sin θ]` instead. `Pendulum`, `Reacher` and `SupplyChain` all do.

## Reading the console traces

![The console's recorder stack during a healthy run](../images/console-cartpole.png)

| What you see | What it means |
|---|---|
| Return climbing, exploration falling, loss rising then levelling | Healthy. Loss rises early because the policy is reaching states the critic has never seen. |
| Loss near zero, return flat | The network has learned something trivially — usually cause 1 above. |
| Return climbing then collapsing | The step is too large. Lower the learning rate, or set `MaxGradientNorm`. For PPO, lower `TargetKl`. |
| Entropy collapsing to zero in the first few episodes | Premature convergence. Raise `EntropyCoefficient`. |
| Entropy never falling at all | The policy is not learning. Check causes 1-3. |
| Loss growing without bound | Diverging. Lower the learning rate; check `TargetUpdateInterval` is not too large for DQN. |
| Return is a flat line at exactly -200 on MountainCar | Normal, and the point of that environment — reward carries no signal until the first success. Give it prioritised replay and more steps. |

## Per-algorithm

### DQN will not learn

- **`LearningStarts` too low.** The first batches all come from one episode, which is nothing like a
  representative sample. 1,000 is a reasonable floor.
- **`TargetUpdateInterval` too large.** The target goes stale and the agent chases a fixed, wrong
  target. Too small and it diverges — 200 to 1,000 gradient steps is the usable band.
- **Exploration decayed too fast.** Check the trace; if epsilon reached its floor before the return
  started moving, widen the `fraction` on the schedule.
- **On MountainCar specifically**, switch on `PrioritizedReplay`. It is often the difference between
  learning and not.

### PPO plateaus immediately

- **The policy head was not initialised small.** RLNet passes `outputScale: 0.01` for this reason. A
  policy that starts sharp spends many rollouts unlearning an arbitrary preference.
- **`RolloutLength` too short.** Below a few hundred steps the advantage estimates are too noisy.
- **`TargetKl` too tight.** Check whether updates are stopping after one or two minibatches; if so,
  raise it or lower the learning rate.

### SAC or TD3 will not learn

- **The action scale is wrong.** Both agents work in `[-1, 1]` internally and rescale at the edge.
  If you wrote a custom environment, make sure `BoxSpace` bounds are the real ones.
- **`LearningStarts` too low.** Both fill the buffer with uniformly random actions first, on purpose:
  an untrained actor's output is not random so much as arbitrary, and covers the action space badly.
- **TD3 specifically:** `ExplorationNoise` is the one hyper-parameter it is genuinely sensitive to.
  Too little and it never finds anything; too much and it never exploits. Try SAC first, which
  learns its own.

### Tabular Q-learning will not learn on a continuous environment

The bins are wrong. Too few and different situations collapse into one entry; too many and no state
is visited twice.

```csharp
// CartPole: ignore the cart, resolve the pole finely.
StateDiscretizer.ForBox(box, [1, 1, 12, 6]);
```

If you find yourself wanting finer bins, that is the signal to switch to DQN.

## Performance

### Training is slower than expected

**Check `TrainFrequency` first.** A gradient step costs far more than an environment step, so a step
every 4 rather than every 1 roughly quadruples throughput.

Then network size: SAC's default is two 256-unit layers *and* one update touches an actor plus four
critics. `HiddenSizes = [64, 64]` and `BatchSize = 64` is around ten times faster and still learns
Pendulum — that is what the console uses.

Compare against the [benchmarks](09-benchmarks.md) to see whether your numbers are unusual.

### The GPU made it slower

Expected at classic-control network sizes. See [GPU](06-gpu.md) — the transfer cost does not shrink
as the network does, so below some device-dependent width the CPU backend simply wins. Run
`GpuBenchmarks` to find where that width is on your hardware.

### Memory keeps growing

Nothing on the hot path allocates, so this is almost always the replay buffer, which is exactly as
large as `BufferCapacity` asks:

```
bytes ≈ capacity × (2 × observationSize + actionSize + 2) × 4
```

A million transitions over an 8-dimensional observation is about 76 MB. Lower `BufferCapacity`.

If memory grows *without* bound, check your own `OnEpisode` callback — a list of every episode's
data is easy to accumulate by accident.

## Reproducing a failure

Seed everything, then it is a bug you can chase rather than a run you cannot repeat:

```csharp
var agent = new DqnAgent(obs, act, seed: 42);
var report = Trainer.Train(environment, agent, new TrainingOptions { Seed = 42 });
```

Environment and agent take separate seeds. `EnvironmentTests` asserts that the same seed replays the
same episode exactly, so if a seeded run is not reproducible, the environment is reaching for a
generator other than its own.

## Still stuck

1. Run the test suite — `dotnet run --project tests/RLNet.Tests -c Release`. If the gradient checks
   fail, the problem is below your code.
2. Try the same setup in the console. Watching 30 seconds of behaviour usually says more than a
   column of numbers.
3. Try a different algorithm on the same environment. If PPO learns and yours does not, the
   environment is fine.
