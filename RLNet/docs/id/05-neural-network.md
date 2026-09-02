# Mesin neural

[← Daftar isi](README.md) · [English](../en/05-neural-network.md)

Semua yang dipakai agen untuk mengaproksimasi fungsi: MLP dense, Adam, dan primitif tervektorkan.
Sekitar 700 baris, tanpa dependensi.

## Kenapa bukan PyTorch

Alternatif yang jelas adalah TorchSharp. Ia membawa autograd, CUDA, konvolusi, dan ekosistem matang
— sekaligus dependensi native yang besar untuk setiap platform, yang menggugurkan tujuan library ini
sebagai satu paket NuGet portabel yang berjalan sama di Windows, Linux, macOS, dan ARM.

Pertukarannya masuk akal justru karena apa yang sebenarnya dibutuhkan RL. Classic control memakai
dua atau tiga layer tersembunyi berisi 64 sampai 256 unit. Pada ukuran itu:

- Satu forward pass atas batch 256 hanya beberapa ratus mikrodetik aritmetika.
- Perjalanan bolak-balik PCIe ke GPU sendiri memakan puluhan mikrodetik, dan tidak mengecil seiring
  mengecilnya jaringan.
- Seluruh modelnya muat di cache L2.

Jadi implementasi SIMD di CPU bukan kompromi seperti yang terdengar — ia alat yang tepat untuk
ukuran masalah ini. Di tempat ia benar-benar alat yang salah, [`RLNet.Gpu`](06-gpu.md) tersedia.

Yang dikorbankan: konvolusi, rekurensi, autograd atas graf sembarang. RL berbasis piksel di luar
cakupan. Semua yang ada di [simulasi](04-simulasi.md) bekerja pada vektor fitur.

## Susunannya

```
   MlpNetwork          memiliki semua buffer; merangkai layer; menjalankan optimizer
        │
        ├── DenseLayer[]        weight, bias, gradien, cache aktivasi
        │        │
        │        └── IComputeBackend      tiga perkalian matriks
        │                 ├── CpuComputeBackend    Vector<T>, satu utas
        │                 └── GpuComputeBackend    ILGPU, paket opsional
        │
        └── AdamOptimizer       dua array momen atas seluruh jaringan
```

Sengaja **bukan** graf komputasi umum. Value head dan policy head pada RL adalah tumpukan layer
dense, dan tumpukan tetap bisa mengalokasikan semua buffer yang akan pernah dibutuhkannya saat
konstruksi — itulah sebabnya satu siklus penuh forward-backward-update mengalokasikan **nol byte**.

## Tata letak weight

Weight disimpan row-major sebagai `[inputSize, outputSize]`. Ini kontrak dengan backend, bukan
detail implementasi: inilah yang membuat ketiga pass berjalan kontigu sepanjang dimensi output.

```
  W[i * outputSize + j]   =  weight dari input i ke output j

  forward:        untuk tiap input i, tambahkan x[i] * W[i, ·] ke baris output
  grad thd. x:    untuk tiap input i, dot(gradOut, W[i, ·])
  grad thd. W:    untuk tiap input i, tambahkan x[i] * gradOut ke W_grad[i, ·]
                                                            ^^^^^^^^^^^
                        setiap loop dalamnya kontigu, jadi semuanya jadi panggilan SimdOps
```

Tata letak sebaliknya membuat forward pass menjadi gather berjarak, dan tidak satu pun dari ketiga
loop itu tervektorkan.

## SIMD

`SimdOps` memakai `Vector<T>`, yang dilebarkan JIT ke apa pun yang tersedia di host — AVX-512, AVX2,
NEON — jadi satu implementasi mencakup x64 dan ARM tanpa matriks intrinsik.

```csharp
SimdOps.Dot(a, b)                  // hasil kali titik
SimdOps.AddScaled(y, x, alpha)     // y += alpha * x        (loop dalam forward)
SimdOps.PolyakBlend(y, x, tau)     // y = y(1-tau) + x*tau  (pembaruan target lunak)
SimdOps.SumSquares(x)              // pemotongan norma gradien
SimdOps.SoftmaxInPlace(logits)     // digeser maksimum, agar logit besar tidak meluap di exp
```

[Percepatan terukur](09-benchmark.md) terhadap loop skalar yang setara: **8,2×** pada panjang 256.

### Melewati nol

Forward pass melewati input yang tepat bernilai nol:

```csharp
float x = sample[i];
if (x != 0f)
    SimdOps.AddScaled(row, weights.Slice(i * outputSize, outputSize), x);
```

Ini bukan mikro-optimisasi. Setelah ReLU, kira-kira separuh aktivasi sebuah layer tersembunyi tepat
bernilai nol, jadi ini memangkas hampir separuh pekerjaan di setiap layer setelah yang pertama.

### Satu utas, disengaja

Jaringan sekecil yang dibutuhkan classic control menyelesaikan forward pass dalam beberapa
mikrodetik, dan membangunkan worker thread-pool lebih mahal daripada pekerjaan yang diserahkan
kepadanya. Paralelisme dalam RL tempatnya satu tingkat di atas — di environment, melangkahkan banyak
salinan sekaligus — bukan di dalam layer selebar 64.

## Aktivasi

Tiga: `Linear`, `ReLU`, `Tanh`. Daftarnya sengaja pendek — setiap entri punya turunan **eksak** yang
bisa dinyatakan hanya dari *keluaran* layer:

