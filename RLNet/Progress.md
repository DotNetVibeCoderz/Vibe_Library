# Progress — RLNet

*Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.*

Development checklist. Newest status first. See [PLAN.md](PLAN.md) for the roadmap.

## Status

| Area | State | Verified by |
|---|---|---|
| Spaces, environment contract, truncation vs termination | Done | `EnvironmentTests` (catalog-driven: determinism, bounds, flag exclusivity) |
| SIMD neural engine, Adam | Done | `NeuralNetworkTests` (finite differences), vectorised Adam pinned against scalar reference |
| `QNetworkPair` action gradient | Done | `QNetworkPairTests` |
| Replay / rollout buffers (uniform, PER, GAE) | Done | buffer tests |
| Agents: Q-learning, DQN, A2C, PPO, SAC, TD3 | Done | `LearningTests` (seeded short runs) |
| Multi-agent (PredatorPrey, independent / shared learners) | Done | `LearningTests`, `EnvironmentTests` |
| Nine environments | Done | `EnvironmentTests` via `Catalog` |
| GPU backend (ILGPU) | Done | `GpuBackendTests` (self-skip without accelerator) |
| Avalonia console | Done | screenshots in `docs/images/` |
| Benchmarks | Done | BenchmarkDotNet; CI smoke run |
| Docs EN / ID (11 chapters each), bilingual README | Done | reviewed |
| CI + publish workflows | Done | `rlnet-ci.yml` on Windows / Linux / macOS |
| NuGet 0.1.0 (`Gravicode.RLNet`, `Gravicode.RLNet.Gpu`) | Done | both listed on nuget.org at 0.1.0 |
| NuGet icon | Done | `icon.png` present in both `.nupkg` and declared in the nuspec; first shipped in 0.2.0 |
| Checkpoints (all agents, resumable) | Done | `CheckpointTests` — resumed PPO run identical to uninterrupted; mismatch refused with agent untouched |
| Continuous PPO / A2C (Gaussian policy) | Done | `GaussianPolicyTests` (finite differences); `LearningTests` — PPO Pendulum, A2C Reacher |
| `VectorEnvironment` + batched PPO / A2C | Done | `VectorEnvironmentTests` — one copy reproduces the single-env run exactly; `VectorPpo_LearnsCartPole` |
| Normalisation (observations, rewards) | Done | `NormalizationTests`; saved bit-exact in checkpoints |
| `MetricsLog` (CSV / JSON Lines) | Done | `MetricsLogTests` |
| Catalog pairs incl. continuous PPO / A2C | Done | `CatalogTests` — every advertised pair trains and checkpoints |
| NuGet 0.2.0 | Done | tag `RLNet-v0.2.0` |

## Checklist — 0.2.0

- [x] Checkpoints for every agent (full state, versioned format, per-agent round-trip test)
- [x] Gaussian policy; continuous PPO and A2C; registered in `Catalog`
- [x] Log-prob and entropy gradient finite-difference tests
- [x] `VectorEnvironment`; `CpuComputeBackend` comment now points at a real type
- [x] CSV / JSON-Lines metrics writer for `OnEpisode`
- [x] Observation / reward normalisation
- [x] Docs EN + ID updated together, README test count
- [x] Benchmarks: new `VectorBenchmarks` measured; PPO / A2C rewrite timed against the old build
- [x] Version 0.2.0, release notes, tag `RLNet-v0.2.0`

## Checklist — Phase 6

See [PLAN.md](PLAN.md#phase-6--next-).

- [ ] Console: save / load / watch, metrics log toggle
- [ ] Batched off-policy agents
- [ ] Continuous A2C robustness (gSDE)

## Log

### 2026-10-21 (0.2.0)
- Phase 5 complete. 162 tests (151 fast + 11 learning), all passing; full learning suite 6m 20s.
- PPO and A2C rewritten onto one `OnPolicyLearner` generic over a `PolicyHead`. Verified
  bit-identical to the old classes before anything else changed: same final average return, losses
  and evaluation score to the last digit on seeded CartPole runs.
- Checkpoints replace the PLAN item "no way to persist a policy" — that was overstated: actor-only
  `ExportParameters` already existed. What was missing was the resumable state (targets, Adam
  moments, RNG, counters), a file format, and validation.
- Continuous PPO, catalog settings: Pendulum about −170 in 100k steps (~10 s), Reacher about −26 in
  200k. Continuous A2C needs γ = 0.9: Reacher −31 vs −141 at γ = 0.99.
- Measured, and written into the docs as found: batching across copies speeds PPO up only 10–13%
  (its update dominates) and A2C not at all; parallel stepping is ~6× slower on these environments.
  The docs recommend vector environments for sample decorrelation, not speed.
- Vector loop counts episodes exactly: copies finishing on the same step past `MaxEpisodes` are not
  counted.

### 2026-10-21
- Added `PLAN.md` and `Progress.md`.
- NuGet icon: the agent–environment loop around a small policy network, the chosen action lit.
  256×256 PNG at `docs/images/icon.png`, wired through `PackageIcon` in `Directory.Build.props`
  and packed by both `RLNet.Core` and `RLNet.Gpu`. Verified with `dotnet pack`. Ships with the next
  published version — 0.1.0 on nuget.org has no icon.
- Found while surveying: `CpuComputeBackend`'s doc comment refers to a `VectorEnvironment` type that
  does not exist. Logged as PLAN item 5.3.

### 2026-09-02 (0.1.0)
- Rebuilt as a full RL library for .NET 10: six algorithms, nine environments, SIMD engine,
  optional GPU backend, Avalonia console.
- Vectorised Adam and pinned it against a scalar reference; GPU measured rather than estimated.
- Test count 81 → 88.
- Package id changed to `Gravicode.RLNet` (plain `RLNet` is taken on nuget.org).
- Published 0.1.0 to nuget.org.
