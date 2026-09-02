# Pemecahan masalah

[← Daftar isi](README.md) · [English](../en/11-troubleshooting.md)

Reinforcement learning gagal tanpa suara. Agen dengan pembaruan yang rusak tetap berjalan, tetap
melaporkan loss, dan tetap menghasilkan kurva — ia hanya tidak pernah membaik. Halaman ini diurutkan
berdasarkan seberapa sering setiap penyebab benar-benar yang terjadi.

## Mulai dari sini

Sebelum menyetel apa pun, jalankan algoritma yang sama pada **GridWorld**:

```csharp
var environment = new GridWorldEnvironment();
var agent = new QTableAgent(environment.ActionSpace, StateDiscretizer.OneHot(),
                            new QTableOptions { LearningRate = 0.2f });
Trainer.Train(environment, agent, new TrainingOptions { MaxEpisodes = 1_500 });

Console.WriteLine(Trainer.Evaluate(environment, agent, 20));   // seharusnya sekitar 9.3
```

Policy optimal GridWorld bisa dihitung dengan tangan. **Agen yang gagal di sini punya bug, bukan
masalah hyper-parameter** — dan pembedaan itu menghemat waktu lebih banyak daripada apa pun di
halaman ini.

## "Berjalan tapi tidak pernah membaik"

### 1. Observasi ditangkap setelah melangkah

Sejauh ini penyebab paling umum pada loop buatan sendiri.

```csharp
// SALAH - `observation` adalah view langsung, jadi saat Observe melihatnya,
// isinya sudah state penerus. Setiap transisi menyatakan tidak ada yang berubah.
var observation = environment.Observation;
var step = environment.Step(action);
agent.Observe(observation, action, step.Reward, environment.Observation, ...);

// BENAR
environment.Observation.CopyTo(buffer);     // salin SEBELUM melangkah
var step = environment.Step(action);
agent.Observe(buffer, action, step.Reward, environment.Observation, ...);
```

**Gejala:** loss turun mendekati nol — jaringan mempelajari pemetaan sepele dengan sempurna —
sementara return tidak pernah bergerak.

Pakai `Trainer.Train` dan ini tidak mungkin terjadi.

### 2. Truncation diperlakukan sebagai termination

```csharp
// SALAH
agent.Observe(obs, action, reward, next, step.Done, false);

// BENAR
agent.Observe(obs, action, reward, next, step.Terminated, step.Truncated);
```

**Gejala:** agen mendatar jauh di bawah yang seharusnya, paling kelihatan pada Pendulum yang sama
sekali tidak punya kondisi akhir. Setiap estimasi nilai di dekat batas langkah ditarik ke nol, dan
policy-nya belajar memperlakukan batas waktu sebagai bencana.