```csharp
// ReLU: keluarannya nol tepat di tempat inputnya negatif
if (output[i] <= 0f) gradient[i] = 0f;

// Tanh: d/dx tanh(x) = 1 - tanh²(x)
gradient[i] *= 1f - output[i] * output[i];
```

Itulah yang membuat sebuah layer cukup menyimpan **satu** buffer per forward pass alih-alih dua,
memangkas separuh memori aktivasi — yang mendominasi ketika satu pembaruan PPO mendorong rollout
2048 langkah sekaligus. Nonlinearitas yang membutuhkan nilai pra-aktivasinya (GELU, SiLU) akan
melipatduakan buffer itu tanpa keuntungan terukur pada ukuran segini.

## Inisialisasi

He untuk ReLU, Xavier selain itu — varians yang menjaga sinyal maju agar tidak runtuh atau meledak
melewati tumpukan layer:

```csharp
float gain = Activation == Activation.ReLU ? 2f : 1f;
float std = MathF.Sqrt(gain / InputSize) * outputScale;
```

`outputScale` mengecilkan head terakhir. PPO dan A2C memakai 0,01, sehingga policy-nya bermula
sebagai distribusi nyaris seragam. Bermula dari policy tajam yang sembarang menghabiskan banyak
rollout untuk "melupakan"-nya, dan itu alasan umum kenapa satu run policy-gradient tampak langsung
mendatar.

Bias mulai dari nol — simetrinya sudah dipecahkan oleh weight.

## Adam

Adam alih-alih SGD biasa karena **gradien RL non-stasioner secara konstruksi**: distribusi datanya
bergeser seiring berubahnya policy, jadi langkah adaptif per parameter di sini bukan kenyamanan,
melainkan yang membuat metodenya bekerja sama sekali. Setiap set hyper-parameter yang dipublikasikan
untuk DQN, PPO, SAC, dan TD3 mengasumsikannya.

Buffer momennya berupa array datar atas seluruh jaringan, diindeks dengan offset berjalan saat
pembaruan menelusuri layer — masing-masing satu alokasi, saat konstruksi.

### Pemotongan gradien

```csharp
new AdamOptimizer(parameterCount, learningRate, maxGradientNorm: 0.5f);
```

**Pemotongannya global atas seluruh jaringan, bukan per layer.** Besaran yang merusak sebuah policy
adalah norma keseluruhan langkahnya; memotong layer demi layer akan menyisakan jaringan yang total
langkahnya tetap jauh melewati batas.

Menyala secara default untuk agen policy-gradient. Satu estimasi advantage yang apes bisa
menghasilkan gradien beberapa orde lebih besar dari sisa batch, dan tanpa pemotongan, satu langkah
itu cukup untuk menghancurkan policy yang butuh sejuta langkah untuk dipelajari.

## Jaringan target

Dua operasi, keduanya di `SimdOps`:

```csharp
target.CopyFrom(online);              // sinkron keras - DQN, tiap N pembaruan
target.SoftUpdateFrom(online, tau);   // Polyak       - SAC dan TD3, tiap pembaruan
```

Sinkron keras tiap beberapa ratus langkah dan campuran lunak pada τ = 0,005 adalah dua cara
menyelesaikan masalah yang sama: meregresi ke target yang dihitung jaringan yang sedang diperbarui
berarti mengejar target bergerak, dan itu divergen. Mana yang dipakai sebuah algoritma adalah
pertukaran stabilitas-vs-latensi, bukan soal kebenaran.

## Memverifikasinya

Backpropagation gagal **diam-diam**. Satu kesalahan tanda atau suku yang hilang menghasilkan
jaringan yang tetap terlatih, hanya ke policy yang lebih buruk, dan tidak ada benchmark RL yang
cukup peka untuk menangkapnya secara andal.

Jadi setiap gradien analitik diperiksa terhadap beda hingga terpusat:

```csharp
numeric = (Loss(θ + ε) - Loss(θ - ε)) / (2ε);
Assert.True(Math.Abs(numeric - analytic) <= 0.02f * Math.Max(1f, Math.Abs(numeric)));
```

`NeuralNetworkTests` mencakup setiap pasangan aktivasi dan gradien terhadap input;
`QNetworkPairTests` mencakup `d min(Q1, Q2) / d action`, turunan paling menentukan di SAC dan TD3 —
ia adalah seluruh sinyal yang didaki actor, dan kesalahan tanda di sana tampak seperti "RL memang
tidak stabil" alih-alih seperti sebuah bug.

Kalau Anda mengubah apa pun di lapisan ini, jalankan itu lebih dulu.

## Memakainya langsung

Tidak ada yang spesifik RL di sini:

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
    gradient[i] = predictions[i] - targets[i];      // d/dV dari ½(V-R)²

network.ZeroGradients();
network.Backward(batch);
network.ApplyGradients(optimizer, 1f / batch);
```

`InputBuffer` menyerahkan scratch milik jaringan alih-alih menerima array, sehingga pemanggil bisa
menyusun minibatch di tempat — sampel replay menulis langsung ke sana, bukan ke sementara yang lalu
disalin.

## Selanjutnya

- [GPU](06-gpu.md) — backend opsional dan kapan ia menguntungkan
- [Benchmark](09-benchmark.md) — angka terukurnya
- [Algoritma](03-algoritma.md) — apa yang duduk di atas ini
