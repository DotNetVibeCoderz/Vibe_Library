# The neural engine

[← Documentation index](README.md) · [Bahasa Indonesia](../id/05-neural-network.md)

Everything the agents use to approximate a function: a dense MLP, Adam, and vectorised primitives.
About 700 lines, no dependencies.

## Why not PyTorch

The obvious alternative is TorchSharp. It would bring autograd, CUDA, convolutions and a mature
ecosystem — and a large native dependency per platform, which defeats the point of a library meant
to be one portable NuGet package that runs the same on Windows, Linux, macOS and ARM.

The trade only works because of what RL actually needs. Classic control uses two or three hidden
layers of 64 to 256 units. At that size:

- A forward pass over a batch of 256 is a few hundred microseconds of arithmetic.
- The PCIe round trip to a GPU costs tens of microseconds on its own, and does not shrink as the
  network does.
- The whole model fits in L2 cache.

So a SIMD CPU implementation is not the compromise it sounds like — it is the right tool for this
size of problem. Where it is genuinely the wrong tool, [`RLNet.Gpu`](06-gpu.md) exists.

What is given up: convolutions, recurrence, autograd over arbitrary graphs. Pixel-based RL is out of
scope. Everything in [environments](04-environments.md) works on feature vectors.

## The stack

```
   MlpNetwork          owns every buffer; chains layers; drives the optimiser
        │
        ├── DenseLayer[]        weights, biases, gradients, activation cache
        │        │
        │        └── IComputeBackend      the three matrix products
        │                 ├── CpuComputeBackend    Vector<T>, single-threaded
        │                 └── GpuComputeBackend    ILGPU, optional package
        │
        └── AdamOptimizer       two moment arrays over the whole network
```

Deliberately **not** a general computation graph. RL value and policy heads are stacks of dense
layers, and a fixed stack can pre-allocate every buffer it will ever need at construction — which is
why a full forward-backward-update cycle allocates **zero bytes**.

## Weight layout

Weights are stored row-major as `[inputSize, outputSize]`. This is a contract with the backends, not
an implementation detail: it is what makes all three passes walk memory contiguously along the
output dimension.

```
  W[i * outputSize + j]   =  weight from input i to output j

  forward:        for each input i, add x[i] * W[i, ·] to the output row
  grad w.r.t. x:  for each input i, dot(gradOut, W[i, ·])
  grad w.r.t. W:  for each input i, add x[i] * gradOut to W_grad[i, ·]
                                                      ^^^^^^^^^^^
                          every inner loop is contiguous, so every one is a SimdOps call
```

The alternative layout makes the forward pass a strided gather, and none of the three loops
vectorise.

## SIMD

`SimdOps` uses `Vector<T>`, which the JIT widens to whatever the host offers — AVX-512, AVX2, NEON —
so one implementation covers x64 and ARM without an intrinsics matrix.

```csharp
SimdOps.Dot(a, b)                  // dot product
SimdOps.AddScaled(y, x, alpha)     // y += alpha * x        (the forward inner loop)
SimdOps.PolyakBlend(y, x, tau)     // y = y(1-tau) + x*tau  (soft target updates)
SimdOps.SumSquares(x)              // gradient-norm clipping
SimdOps.SoftmaxInPlace(logits)     // max-shifted, so large logits cannot overflow exp
```

[Measured speedups](09-benchmarks.md) against the equivalent scalar loops.

### Skipping zeros

The forward pass skips inputs that are exactly zero:

```csharp
float x = sample[i];
if (x != 0f)
    SimdOps.AddScaled(row, weights.Slice(i * outputSize, outputSize), x);
```

Not a micro-optimisation. After a ReLU, roughly half of a hidden layer's activations are exactly
zero, so this elides close to half the work in every layer past the first.

### Single-threaded, on purpose

A network small enough for classic control finishes a forward pass in a few microseconds, and waking
thread-pool workers costs more than the work handed to them. Parallelism in RL belongs one level up
— at the environment, stepping many copies at once — not inside a 64-wide layer.

## Activations

Three: `Linear`, `ReLU`, `Tanh`. The list is short on purpose — every entry has an **exact**
derivative expressible from the layer's *output* alone:

```csharp
// ReLU: the output is zero exactly where the input was negative
if (output[i] <= 0f) gradient[i] = 0f;

// Tanh: d/dx tanh(x) = 1 - tanh²(x)
gradient[i] *= 1f - output[i] * output[i];
```