Lihat [arsitektur](02-arsitektur.md#termination-vs-truncation).

### 3. Jadwal tidak pernah bergerak

```csharp
agent.SetProgress(step / (float)totalSteps);   // wajib pada loop buatan sendiri
```

**Gejala:** epsilon diam di 1,0 selamanya, jadi agen bertindak seragam acak sepanjang run. Periksa
trace eksplorasi di konsol — garis datar di puncak berarti ini.

`Trainer` memanggilnya untuk Anda.

### 4. Observasi berskala buruk

Learning rate default para agen mengasumsikan observasi kira-kira di `[-1, 1]`. Observasi dalam
ribuan butuh learning rate berbeda untuk masalah yang selebihnya identik.

**Gejala:** loss-nya besar sekali sejak pembaruan pertama, atau jaringannya mengeluarkan `NaN` dalam
beberapa ratus langkah.

Normalkan di `WriteObservation`. `Trading` dan `SupplyChain` sama-sama melakukannya.

### 5. Besaran periodik dimasukkan mentah-mentah

Sudut, fase, hari dalam minggu. Memberi nilai mentah memberi tahu jaringan bahwa kedua ujung
siklusnya paling berjauhan, yang salah.

**Gejala:** agen mempelajari sebagian besar ruang state tapi berperilaku kacau di dekat titik
sambungnya.

Berikan `[cos θ, sin θ]`. `Pendulum`, `Reacher`, dan `SupplyChain` semuanya begitu.

## Membaca trace konsol

![Tumpukan perekam konsol selama run yang sehat](../images/console-cartpole.png)

| Yang terlihat | Artinya |
|---|---|
| Return naik, eksplorasi turun, loss naik lalu mendatar | Sehat. Loss naik di awal karena policy mencapai state yang belum pernah dilihat critic. |
| Loss mendekati nol, return datar | Jaringan mempelajari sesuatu yang sepele — biasanya penyebab 1 di atas. |
| Return naik lalu runtuh | Langkahnya terlalu besar. Turunkan learning rate, atau setel `MaxGradientNorm`. Untuk PPO, turunkan `TargetKl`. |
| Entropi runtuh ke nol dalam beberapa episode pertama | Konvergensi prematur. Naikkan `EntropyCoefficient`. |
| Entropi tidak pernah turun sama sekali | Policy-nya tidak belajar. Periksa penyebab 1-3. |
| Loss tumbuh tanpa batas | Divergen. Turunkan learning rate; periksa `TargetUpdateInterval` tidak terlalu besar untuk DQN. |
| Return berupa garis datar tepat di -200 pada MountainCar | Normal, dan justru itu inti simulasinya — reward tidak membawa sinyal sampai keberhasilan pertama. Beri prioritised replay dan lebih banyak langkah. |

## Per algoritma

### DQN tidak mau belajar

- **`LearningStarts` terlalu rendah.** Batch pertama semuanya berasal dari satu episode, yang sama
  sekali bukan sampel representatif. 1.000 adalah batas bawah yang wajar.
- **`TargetUpdateInterval` terlalu besar.** Targetnya basi dan agen mengejar target tetap yang salah.
  Terlalu kecil dan ia divergen — 200 sampai 1.000 gradient step adalah rentang yang bisa dipakai.
- **Eksplorasi meluruh terlalu cepat.** Periksa trace-nya; kalau epsilon mencapai lantainya sebelum
  return mulai bergerak, perlebar `fraction` pada jadwalnya.
- **Khusus pada MountainCar**, nyalakan `PrioritizedReplay`. Sering kali itulah pembeda antara
  belajar dan tidak.

### PPO langsung mendatar

- **Policy head tidak diinisialisasi kecil.** RLNet memakai `outputScale: 0.01` untuk alasan ini.
  Policy yang mulai tajam menghabiskan banyak rollout untuk melupakan preferensi sembarang.
- **`RolloutLength` terlalu pendek.** Di bawah beberapa ratus langkah, estimasi advantage-nya terlalu
  berisik.
- **`TargetKl` terlalu ketat.** Periksa apakah pembaruan berhenti setelah satu-dua minibatch; kalau
  ya, naikkan atau turunkan learning rate.

### SAC atau TD3 tidak mau belajar

- **Skala aksinya salah.** Keduanya bekerja di `[-1, 1]` secara internal dan menskalakan ulang di
  tepi. Kalau Anda menulis environment sendiri, pastikan batas `BoxSpace`-nya yang sebenarnya.
- **`LearningStarts` terlalu rendah.** Keduanya sengaja mengisi buffer dengan aksi acak seragam
  lebih dulu: keluaran actor yang belum terlatih bukan acak melainkan sembarang, dan cakupannya atas
  ruang aksi buruk.
- **Khusus TD3:** `ExplorationNoise` adalah satu-satunya hyper-parameter yang benar-benar sensitif.
  Terlalu kecil dan ia tidak pernah menemukan apa pun; terlalu besar dan ia tidak pernah
  mengeksploitasi. Coba SAC dulu, yang mempelajarinya sendiri.

### Q-learning tabular tidak mau belajar di environment kontinu

Kotaknya salah. Terlalu sedikit dan situasi berbeda melebur jadi satu entri; terlalu banyak dan tidak
ada state yang dikunjungi dua kali.

```csharp
// CartPole: abaikan keretanya, resolusikan tongkatnya dengan halus.
StateDiscretizer.ForBox(box, [1, 1, 12, 6]);
```

Kalau Anda mulai ingin kotak yang lebih halus, itu sinyal untuk pindah ke DQN.

## Performa

### Training lebih lambat dari perkiraan

**Periksa `TrainFrequency` lebih dulu.** Satu gradient step jauh lebih mahal daripada satu langkah
simulasi, jadi satu pembaruan per 4 langkah alih-alih per 1 kira-kira melipatempatkan throughput.

Lalu ukuran jaringan: default SAC adalah dua layer 256 unit *dan* satu pembaruan menyentuh satu actor
plus empat critic. `HiddenSizes = [64, 64]` dengan `BatchSize = 64` kira-kira sepuluh kali lebih
cepat dan tetap mempelajari Pendulum — itulah yang dipakai konsol.

Bandingkan dengan [benchmark](09-benchmark.md) untuk melihat apakah angka Anda tidak wajar.

### GPU malah membuatnya lebih lambat

Wajar di bawah lebar tersembunyi sekitar 512. Lihat [GPU](06-gpu.md) — biaya transfernya tidak
mengecil seiring mengecilnya jaringan.

### Memori terus bertambah

Tidak ada yang mengalokasikan di jalur panas, jadi ini hampir selalu replay buffer, yang besarnya
persis seperti yang diminta `BufferCapacity`:

```
byte ≈ kapasitas × (2 × observationSize + actionSize + 2) × 4
```

Satu juta transisi atas observasi 8 dimensi kira-kira 76 MB. Turunkan `BufferCapacity`.

Kalau memori tumbuh *tanpa* batas, periksa callback `OnEpisode` Anda sendiri — daftar berisi data
setiap episode mudah sekali terkumpul tanpa disadari.

## Mengulang sebuah kegagalan

Beri seed pada semuanya, maka itu jadi bug yang bisa dikejar, bukan run yang tidak bisa diulang:

```csharp
var agent = new DqnAgent(obs, act, seed: 42);
var report = Trainer.Train(environment, agent, new TrainingOptions { Seed = 42 });
```

Environment dan agen memakai seed terpisah. `EnvironmentTests` memastikan seed yang sama memutar
ulang episode yang sama persis, jadi kalau run ber-seed tidak bisa diulang, berarti environment-nya
memakai generator selain miliknya sendiri.

## Masih buntu

1. Jalankan test suite — `dotnet run --project tests/RLNet.Tests -c Release`. Kalau pemeriksaan
   gradiennya gagal, masalahnya ada di bawah kode Anda.
2. Coba setup yang sama di konsol. Menonton 30 detik perilaku biasanya lebih informatif daripada
   sekolom angka.
3. Coba algoritma lain pada environment yang sama. Kalau PPO belajar dan milik Anda tidak,
   environment-nya baik-baik saja.
