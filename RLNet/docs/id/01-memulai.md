# Memulai

[← Daftar isi](README.md) · [English](../en/01-getting-started.md)

## Instalasi

```bash
dotnet add package Gravicode.RLNet
```

Butuh .NET 10 SDK. Tidak ada dependensi lain — tanpa binary native, tanpa Python, tanpa apa pun yang
harus dipasang terpisah.

Backend GPU adalah paket terpisah dan opsional:

```bash
dotnet add package Gravicode.RLNet.Gpu   # hanya menguntungkan untuk jaringan lebar; lihat docs/id/06-gpu.md
```

## Program terpendek yang berguna

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
Console.WriteLine($"Evaluasi: {Trainer.Evaluate(environment, agent, episodes: 20)}");
```

```
333 episodes, 61,306 steps in 11.2s (5,494 steps/s), final average return 475.54, solved at episode 333
Evaluasi: 500
```

Run berhenti lebih awal karena rata-rata bergeraknya melewati ambang "selesai". `Catalog` memilih
default yang wajar untuk PPO di simulasi ini, jadi tidak ada yang perlu dikonfigurasi.

## Memantau run

`OnEpisode` dipanggil setiap episode selesai. Jaga tetap ringan — kode ini berjalan di dalam loop
training.

```csharp
var report = Trainer.Train(environment, agent, new TrainingOptions
{
    MaxSteps = 150_000,
    OnEpisode = e =>
    {
        if (e.Episode % 25 == 0)
            Console.WriteLine($"ep {e.Episode,4}  return {e.Return,7:F1}  rata2 {e.AverageReturn,7:F1}");
    },
});
```

`AverageReturn` adalah rata-rata bergerak atas `WindowSize` episode terakhir (default 100). Inilah
angka yang layak dipantau: return satu episode terlalu berisik untuk menyimpulkan apa pun.

Untuk berhenti berdasarkan kondisi sendiri:

```csharp
ShouldStop = () => DateTime.UtcNow > batasWaktu,
```

## Mencatat sebuah run

`MetricsLog` menulis satu baris per episode, disertai diagnostik agen pada saat itu:

```csharp
using var log = MetricsLog.Create("ppo-cartpole.csv");     // .jsonl untuk JSON Lines

Trainer.Train(environment, agent, new TrainingOptions
{
    MaxSteps = 150_000,
    OnEpisode = log.For(agent),
});
```

Kolomnya adalah `episode, steps, return, average_return, total_steps, terminated, value_loss,
policy_loss, entropy, epsilon, temperature, td_error, updates`. Diagnostik yang tidak dihasilkan
algoritmanya — policy loss untuk DQN, temperature untuk selain SAC — menjadi field CSV kosong atau
`null` di JSON, sehingga grafik menampilkan celah, bukan garis di nol. Angka ditulis dengan invariant
culture, jadi log dari mesin yang memakai koma desimal tetap bisa dibaca di mana saja.

## Evaluasi yang benar

Return saat training dan return saat evaluasi adalah dua angka berbeda, dan melaporkan yang pertama
seolah-olah yang kedua akan melebih-lebihkan kualitas policy. Agen yang masih bereksplorasi 5%
menghabiskan satu dari dua puluh langkah melakukan hal yang sudah ia tahu salah.

```csharp
float skor = Trainer.Evaluate(environment, agent, episodes: 20, seed: 1234);
```

`Evaluate` berjalan tanpa eksplorasi. Memberi seed membuat evaluasi dapat diulang, sambil tetap
memberi setiap episode kondisi awal yang berbeda.

## Menyimpan dan melanjutkan

`Checkpoint` menulis state lengkap sebuah agen — setiap jaringan termasuk jaringan target, estimasi
momen setiap optimizer, generator acak, statistik normalisasi, dan posisi jadwal:

```csharp
using RLNet.Persistence;

