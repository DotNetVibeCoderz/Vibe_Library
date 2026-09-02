# Arsitektur

[← Daftar isi](README.md) · [English](../en/02-architecture.md)

## Bentuk keseluruhan

```
                    ┌──────────────────────────────────────────┐
                    │              Catalog                     │
                    │   nama ──▶ simulasi ──▶ agen tersetel     │
                    └──────────────────────────────────────────┘
                                       │
        ┌──────────────────────────────┼──────────────────────────────┐
        ▼                              ▼                              ▼
┌───────────────┐            ┌──────────────────┐           ┌──────────────────┐
│  Environment  │            │      Agent       │           │     Trainer      │
│               │            │                  │           │                  │
│ Observation   │───span────▶│ SelectAction     │◀─menyetir─│  loop yang       │
│ Step(action)  │◀──aksi─────│ Observe          │           │  menyambungkan   │
│ Spaces        │            │ Metrics          │           │  keduanya        │
└───────────────┘            └──────────────────┘           └──────────────────┘
                                       │
                        ┌──────────────┴──────────────┐
                        ▼                             ▼
              ┌──────────────────┐          ┌──────────────────┐
              │  IReplayBuffer   │          │   MlpNetwork     │
              │                  │          │                  │
              │ Uniform          │          │  DenseLayer[]    │
              │ Prioritized      │          │  Adam            │
              │ Rollout (on-pol) │          │  IComputeBackend │
              └──────────────────┘          └────────┬─────────┘
                                                     │
                                        ┌────────────┴────────────┐
                                        ▼                         ▼
                                 ┌─────────────┐          ┌──────────────┐
                                 │  CPU SIMD   │          │ GPU (ILGPU)  │
                                 │  (default)  │          │  (opsional)  │
                                 └─────────────┘          └──────────────┘
```

Lima lapis, masing-masing hanya bergantung pada lapis di bawahnya. Environment tidak tahu apa-apa
tentang agen; agen tidak tahu apa-apa tentang trainer; network tidak tahu apa-apa tentang
reinforcement learning.

## Dua kontrak

Hampir semua hal yang tidak biasa di library ini berasal dari dua keputusan. Keduanya tentang
menghindari alokasi memori di jalur yang dieksekusi jutaan kali.

### Kontrak observasi

`IEnvironment.Observation` mengembalikan `ReadOnlySpan<float>` di atas buffer yang dimiliki
environment dan ditimpa di tempat.

```csharp
var observation = environment.Observation;   // sebuah view, bukan salinan
environment.Step(action);
// `observation` sekarang menampilkan state BARU. Nilai lamanya sudah hilang.
```

Alternatifnya — mengembalikan `float[]` baru setiap langkah — berbiaya satu alokasi per langkah.
Pada satu juta langkah itu berarti satu juta array berumur pendek, dan collector menjadi konsumen
CPU terbesar dalam satu run training.

Jadi aturannya: **apa pun yang harus bertahan melewati langkah harus disalin.** Library melakukan
ini di setiap titik yang penting:

```csharp
// Trainer.Train, loop tempat setiap agen berjalan
environment.Observation.CopyTo(observation);       // tangkap SEBELUM melangkah
int action = agent.SelectAction(observation);
var step = environment.Step(action);               // buffer environment sudah tertimpa
agent.Observe(observation, action, step.Reward, environment.Observation, ...);
//            ^^^^^^^^^^^ salinannya                ^^^^^^^^^^^^^^^^^^^^^^^ state baru
```

Kalau Anda menulis loop sendiri, inilah satu hal yang wajib benar. Gejala kalau salah: agen berjalan
tapi tidak pernah membaik, karena setiap transisi yang disimpannya menyatakan state tidak berubah.

### Termination vs truncation

`StepResult` membawa dua flag, bukan satu:

```csharp
public readonly record struct StepResult(float Reward, bool Terminated, bool Truncated)
{
    public bool Done => Terminated || Truncated;
}
```

- **Terminated** — episode mencapai kondisi akhir sungguhan. Tongkat jatuh, pesawat menabrak, agen
  mencapai tujuan. Tidak ada masa depan setelahnya, dan nilainya memang benar-benar nol.
- **Truncated** — episode menabrak batas langkah padahal masih berjalan baik. Masa depannya *ada*;
  kita saja yang berhenti melihat.

Setiap target bootstrap di library ini membaca `Terminated`:

```csharp
target = terminated
    ? reward                                  // tidak ada masa depan sama sekali
    : reward + gamma * ValueOf(nextState);    // masa depan ada, hitung
```

Memakai `Done` di sini adalah bug diam-diam paling umum pada kode RL buatan sendiri. Ia mengajarkan
agen bahwa dunia berakhir di langkah ke-500, sehingga state di dekat batas tampak seperti bencana.
Pada Pendulum — yang sama sekali *tidak punya* kondisi akhir, hanya batas 200 langkah — kesalahan ini
menghabiskan beberapa ratus poin return akhir, dan agennya tetap tampak seperti sedang belajar.

`Done` hanya untuk mengendalikan loop:

```csharp
if (step.Done) environment.Reset();   // pemakaian yang benar
```

## Spaces

