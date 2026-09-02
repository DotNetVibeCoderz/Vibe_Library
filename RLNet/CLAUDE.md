# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Scope

`RLNet/` is one project inside the **Vibe_Library** monorepo (the git root is one level up, alongside
`LVGL.Net`, `D3Net`, `ClassicML`, …). Keep all work inside `RLNet/`; do not touch sibling projects.

Monorepo conventions this project follows: workflows in `.github/workflows/` are path-scoped and
named after the single project they cover (`rlnet-ci.yml`, `rlnet-publish.yml`), and release tags are
namespaced (`RLNet-v0.1.0`) because a bare `v*` tag would be ambiguous across projects.

## Commands

```bash
dotnet build RLNet.slnx -c Release
dotnet run --project tests/RLNet.Tests -c Release            # all tests
dotnet run --project tests/RLNet.Tests -c Release -- --filter-class '*LearningTests*'
dotnet run --project src/RLNet.Visualizer -- --env Pendulum --algo Sac --start
dotnet run -c Release --project benchmarks/RLNet.Benchmarks -- --filter '*SimdBenchmarks*'
dotnet pack RLNet.slnx -c Release -o artifacts
```

**`dotnet test` does not work here.** The .NET 10 SDK dropped the VSTest bridge that xunit.v3's
Microsoft.Testing.Platform runner needs, and it fails with MSB4025 before running anything. The test
project is an executable — run it directly with `dotnet run`, which is also what CI does. `dotnet.config`
in the repo root exists for this; do not delete it assuming it is unused.

The full suite takes ~8 minutes because `LearningTests` trains real agents. `--filter-class` to skip
them while iterating.

## Architecture

Five projects. `RLNet.Core` is the library and the only one that matters for most work.

```
src/RLNet.Core/         the library (packs as NuGet id "RLNet", assembly RLNet.Core)
  Spaces/               DiscreteSpace, BoxSpace - environments self-describe
  Environments/         IEnvironment + EnvironmentBase, then Classic/ Control/ Domain/ MultiAgent/
  Neural/               MlpNetwork, DenseLayer, AdamOptimizer, IComputeBackend, QNetworkPair
  Buffers/              IReplayBuffer, Uniform, Prioritized (SumTree), RolloutBuffer (GAE)
  Policies/             CategoricalPolicy - shared by PPO and A2C
  Agents/               QTable, Dqn, A2C, Ppo, Sac, Td3, IndependentLearners, Schedule
  Training/             Trainer - the loop; TrainingReport
  Catalog.cs            name -> environment -> configured agent
src/RLNet.Gpu/          optional ILGPU backend, packs as "RLNet.Gpu"
src/RLNet.Visualizer/   Avalonia console (not packable)
tests/RLNet.Tests/      xunit.v3, ~80 tests
benchmarks/             BenchmarkDotNet
```

### Two contracts that explain most of the design

**1. Observations are spans over environment-owned memory, invalidated by the next step.**

```csharp
environment.Observation.CopyTo(buffer);   // capture BEFORE stepping
var step = environment.Step(action);
agent.Observe(buffer, action, step.Reward, environment.Observation, ...);
```

Getting this wrong produces an agent that trains but never improves, because every stored transition
says the state did not change. This is why nothing on the hot path allocates and why a full
forward-backward-update cycle is 0 bytes.

**2. `StepResult` carries `Terminated` and `Truncated` separately.**

Every bootstrap target reads `Terminated`, never `Done`. Termination means a real terminal state
(value genuinely 0); truncation means a step limit was hit while the episode was still viable (there
is a future, bootstrap through it). Pendulum has *no* terminal state, so it is the environment where
this matters most. `Done` is for loop control only.

`EnvironmentBase.Advance(reward, terminated)` owns the step counter and the time limit, so no
environment can forget to report truncation.

### Swappable seams

`IReplayBuffer`, `IComputeBackend` and `StateKeyEncoder` are taken by agents as constructor
arguments, never constructed inside them. Adding a buffer strategy or a compute device does not mean
forking an agent.

## Adding an environment

1. Derive from `DiscreteEnvironmentBase` / `ContinuousEnvironmentBase`; implement `OnReset`,
   `WriteObservation`, `OnStep`. Use the base's `Random` for everything stochastic or seeded replay
   breaks.
2. Add an entry to `Catalog.Entries`.
3. Add a `case` to `WorldView.Render` plus a `DrawXxx` method if it should be visible.

`EnvironmentTests` is driven by `Catalog`, so a registered environment automatically gets covered by
every property test — seeded determinism, observation bounds, flag exclusivity. No new test code.

Conventions worth matching: keep observations roughly in `[-1, 1]`; feed periodic quantities as
sine-cosine pairs (`Pendulum`, `Reacher`, `SupplyChain` all do).

## Testing philosophy

Backpropagation fails silently — a sign error produces a network that still trains, just worse. So:

- `NeuralNetworkTests` checks every analytic gradient against central finite differences.
- `QNetworkPairTests` checks `d min(Q1,Q2)/d action`, the signal SAC and TD3's actors ascend.
- `LearningTests` asserts each algorithm actually learns on a seeded short run — thresholds are
  deliberately loose, since the question is "did it learn at all", not "did it hit a published score".
- `GpuBackendTests` self-skip when no accelerator is present, and assert they got one first.

If you change anything under `Neural/`, run the gradient tests before anything else.

## Performance notes

`Vector<T>` throughout `SimdOps` (~8× over scalar, measured). Weights are row-major
`[inputSize, outputSize]` — a contract with the backends, since it is what makes all three passes
contiguous. The CPU backend is single-threaded on purpose: these networks finish a forward pass in
microseconds and thread-pool wakeup costs more than the work.

`Q1` and `Q2` in `QNetworkPair` are separate `MlpNetwork` instances with independent activation
caches, so evaluating one does not invalidate the other's — that is why `ActionGradientOfMinimum`
needs only one forward pass per critic. Same for the actor versus the critics in TD3.

## Visualizer

Training runs on the UI thread in time-budgeted slices (`TrainingSession.FrameBudget`), because the
renderer reads live environment state and a background thread would race it.

`DemoPresets.cs` deliberately uses lighter settings than the library defaults — SAC at its published
hyper-parameters runs at ~20 steps/sec, which is unwatchable. Do not "fix" these to match `Catalog`.

## Documentation

`docs/en/` and `docs/id/` are parallel — 11 files each, cross-linked. **Both must be updated
together**; the README is also bilingual in one file. `docs/images/` holds console screenshots,
reproducible via the visualizer's CLI flags.

Any performance or result figure quoted in the docs was measured. If you change something that moves
a number, re-measure rather than adjusting the prose.

## Attribution

Every source file carries `// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.` and
`Directory.Build.props` puts the same credit into assembly metadata and NuGet fields. Keep both when
adding files.