Checkpoint.Save("cartpole.rlnet", agent);
```

Untuk memuat, buat agen dengan konfigurasi yang sama lalu pulihkan ke dalamnya:

```csharp
var agent = new PpoAgent(environment.ObservationSpace, environment.ActionSpace);
Checkpoint.Load("cartpole.rlnet", agent);
```

Semua agen di library ini mendukungnya. Agen PPO atau A2C yang dipulihkan melanjutkan persis dari
titik agen yang disimpan berhenti: test suite melatih satu agen tanpa jeda dan satu lagi melalui
simpan-dan-muat, lalu menuntut kedua run menghasilkan return yang identik. Yang *tidak* disimpan
adalah pengalaman — DQN, SAC, dan TD3 kembali dengan replay buffer kosong dan menunggu
`LearningStarts` transisi sebelum training lagi, dan agen on-policy membuang rollout yang baru
setengah terkumpul.

**Pemuatan memeriksa semuanya sebelum mengubah apa pun.** Checkpoint dari algoritma lain, atau dari
jaringan berbentuk lain, melempar `InvalidDataException` yang menyebut entri yang berbeda, dan agen
dibiarkan persis seperti semula. File ditulis di samping tujuannya lalu dipindahkan ke tempatnya,
jadi crash di tengah penyimpanan tidak pernah merusak checkpoint sebelumnya.

Beberapa komponen bisa berbagi satu file, dicocokkan berdasarkan urutan — sebuah agen dan apa pun
yang dilatih bersamanya:

```csharp
Checkpoint.Save("run.rlnet", learners);          // IndependentLearners: setiap agen berbeda sekali
```

`ExportParameters` dan `ImportParameters` tetap ada untuk saat hanya bobot actor yang dibutuhkan,
misalnya untuk diserahkan ke runtime lain. Keduanya tidak membawa state optimizer, statistik
normalisasi, atau log standar deviasi PPO.

## Memilih algoritma

| Situasi | Mulai dengan |
|---|---|
| State discrete kecil, ingin policy optimal | `QLearning` |
| Aksi discrete, masalah belum dikenal | `Ppo` |
| Aksi discrete, butuh hemat sampel | `Dqn` |
| Aksi discrete, ingin langsung melihat pembelajaran | `A2C` |
| Aksi kontinu, percobaan pertama | `Sac` |
| Aksi kontinu, siap menyetel | `Td3` |
| Aksi kontinu, simulasi murah, banyak salinan | `Ppo` |

[Algoritma](03-algoritma.md) menjelaskan apa yang sebenarnya dilakukan masing-masing dan arti
setiap parameternya.

## Kontrol kontinu

Bentuk API-nya sama, hanya aksinya berupa vektor, bukan indeks:

```csharp
var environment = Catalog.CreateContinuous("Pendulum");
var agent = Catalog.CreateAgent(Algorithm.Sac, environment.ObservationSpace, environment.ActionSpace);

var report = Trainer.Train(environment, agent, new TrainingOptions { MaxSteps = 40_000 });
```

Reward Pendulum berupa biaya, jadi return-nya negatif dan policy yang baik mendekati nol dari bawah.
Sekitar -150 berarti selesai; policy yang tidak pernah berhasil mengayun ke atas bernilai sekitar
-1200.

PPO dan A2C juga bekerja di sini, dengan policy Gaussian. Keduanya butuh lebih banyak langkah
simulasi daripada SAC dan jauh lebih sedikit komputasi per langkah — tanpa replay, tanpa update critic
di setiap langkah — sehingga pada simulasi yang murah keduanya sering selesai lebih cepat menurut jam:

```csharp
var agent = Catalog.CreateAgent(Algorithm.Ppo, environment.ObservationSpace, environment.ActionSpace);
var report = Trainer.Train(environment, agent, new TrainingOptions { MaxSteps = 100_000 });
```


## Beberapa simulasi sekaligus

PPO dan A2C bisa mengumpulkan data dari beberapa salinan simulasi bersamaan, memilih aksi setiap
salinan dalam satu forward pass:

```csharp
var environments = Catalog.CreateDiscreteVector("CartPole", count: 8);
var agent = Catalog.CreateVectorAgent(Algorithm.Ppo, environments.ObservationSpace, environments.ActionSpace, seed: 1);