That is what lets a layer cache **one** buffer per forward pass instead of two, halving activation
memory — which dominates when a PPO update pushes a 2048-step rollout through in a single batch. A
nonlinearity needing its pre-activation back (GELU, SiLU) would double that buffer for no measurable
gain at these sizes.

## Initialisation

He for ReLU, Xavier otherwise — the variance that keeps the forward signal from collapsing or
exploding through a stack of layers:

```csharp
float gain = Activation == Activation.ReLU ? 2f : 1f;
float std = MathF.Sqrt(gain / InputSize) * outputScale;
```

`outputScale` shrinks the final head. PPO and A2C pass 0.01, so the policy starts as a near-uniform
distribution. Starting from a sharp arbitrary policy costs many rollouts of unlearning, and is a
common reason a policy-gradient run appears to plateau immediately.

Biases start at zero — symmetry is already broken by the weights.

## Adam

Adam rather than plain SGD because **RL gradients are non-stationary by construction**: the data
distribution shifts as the policy changes, so a per-parameter adaptive step is not a convenience
here, it is what makes the methods work at all. Every published hyper-parameter set for DQN, PPO,
SAC and TD3 assumes it.

The moment buffers are flat arrays over the whole network, indexed by a running offset as the update
walks the layers — one allocation each, at construction.

### Gradient clipping

```csharp
new AdamOptimizer(parameterCount, learningRate, maxGradientNorm: 0.5f);
```

**The clip is global across the network, not per layer.** The quantity that destroys a policy is the
norm of the whole step; clipping layer by layer would leave a network whose total step is still far
past the limit.

On by default for the policy-gradient agents. A single unlucky advantage estimate can produce a
gradient orders of magnitude larger than the rest of the batch, and without a clip that one step is
enough to destroy a policy that took a million steps to learn.

## Target networks

Two operations, both in `SimdOps`:

```csharp
target.CopyFrom(online);              // hard sync   - DQN, every N updates
target.SoftUpdateFrom(online, tau);   // Polyak      - SAC and TD3, every update
```

A hard sync every few hundred steps and a soft blend at τ = 0.005 are two ways of solving the same
problem: regressing toward a target computed by the network being updated is a moving target, and it
diverges. Which one an algorithm uses is a stability-versus-latency trade, not a correctness one.

## Verifying it

Backpropagation fails **silently**. A sign error or a missing term produces a network that still
trains, just to a worse policy, and no RL benchmark is sensitive enough to catch it reliably.

So every analytic gradient is checked against a central finite difference:

```csharp
numeric = (Loss(θ + ε) - Loss(θ - ε)) / (2ε);
Assert.True(Math.Abs(numeric - analytic) <= 0.02f * Math.Max(1f, Math.Abs(numeric)));
```

`NeuralNetworkTests` covers every activation pairing and the gradient with respect to the input;
`QNetworkPairTests` covers `d min(Q1, Q2) / d action`, which is the single most load-bearing
derivative in SAC and TD3 — it is the entire signal the actor improves along, and a sign error there
looks like "RL is just unstable" rather than like a bug.

If you change anything in this layer, run those first.

## Using it directly

Nothing here is RL-specific:

```csharp
var random = new FastRandom(seed: 1);
var network = new MlpNetwork(
    inputSize: 4, hiddenSizes: [64, 64], outputSize: 2,
    hidden: Activation.ReLU, output: Activation.Linear,
    maxBatch: 32, random);

var optimizer = new AdamOptimizer(network.ParameterCount, 1e-3f);

// forward
inputs.CopyTo(network.InputBuffer(batch));
var predictions = network.Forward(batch);

// backward
var gradient = network.OutputGradientBuffer(batch);
for (int i = 0; i < gradient.Length; i++)
    gradient[i] = predictions[i] - targets[i];      // d/dV of ½(V-R)²

network.ZeroGradients();
network.Backward(batch);
network.ApplyGradients(optimizer, 1f / batch);
```

`InputBuffer` hands out the network's own scratch rather than accepting an array, so callers can
build a minibatch in place — a replay sample writes straight there instead of into a temporary that
is then copied.

## Next

- [GPU](06-gpu.md) — the optional backend and when it pays
- [Benchmarks](09-benchmarks.md) — the measured numbers
- [Algorithms](03-algorithms.md) — what sits on top of this