Environment mendeskripsikan bentuknya sendiri, sehingga agen generik bisa diarahkan ke simulasi yang
belum dikenal tanpa konfigurasi:

```csharp
public abstract class Space
{
    public abstract int FlatSize { get; }
    public abstract void Sample(FastRandom random, Span<float> destination);
    public abstract bool Contains(ReadOnlySpan<float> value);
}
```

- `DiscreteSpace(count)` — himpunan aksi terbatas, dengan label opsional untuk konsol.
- `BoxSpace(low[], high[])` — vektor kontinu terbatas, satu interval per dimensi.

`DqnAgent` membaca `observationSpace.FlatSize` untuk menentukan ukuran layer input dan
`actionSpace.Count` untuk layer output. Hanya itu konfigurasinya. Ini mencerminkan peran
`gymnasium.spaces` di ekosistem Python, dan inilah wujud nyata dari "standardisasi environment".

`BoxSpace` juga membawa dua konversi yang dibutuhkan agen kontinu:

```csharp
space.Clamp(action);          // kembalikan aksi di luar jangkauan ke dalam batas
space.ScaleFromUnit(action);  // petakan keluaran tanh [-1, 1] ke batas sebenarnya
```

SAC dan TD3 sama-sama mengeluarkan aksi lewat `tanh`, jadi keluaran mentahnya selalu `[-1, 1]`.
Menempatkan penskalaan di satu tempat berarti tidak ada environment maupun algoritma yang perlu tahu
satuan pihak lain.

## Agen

Dua interface, dipisah berdasarkan jenis aksi:

```csharp
public interface IDiscreteAgent : IAgent
{
    int SelectAction(ReadOnlySpan<float> observation, bool deterministic = false);
    void Observe(ReadOnlySpan<float> observation, int action, float reward,
                 ReadOnlySpan<float> nextObservation, bool terminated, bool truncated);
}
```

`Observe` adalah tempat algoritmanya berada. Apakah ia belajar langsung (Q-learning), menyangga lalu
belajar terjadwal (DQN, SAC, TD3), atau mengumpulkan rollout lalu memperbarui sekaligus (PPO, A2C)
sepenuhnya urusan internal — loop pemanggilnya identik untuk keenamnya.

`deterministic` lebih penting daripada kelihatannya. Return training dan return evaluasi adalah dua
angka berbeda: agen yang masih bereksplorasi 5% menghabiskan satu dari dua puluh langkah melakukan
hal yang ia tahu salah. Selalu evaluasi secara deterministik sebelum melaporkan hasil.

`SetProgress(float)` memberi tahu agen sejauh mana training sudah berjalan, supaya jadwal —
peluruhan epsilon, anil learning rate, beta prioritised replay — bisa mengikutinya. `Trainer`
memanggilnya otomatis. Tanpa itu, setiap jadwal diam di nilai awalnya selamanya.

## Sambungan yang bisa diganti

Tiga hal berupa interface yang *diterima* agen alih-alih dibuat sendiri, dan inilah yang membuat
library ini modular sesuai permintaan requirement:

```csharp
// Strategi replay
var agent = new DqnAgent(obs, act, buffer: new PrioritizedReplayBuffer(1_000_000, 4, 1));

// Perangkat komputasi
var agent = new DqnAgent(obs, act, backend: GpuComputeBackend.TryCreate());

// Encoding state, untuk agen tabular
var agent = new QTableAgent(act, StateDiscretizer.ForBox(box, [12, 12, 20, 20]));
```

Masing-masing punya default yang masuk akal, jadi tidak ada yang wajib disetel untuk mulai — tapi
tidak ada pula yang mengharuskan Anda mem-fork sebuah agen.

## Model memori

Setiap buffer yang dibutuhkan satu run dialokasikan saat konstruksi:

| Komponen | Dialokasikan sekali | Ukurannya ditentukan oleh |
|---|---|---|
| `MlpNetwork` | input, aktivasi per layer, gradien per layer | `maxBatch` |
| `DenseLayer` | weight, bias, gradien weight/bias, cache aktivasi | bentuk layer |
| `AdamOptimizer` | dua array momen | jumlah parameter |
| `UniformReplayBuffer` | lima array datar, structure-of-arrays | kapasitas |
| `RolloutBuffer` | sepuluh array datar | panjang rollout |
| `ReplayBatch` | satu minibatch, dipakai ulang tiap sampling | ukuran batch |

Satu siklus penuh forward-backward-update mengalokasikan **nol byte**.
[Benchmark](09-benchmark.md) menyertakan kolom memori khusus supaya regresi di sini langsung terlihat.

Replay buffer disusun sebagai structure of arrays, bukan array berisi objek transisi. Buffer satu
juta langkah atas observasi empat dimensi berarti satu juta objek kecil di heap pada desain yang
lebih "wajar", dan segelintir array datar besar pada desain ini — jauh lebih hemat memori, dan jauh
lebih ramah bagi prefetcher saat satu batch dikumpulkan.

## Selanjutnya

- [Algoritma](03-algoritma.md) — apa yang sebenarnya dilakukan keenamnya
- [Mesin neural](05-neural-network.md) — lapisan di bawah agen
- [Memperluas](10-memperluas.md) — menambah simulasi atau algoritma sendiri
