# Getting started

[← Documentation index](README.md) · [Bahasa Indonesia](../id/01-memulai.md)

## Install

```bash
dotnet add package Gravicode.RLNet
```

Requires the .NET 10 SDK. There are no other dependencies — no native binaries, no Python, nothing
to install alongside it.

The GPU backend is a separate, optional package:

```bash
dotnet add package Gravicode.RLNet.Gpu   # only worth it for wide networks; see docs/en/06-gpu.md
```

## The shortest useful program

```csharp
using RLNet;
using RLNet.Training;

var environment = Catalog.CreateDiscrete("CartPole");
var agent = Catalog.CreateAgent(Algorithm.Ppo, environment.ObservationSpace, environment.ActionSpace, seed: 1);

var report = Trainer.Train(environment, agent, new TrainingOptions
{
    MaxSteps = 150_000,
    SolveThreshold = 475,
    Seed = 1,
});

Console.WriteLine(report);
Console.WriteLine($"Evaluation: {Trainer.Evaluate(environment, agent, episodes: 20)}");
```

```
333 episodes, 61,306 steps in 11.2s (5,494 steps/s), final average return 475.54, solved at episode 333
Evaluation: 500
```

The run stopped early because the trailing average passed the solve threshold. `Catalog` picked
reasonable defaults for PPO on this environment, so nothing had to be configured.

## Watching a run

`OnEpisode` fires after every completed episode. Keep it cheap — it runs inside the training loop.

```csharp
var report = Trainer.Train(environment, agent, new TrainingOptions
{
    MaxSteps = 150_000,
    OnEpisode = e =>
    {
        if (e.Episode % 25 == 0)
            Console.WriteLine($"ep {e.Episode,4}  return {e.Return,7:F1}  avg {e.AverageReturn,7:F1}");
    },
});
```

`AverageReturn` is the trailing mean over `WindowSize` episodes (100 by default). It is the number
worth watching: a single episode's return is noisy enough that it tells you almost nothing.

To stop early on your own condition:

```csharp
ShouldStop = () => DateTime.UtcNow > deadline,
```

## Evaluating properly

Training return and evaluation return are different numbers, and reporting the first as if it were
the second overstates a policy. An agent still exploring at 5% spends one step in twenty doing
something it already knows is wrong.

```csharp
float score = Trainer.Evaluate(environment, agent, episodes: 20, seed: 1234);
```

`Evaluate` runs with exploration off. Passing a seed makes the evaluation reproducible while still
giving each episode a different start.

## Saving and loading a policy

```csharp
float[] parameters = agent switch
{
    PpoAgent ppo => ppo.ExportParameters(),
    DqnAgent dqn => dqn.ExportParameters(),
    _ => throw new NotSupportedException(),
};

File.WriteAllBytes("policy.bin", MemoryMarshal.AsBytes(parameters.AsSpan()).ToArray());
```

To load, construct the same agent shape and import:

```csharp
var bytes = File.ReadAllBytes("policy.bin");
var restored = MemoryMarshal.Cast<byte, float>(bytes).ToArray();

var agent = new PpoAgent(environment.ObservationSpace, environment.ActionSpace);
agent.ImportParameters(restored);
```

`ImportParameters` throws if the length does not match, which catches the common mistake of loading
a policy into a differently-shaped network. Note this saves the *policy*, not the optimiser state
or the replay buffer — it is enough to run a trained agent, not to resume training exactly.

## Choosing an algorithm

| Situation | Start with |
|---|---|
| Small discrete state space, want the optimal policy | `QLearning` |
| Discrete actions, unfamiliar problem | `Ppo` |
| Discrete actions, want sample efficiency | `Dqn` |
| Discrete actions, want to see learning immediately | `A2C` |
| Continuous actions, first attempt | `Sac` |
| Continuous actions, willing to tune | `Td3` |

[Algorithms](03-algorithms.md) covers what each one is actually doing and what its knobs mean.

## Continuous control

The API is the same shape, with an action vector instead of an index:

```csharp
var environment = Catalog.CreateContinuous("Pendulum");
var agent = Catalog.CreateAgent(Algorithm.Sac, environment.ObservationSpace, environment.ActionSpace);

var report = Trainer.Train(environment, agent, new TrainingOptions { MaxSteps = 40_000 });
```

Pendulum's reward is a cost, so returns are negative and a good policy approaches zero from below.
Around -150 is solved; a policy that never swings up scores about -1200.

## Configuring an agent

`Catalog.CreateAgent` picks defaults. When you want control, construct directly — every constructor
is public and every option has a documented default:

```csharp
var agent = new DqnAgent(
    environment.ObservationSpace,
    environment.ActionSpace,
    new DqnOptions
    {
        HiddenSizes = [256, 256],
        LearningRate = 1e-4f,
        BatchSize = 128,
        TrainFrequency = 4,                              // gradient step every 4 env steps
        Epsilon = Schedule.Linear(1f, 0.02f, 0.4f),      // decay over the first 40% of training
        PrioritizedReplay = true,
    },
    seed: 42);
```

`TrainFrequency` is the biggest lever on wall-clock speed. A gradient step costs far more than an
environment step, so raising it from 1 to 4 roughly quadruples throughput at some cost in sample
efficiency.

## Writing your own loop

`Trainer` exists so you do not have to, but if you need the control:

```csharp
var observation = new float[environment.ObservationSpace.FlatSize];
environment.Reset(seed: 1);

for (long step = 0; step < 100_000; step++)
{
    agent.SetProgress(step / 100_000f);          // drives epsilon and LR schedules

    environment.Observation.CopyTo(observation);  // COPY, before stepping
    int action = agent.SelectAction(observation);
    var result = environment.Step(action);

    agent.Observe(observation, action, result.Reward, environment.Observation,
                  result.Terminated, result.Truncated);   // both flags, separately

    if (result.Done)
    {
        agent.OnEpisodeEnd();
        environment.Reset();
    }
}
```

Three things to get right, all covered in [architecture](02-architecture.md):

1. Copy the observation **before** stepping — the environment overwrites its buffer.
2. Pass `Terminated` and `Truncated` separately — never collapse them into one flag.
3. Call `SetProgress` — without it, no schedule ever moves.

## Next

- [Architecture](02-architecture.md) — why the API looks like this
- [Environments](04-environments.md) — what all nine are for
- [The console](08-console.md) — watching a run instead of reading numbers
