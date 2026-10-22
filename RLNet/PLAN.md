# PLAN — RLNet roadmap / peta jalan

*Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.*

Status per phase. ✅ done · 🔜 next · 💡 considered · ⛔ out of scope

Progress against this plan is tracked in [Progress.md](Progress.md).

## Phase 1 — Core library ✅

- ✅ Gym-shaped environment contract: `DiscreteSpace` / `BoxSpace`, seeded `Reset`, separate
  `Terminated` / `Truncated`, time limits owned by `EnvironmentBase.Advance`
- ✅ Zero-allocation observation contract (spans over environment-owned memory)
- ✅ SIMD MLP engine (`Vector<T>`): dense layers, activations, Adam (vectorised), row-major weights
- ✅ `IComputeBackend` seam; single-threaded CPU backend
- ✅ Replay buffers: uniform, prioritised (SumTree); rollout buffer with GAE
- ✅ Agents: tabular Q-learning, DQN (double + dueling + PER), A2C, PPO, SAC, TD3
- ✅ `Trainer` loop with solve threshold, `OnEpisode`, `ShouldStop`, deterministic `Evaluate`
- ✅ `Catalog`: environment name → environment → configured agent

## Phase 2 — Environments ✅

- ✅ Classic: CartPole, MountainCar, GridWorld, LunarLander (analytic)
- ✅ Control: Pendulum, Reacher (analytic stand-in for the MuJoCo task)
- ✅ Domain: Trading, SupplyChain
- ✅ Multi-agent: PredatorPrey with independent or parameter-shared learners

## Phase 3 — Tooling ✅

- ✅ Optional GPU backend (`RLNet.Gpu`, ILGPU: CUDA / OpenCL, CPU fallback)
- ✅ Avalonia console (Windows / Linux / macOS) with CLI flags for reproducible screenshots
- ✅ BenchmarkDotNet suite (SIMD, agents, device report)
- ✅ Test suite: gradient checks against finite differences, learning tests, catalog-driven
  environment property tests

## Phase 4 — Release 0.1.0 ✅

- ✅ Documentation EN / ID (11 chapters each) and bilingual README
- ✅ CI (`rlnet-ci.yml`, three OSes) and publish (`rlnet-publish.yml`, `RLNet-v*` tags)
- ✅ `Gravicode.RLNet` and `Gravicode.RLNet.Gpu` 0.1.0 on nuget.org
- ✅ NuGet icon (`docs/images/icon.png`) — wired into both packages, first shipped in 0.2.0

## Phase 5 — 0.2.0 ✅

- ✅ **Checkpoints.** `Checkpoint.Save` / `Load` for every agent: networks, targets, Adam moments,
  RNG position, counters, normalisers. Versioned little-endian format of named arrays; network
  layouts checked, whole file validated before anything is overwritten, atomic file writes.
  (Agents already had actor-only `ExportParameters`; this is the complete, resumable form.)
- ✅ **Continuous PPO and A2C.** `GaussianPolicy` (state-independent log-std, unsquashed samples),
  `ContinuousPpoAgent`, `ContinuousA2CAgent`, registered in `Catalog` for Pendulum and Reacher.
  PPO and A2C now share one implementation (`OnPolicyLearner`) over a `PolicyHead`.
- ✅ **`VectorEnvironment`.** N copies with auto-reset and separate `NextObservations`; batched
  action selection in PPO / A2C; multi-stream GAE in `RolloutBuffer`; `Trainer` vector loops;
  optional parallel stepping (off by default: ~6× slower on these environments, measured).
- ✅ **`MetricsLog`.** CSV / JSON Lines per episode, invariant culture, NaN as empty / null.
- ✅ **Normalisation.** `RunningMeanStd`, `ObservationNormalizer`, `RewardScaler`, opt-in on PPO /
  A2C, saved in the agent's checkpoint.

## Phase 6 — 0.3.0: console and batching ✅

- ✅ **Console: save / load / watch / log.** `SAVE` / `LOAD` checkpoints; a load the console's
  lighter agent refuses is retried against a Catalog-configured one, so policies trained in code
  can be watched. `WATCH` runs deterministically on a *second* environment copy, so training resumes
  bit-identical. `LOG` writes a CSV per run. CLI: `--load`, `--watch`, `--log`, `--log-dir`.
- ✅ **Batched off-policy agents.** DQN, SAC and TD3 implement the vector interfaces: one forward
  pass for every copy's action, one replay buffer, `TrainFrequency` counted in transitions. One
  copy reproduces the single-environment run exactly. (DQN has no n-step returns yet, so there was
  no per-copy n-step state to keep apart — that is now Phase 7.)
- ✅ **Continuous A2C robustness — gSDE.** `A2COptions.StateDependentExploration`, on by default in
  `Catalog`. On four copies over twelve seeds: Reacher mean unchanged (−87) with the spread across
  seeds cut from ±35 to ±9; Pendulum median at 300k steps −568 → −220. It does *not* remove the
  need for γ = 0.9 (−133 at 0.99), and it is worse on a single environment.
- ✅ Released as 0.3.0 (tag `RLNet-v0.3.0`).
- ✅ Library fixes found on the way: deterministic Q-table action selection no longer draws from
  the training generator or inserts table rows; deterministic calls no longer overwrite
  `Metrics.Epsilon`; loading a checkpoint restores it.

## Phase 7 — next 🔜

1. 🔜 **n-step returns for DQN.** Now that DQN takes vector batches, n-step needs one pending
   window per copy — a window that runs across two copies computes returns over two unrelated
   episodes. Measure against 1-step on CartPole and LunarLander.
2. 🔜 **gSDE for PPO.** Needs a resample interval: one exploration draw held for PPO's whole
   2,048-step rollout explores a single direction. Measured without it: Pendulum −164 → −799,
   Reacher −26 → −118. The reference implementation resamples every few steps; evaluate that.
3. 🔜 **0.4.0** with Phase 7.

## Later 💡

- 💡 GPU: pipeline kernel launches so wide networks stop paying a sync per layer
  (`docs/en/06-gpu.md` names this as the current ceiling) — re-measure the crossover afterwards.
- 💡 More algorithms: QR-DQN / C51 (distributional), Rainbow composition.
- 💡 Multi-agent: centralised critic (MADDPG / MAPPO) on top of `IMultiAgentEnvironment`.
- 💡 Hyper-parameter sweep helper over `Catalog` + seeds, reporting mean ± spread.
- 💡 Recurrent policy (GRU) for partially observable tasks — needs BPTT in `Neural/`, so gradient
  tests first.
- 💡 NativeAOT / trimming check for `RLNet.Core` (it is reflection-free, so likely just a CI job).

## Out of scope ⛔

Decided, not forgotten — see the scope note in `docs/en/README.md`.

- ⛔ Python Gym / Gymnasium interop (would put a Python runtime in the loop)
- ⛔ Atari, pixel observations, convolutions
- ⛔ MuJoCo / Box2D physics ports
- ⛔ TorchSharp or any native dependency in `RLNet.Core`
