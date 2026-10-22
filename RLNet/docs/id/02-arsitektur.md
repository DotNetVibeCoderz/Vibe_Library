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

PPO dan A2C masing-masing hadir dalam dua bentuk di atas satu implementasi: `PpoAgent` dan
`A2CAgent` untuk `DiscreteSpace`, `ContinuousPpoAgent` dan `ContinuousA2CAgent` untuk `BoxSpace`.
Algoritmanya ditulis sekali terhadap sebuah *policy head* — kategorikal atau Gaussian — yang menjadi
satu-satunya bagian yang berbeda.

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

## Beberapa simulasi sekaligus

`VectorEnvironment` menjalankan beberapa salinan satu simulasi bersamaan dan menerbitkan
observasinya sebagai satu buffer kontigu `[salinan, observationSize]`. Setiap agen neural — PPO,
A2C, DQN, SAC, TD3 — mengimplementasikan `IVectorDiscreteAgent` / `IVectorContinuousAgent`, yang
memilih aksi setiap salinan dalam satu forward pass. Itu membuat pengumpulan lebih murah, tapi pada PPO update-nya yang
dominan dan percepatan ujung-ke-ujungnya sederhana ([terukur](09-benchmark.md#beberapa-simulasi-sekaligus));
keuntungan yang lebih besar adalah rollout yang kurang berkorelasi.

Reset otomatis memaksa adanya buffer observasi kedua, dan ini detail sejenis pemisahan
termination/truncation:

```
   episode salinan 2 berakhir pada langkah ini
                 │
                 ▼
  NextObservations[2]  = state tempat episode berakhir      ← bootstrap truncation dari sini
  Observations[2]      = state pertama episode berikutnya    ← pilih aksi berikutnya dari sini
```

Memperlakukan state hasil reset sebagai penerus transisi terakhir berarti memberi agen awal baru
yang tidak ia peroleh. `Trainer` meneruskan `NextObservations` ke `ObserveBatch` justru karena itu.

Di dalam agen, rollout menyimpan satu **stream** per salinan, diselang-seling berurutan waktu —
slot `t · salinan + c`. Advantage dihitung menyusuri setiap stream secara terpisah, jadi state
berikutnya dari sebuah trajektori selalu langkahnya sendiri, tidak pernah milik tetangganya. Dengan
satu salinan, ini adalah algoritma satu-simulasi, sama persis sampai setiap tarikan acaknya; test
suite menuntut hal itu.

## Checkpoint

`Checkpoint.Save` / `Checkpoint.Load` bekerja pada apa pun yang mengimplementasikan
`ICheckpointable`, dan semua agen mengimplementasikannya. Sebuah checkpoint adalah daftar array
bernama yang berversi dan little-endian:

```
  "RLNETCKP"  versi format  jumlah komponen
  ┌ nama tipe komponen ────────────────────────────────────────┐
  │ "actor.shape"       int64[]  lebar input, (lebar, aktivasi) per layer
  │ "actor.parameters"  float[]
  │ "actorOptimizer.m"  float[]  ".v"  ".step"
  │ "random"            int64[]  posisi persis generator
  │ ...                                                        │
  └────────────────────────────────────────────────────────────┘
```

Entri bernama, bukan rekaman tetap per agen, membuat formatnya tidak terikat pada field agen mana
pun: menambah state berarti menambah entri, dan file lama yang tidak memilikinya gagal dengan menyebut
nama entri itu. Sebelum apa pun ditulis ke dalam agen, agen menjabarkan apa yang ia harapkan dengan
menulis state-nya sendiri saat ini, lalu setiap entri, tipe, panjang, dan bentuk jaringan dibandingkan
dengan file — jadi ketidakcocokan dilaporkan selagi agen masih utuh. Bentuk jaringan diperiksa
sebagai susunan, bukan sekadar jumlah parameter: `[64, 32]` dan `[32, 64]` bisa memuat jumlah float
yang sama.

**Normalisasi berada di dalam agen.** `PpoOptions.NormalizeObservations` dan `NormalizeRewards`
(juga milik A2C) menyimpan statistik berjalan di dalam agen, bukan di wrapper simulasi. Policy yang
dilatih dengan input terstandardisasi tidak bermakna pada input mentah, jadi statistiknya adalah
bagian dari policy — dan karena berada di dalam agen, ia ikut disimpan dan dipulihkan, tanpa objek
kedua yang harus dijaga tetap selaras.

## Model memori

Setiap buffer yang dibutuhkan satu run dialokasikan saat konstruksi:

| Komponen | Dialokasikan sekali | Ukurannya ditentukan oleh |
|---|---|---|
| `MlpNetwork` | input, aktivasi per layer, gradien per layer | `maxBatch` |
| `DenseLayer` | weight, bias, gradien weight/bias, cache aktivasi | bentuk layer |
| `AdamOptimizer` | dua array momen | jumlah parameter |
| `UniformReplayBuffer` | lima array datar, structure-of-arrays | kapasitas |
| `RolloutBuffer` | sepuluh array datar | panjang rollout (dibagi ke semua stream simulasi) |
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