Trainer.Train(environments, agent, new TrainingOptions { MaxSteps = 150_000, Seed = 1 });

// Tetap agen biasa: evaluasi pada satu salinan.
float skor = Trainer.Evaluate(Catalog.CreateDiscrete("CartPole"), agent, episodes: 20);
```

`RolloutLength` tetap berarti jumlah transisi per update, dibagi rata ke semua salinan, jadi opsi
yang sama menghasilkan jumlah pembelajaran yang sama, dari sampel yang kurang berkorelasi. Jangan
berharap percepatan besar pada simulasi-simulasi ini: [terukur](09-benchmark.md#beberapa-simulasi-sekaligus),
PPO hanya lebih cepat sekitar 10% karena update-nya dominan, dan A2C tidak sama sekali. Episode dihitung
per salinan, jadi `MaxEpisodes` dan `SolveThreshold` tetap berarti seperti biasa.

DQN, SAC, dan TD3 belajar dari replay buffer dan tidak di-batch; `CreateVectorAgent` menolaknya.

## Mengonfigurasi agen

`Catalog.CreateAgent` memilih default. Kalau butuh kendali, buat langsung — semua konstruktor
publik dan setiap opsi punya default yang terdokumentasi:

```csharp
var agent = new DqnAgent(
    environment.ObservationSpace,
    environment.ActionSpace,
    new DqnOptions
    {
        HiddenSizes = [256, 256],
        LearningRate = 1e-4f,
        BatchSize = 128,
        TrainFrequency = 4,                              // satu gradient step per 4 langkah
        Epsilon = Schedule.Linear(1f, 0.02f, 0.4f),      // meluruh selama 40% pertama training
        PrioritizedReplay = true,
    },
    seed: 42);
```

`TrainFrequency` adalah pengungkit terbesar untuk kecepatan waktu nyata. Satu gradient step jauh
lebih mahal daripada satu langkah simulasi, jadi menaikkannya dari 1 ke 4 kira-kira melipatempatkan
throughput dengan sedikit mengorbankan efisiensi sampel.

## Menulis loop sendiri

`Trainer` ada supaya Anda tidak perlu melakukannya, tapi kalau butuh kendali penuh:

```csharp
var observation = new float[environment.ObservationSpace.FlatSize];
environment.Reset(seed: 1);

for (long step = 0; step < 100_000; step++)
{
    agent.SetProgress(step / 100_000f);          // menggerakkan jadwal epsilon dan learning rate

    environment.Observation.CopyTo(observation);  // SALIN, sebelum melangkah
    int action = agent.SelectAction(observation);
    var result = environment.Step(action);

    agent.Observe(observation, action, result.Reward, environment.Observation,
                  result.Terminated, result.Truncated);   // dua flag, terpisah

    if (result.Done)
    {
        agent.OnEpisodeEnd();
        environment.Reset();
    }
}
```

Tiga hal yang harus benar, semuanya dibahas di [arsitektur](02-arsitektur.md):

1. Salin observasi **sebelum** melangkah — environment menimpa buffer-nya sendiri.
2. Berikan `Terminated` dan `Truncated` terpisah — jangan pernah menggabungkannya jadi satu flag.
3. Panggil `SetProgress` — tanpa itu, tidak ada jadwal yang bergerak.

## Selanjutnya

- [Arsitektur](02-arsitektur.md) — kenapa API-nya begini
- [Simulasi](04-simulasi.md) — kegunaan kesembilan simulasi
- [Konsol](08-konsol.md) — menonton run, bukan membaca angka
