# RLNet documentation

*Created by Gravicode Studios, led by Kang Fadhil.*

[Bahasa Indonesia](../id/README.md)

## Contents

| | |
|---|---|
| [01 — Getting started](01-getting-started.md) | Install, first training run, evaluation, saving a policy |
| [02 — Architecture](02-architecture.md) | How the pieces fit, and the two contracts that hold it together |
| [03 — Algorithms](03-algorithms.md) | What each of the six does, when to pick it, what to tune |
| [04 — Environments](04-environments.md) | All nine, what each teaches, and their reward structures |
| [05 — Neural engine](05-neural-network.md) | The MLP, SIMD, Adam, and why not PyTorch |
| [06 — GPU](06-gpu.md) | The optional backend, and when it is actually worth using |
| [07 — Multi-agent](07-multi-agent.md) | Simultaneous-move environments and independent learners |
| [08 — The console](08-console.md) | Reading the visualizer, and what each trace means |
| [09 — Benchmarks](09-benchmarks.md) | Measured throughput, and how to reproduce it |
| [10 — Extending](10-extending.md) | New environments, algorithms, buffers and backends |
| [11 — Troubleshooting](11-troubleshooting.md) | Why an agent is not learning, and how to tell which cause |

## The short version

RLNet is a reinforcement-learning library for .NET 10 with no external dependencies. Everything —
the environments, the neural network, the optimiser, the algorithms — is C# in this repository.

```csharp
var environment = Catalog.CreateDiscrete("CartPole");
var agent = Catalog.CreateAgent(Algorithm.Ppo, environment.ObservationSpace, environment.ActionSpace);
var report = Trainer.Train(environment, agent, new TrainingOptions { MaxSteps = 150_000 });
```

Three things are worth knowing before reading further, because they shape every API in the library:

1. **Termination and truncation are separate flags.** An episode ending because the agent crashed
   and an episode ending because it ran out of time need different bootstrap targets. See
   [architecture](02-architecture.md#termination-versus-truncation).

2. **Observations are spans into memory the environment reuses.** They are invalidated by the next
   step. Copy anything that must outlive it. See [architecture](02-architecture.md#the-observation-contract).

3. **Nothing on the hot path allocates.** Buffers are sized once at construction. This is why the
   library can do millions of steps without the garbage collector becoming the bottleneck.
