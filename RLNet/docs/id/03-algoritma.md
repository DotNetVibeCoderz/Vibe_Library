# Algoritma

[← Daftar isi](README.md) · [English](../en/03-algorithms.md)

Enam algoritma. Halaman ini menjelaskan apa yang sebenarnya dilakukan masing-masing, kapan ia
pilihan yang tepat, dan parameter mana yang penting.

## Memilih

```
                    ┌─ aksi discrete ────┬─ state kecil, bisa didaftar ──▶ QLearning
                    │                    │
  Jenis ruang       │                    ├─ masalah belum dikenal ───────▶ PPO
  aksinya apa?   ───┤                    │
                    │                    ├─ efisiensi sampel penting ────▶ DQN
                    │                    │
                    │                    └─ ingin progres cepat terlihat ▶ A2C
                    │
                    └─ kontinu ──────────┬─ percobaan pertama ───────────▶ SAC
                                         │
                                         └─ siap menyetel ───────────────▶ TD3
```

| | Keluarga | On/off-policy | Efisiensi sampel | Kebutuhan penyetelan |
|---|---|---|---|---|
| **Q-Learning** | value, tabular | off | sangat tinggi (masalah kecil) | hampir nol |
| **DQN** | value, neural | off | tinggi | sedang |
| **A2C** | policy gradient | on | rendah | sedang |
| **PPO** | policy gradient | on | menengah | rendah |
| **SAC** | actor-critic | off | tinggi | sangat rendah |
| **TD3** | actor-critic | off | tinggi | tinggi |

## Q-Learning (tabular)

Watkins (1989). Satu nilai tersimpan per pasangan state-aksi, tanpa aproksimasi fungsi.

```csharp
var agent = new QTableAgent(
    environment.ActionSpace,
    StateDiscretizer.OneHot(),                      // GridWorld menerbitkan sel one-hot
    new QTableOptions { LearningRate = 0.2f });
```

Masih alat yang tepat untuk masalah discrete kecil: ia konvergen ke policy optimal dengan
probabilitas 1 di bawah kondisi ringan, sesuatu yang tidak bisa diklaim agen neural mana pun. Pada
GridWorld ia menemukan jalur optimal dalam beberapa ratus episode, jauh lebih cepat daripada DQN
pada tugas yang sama.

Batasannya bukan kecepatan, melainkan memori. Tabelnya punya satu entri per state berbeda, jadi ia
tidak bisa menggeneralisasi antar state yang mirip dan tidak bisa menangani ruang kontinu tanpa
dibagi kotak dulu:

```csharp
// CartPole punya 4 dimensi kontinu. Bagi kotak, secara kasar.
var encoder = StateDiscretizer.ForBox(box, [1, 1, 12, 6]);
//                                          ^  ^  ^^  ^
//              posisi dan kecepatan kereta masing-masing 1 kotak - diabaikan sepenuhnya.
//              Sudut tongkat dapat 12, kecepatan sudut 6. Itulah yang penting.
```

Jumlah kotak adalah inti persoalan pada RL tabular. Terlalu sedikit dan situasi yang benar-benar
berbeda melebur jadi satu entri; terlalu banyak dan tabelnya begitu jarang sampai tidak ada state
yang dikunjungi dua kali. Ketika Anda mulai ingin kotak yang lebih halus, itulah sinyal untuk pindah
ke DQN.

**Opsi penting:** `LearningRate`, `Epsilon` (sebuah `Schedule`), `InitialValue`.

`InitialValue` di atas return apa pun yang bisa dicapai memberi *inisialisasi optimistis*: setiap
aksi yang belum dikunjungi tampak lebih baik daripada yang sudah, sehingga agen bereksplorasi secara
sistematis alih-alih kebetulan. Pada masalah deterministik kecil ia mengalahkan epsilon-greedy
langsung. Default-nya 0 karena pada environment stokastik ia justru memperlambat.

## DQN

Mnih dkk. (2015), dengan double Q-learning dan dueling head aktif secara default.

```csharp
var agent = new DqnAgent(obs, act, new DqnOptions
{
    HiddenSizes = [128, 128],
    LearningRate = 5e-4f,
    BatchSize = 64,
    TargetUpdateInterval = 500,
    TrainFrequency = 1,
    Epsilon = Schedule.Linear(1f, 0.05f, 0.5f),
    DoubleQ = true,
    Dueling = true,
    PrioritizedReplay = true,
});
```

**Dua jaringan adalah trik intinya.** Meregresi ke target yang dihitung oleh jaringan yang sedang
diperbarui berarti mengejar target bergerak, dan itu divergen. Membekukan salinan selama beberapa
ratus langkah membuat regresinya cukup stasioner untuk konvergen.

**Double Q-learning** memisahkan pemilihan aksi dari penilaian aksi. Satu jaringan yang mengambil
`max` atas estimasinya sendiri yang berisik akan sistematis melebih-lebihkan — maksimum dari derau
bias ke atas — dan bias itu berlipat lewat bootstrapping. Jaringan online memilih aksi penerus,
jaringan target menilainya.

**Dueling head** memfaktorkan Q menjadi nilai state dan advantage:
`Q(s,a) = V(s) + A(s,a) − mean_a A(s,a)`. Ini membuat jaringan bisa belajar bahwa sebuah state buruk
tanpa harus menemukannya terpisah untuk setiap aksi. Mengurangi rata-rata bukan sekadar kosmetik —
tanpa itu V dan A hanya terdefinisi sampai sebuah konstanta yang bebas bergeser di antara keduanya.

**Prioritised replay** mengambil sampel transisi sebanding dengan galat TD, memusatkan anggaran
gradien di tempat fungsi nilainya masih salah. Pada masalah ber-reward jarang seperti MountainCar,
seringkali inilah pembeda antara belajar dan tidak. Ia membiaskan pembaruan, yang dibayar kembali
lewat bobot importance-sampling yang dianil sepanjang training.

**Opsi penting berdasarkan dampak:** `TrainFrequency` (throughput), `LearningRate`,
`TargetUpdateInterval`, jadwal `Epsilon`, `PrioritizedReplay`.

## A2C

Bentuk sinkron dari A3C-nya Mnih dkk. (2016). Satu gradient step per rollout pendek, murni
on-policy.

```csharp
var agent = new A2CAgent(obs, act, new A2COptions
{
    RolloutLength = 32,          // sengaja pendek
    LearningRate = 7e-4f,
    EntropyCoefficient = 0.01f,
});
```

Dibanding PPO, bedanya ada pada apa yang terjadi setelah satu rollout: A2C mengambil tepat satu
gradient step lalu membuang datanya, sehingga policy tidak pernah menyimpang dari policy yang
mengumpulkannya dan clipping tidak diperlukan. Lebih sederhana, dan jelas kurang efisien secara
sampel — setiap transisi menyumbang tepat satu pembaruan.

Ia layak ada karena dua alasan. Ia adalah actor-critic terlengkap yang paling ringkas di library
ini, jadi paling enak dibaca lebih dulu; dan rollout 32 langkahnya membuat ia memperbarui diri jauh
lebih sering daripada PPO, sehingga pembelajaran awal terlihat dalam hitungan detik di konsol,
bukan setelah rollout 2048 langkah pertama selesai.

## PPO

Schulman dkk. (2017). Pilihan pertama yang layak dicoba untuk tugas discrete yang belum dikenal —
bukan karena puncaknya tertinggi, tapi karena ia bekerja pada rentang masalah yang luas tanpa
penyetelan per tugas.

```csharp
var agent = new PpoAgent(obs, act, new PpoOptions
{
    RolloutLength = 2_048,
    MinibatchSize = 64,
    Epochs = 10,
    ClipRange = 0.2f,
    GaeLambda = 0.95f,
    TargetKl = 0.02f,
});
```

**Idenya.** Satu langkah policy-gradient hanya valid di dekat policy yang mengumpulkan datanya, dan
langkah besar membatalkan sampel yang membenarkannya. PPO tetap mengambil beberapa gradient step per
rollout — itulah yang membuatnya hemat sampel — dan menjaganya tetap jujur dengan meng-clip rasio
probabilitas. Begitu sebuah aksi menjadi lebih dari `1+ε` kali lebih mungkin dibanding saat
pengumpulan, objektifnya mendatar dan gradiennya lenyap:

```
                    tanpa clip ───╱
  objektif                      ╱
              ───────────────╱────────── ter-clip: datar, gradien nol
                           ╱
                    1-ε   1   1+ε        rasio
```

Wilayah datar itulah seluruh mekanismenya. Tanpanya, PPO hanyalah policy gradient biasa yang
dijalankan sepuluh kali pada data yang sama, dan itu divergen.

**`TargetKl` adalah jaring pengaman kedua.** Clip membatasi rasio setiap aksi, tapi tidak
distribusinya secara keseluruhan; satu rollout bisa menyimpang jauh dalam KL sementara setiap rasio
individual tetap di dalam rentang. Ketika rata-rata KL melewati target, pembaruan berhenti lebih
awal.

**GAE** (`GaeLambda`) menukar bias dengan varians pada estimasi advantage. Pada 0 ia adalah galat TD
satu langkah — varians rendah, bias tinggi. Pada 1 ia adalah return Monte-Carlo penuh — tak bias,
varians tinggi. 0.95 adalah kompromi lazimnya.

**Actor dan critic adalah jaringan terpisah.** Berbagi batang menghemat parameter tapi menyatukan
kedua loss lewat batang itu, dan value loss — yang jauh lebih besar magnitudonya — cenderung
mendominasi gradien policy. Jaringan terpisah lebih boros memori dan jauh lebih mudah disetel.

**Opsi penting:** `RolloutLength`, `Epochs`, `ClipRange`, `EntropyCoefficient`.

## SAC

Haarnoja dkk. (2018). Kontrol kontinu, off-policy, dengan policy stokastik.

```csharp
var agent = new SacAgent(obs, act, new SacOptions
{
    HiddenSizes = [256, 256],
    BatchSize = 256,
    Tau = 0.005f,
    AutoTuneTemperature = true,     // biarkan menyala
});
```

**Objektifnya adalah return ditambah entropi policy**, jadi agen mendapat imbalan karena tetap
tidak pasti di tempat ketidakpastian itu murah. Ini mengubah eksplorasi dari sesuatu yang ditempel
dari luar — derau yang disuntikkan, diluruhkan manual — menjadi bagian dari yang dioptimalkan, dan
itulah sebabnya SAC butuh jauh lebih sedikit penyetelan dibanding alternatifnya.

**Penyetelan temperatur otomatis adalah hal paling berguna darinya.** Titik seimbang yang tepat
antara reward dan eksplorasi bukanlah konstanta — ia berbeda per environment dan berubah sepanjang
run. Mempelajarinya terhadap entropi target menghilangkan hyper-parameter paling menjengkelkan di
library ini. Biarkan `AutoTuneTemperature` menyala kecuali ada alasan khusus.

**Koreksi tanh itu penting.** Policy-nya adalah Gaussian yang diperas lewat `tanh` agar aksinya
jatuh di `[-1, 1]`. Pemerasan itu mengubah densitasnya, dan koreksi `log(1 − tanh²)` bukan
pembukuan opsional — tanpanya log-probabilitas yang dilaporkan salah, temperatur disetel terhadap
fiksi, dan policy diam-diam menjenuh di batas aksi.

**Twin critic** (dipakai bersama TD3) mengatasi estimasi berlebih: satu critic ber-bootstrap bias
ke atas karena actor dilatih untuk memaksimalkan keluarannya sendiri yang berisik. Dua critic dengan
galat independen, diambil minimumnya, justru bias ke bawah — arah yang tidak berbahaya.

**Opsi penting:** `HiddenSizes` dan `BatchSize` (keduanya mendominasi biaya), `TrainFrequency`,
`Tau`.

## TD3

Fujimoto dkk. (2018). DDPG berhasil ketika berhasil dan divergen ketika tidak; TD3 adalah tiga
perbaikan spesifik atas sebabnya.

```csharp
var agent = new Td3Agent(obs, act, new Td3Options
{
    PolicyDelay = 2,
    PolicyNoise = 0.2f,
    NoiseClip = 0.5f,
    ExplorationNoise = 0.1f,
});
```

1. **Twin critic** — minimum yang pesimistis, seperti pada SAC.
2. **Pembaruan policy tertunda** (`PolicyDelay`) — critic diberi waktu mengendap beberapa langkah
   sebelum actor mengejarnya, sehingga actor tidak mendaki permukaan yang masih bergerak di bawahnya.
3. **Penghalusan target policy** (`PolicyNoise`, `NoiseClip`) — derau ter-clip pada aksi penerus
   mencegah actor mengeksploitasi puncak sempit tempat critic kebetulan salah. Ini memaksa critic
   benar atas satu lingkungan, bukan satu titik.

Dibanding SAC: policy TD3 deterministik, jadi seluruh eksplorasinya adalah `ExplorationNoise`, yang
besarnya harus Anda pilih. TD3 sering lebih kuat setelah disetel; SAC jauh lebih mungkin berhasil
pada percobaan pertama.

## Schedule

Laju eksplorasi, learning rate, dan clip range sama-sama ingin mengecil sepanjang run, dan
ketiganya dinyatakan dengan cara yang sama:

```csharp
Schedule.Constant(0.1f)
Schedule.Linear(1f, 0.05f, fraction: 0.5f)       // luruh selama separuh pertama, lalu tahan
Schedule.Exponential(1f, 0.05f, fraction: 0.6f)  // geometrik; pilihan klasik DQN
```

Menyelesaikan peluruhan lebih awal alih-alih tepat di akhir itu disengaja: agen yang masih
bereksplorasi pada episode terakhirnya tidak punya kesempatan mengonsolidasi, dan justru rentang
akhir dengan eksplorasi rendah itulah yang mengubah policy yang kadang berhasil menjadi policy yang
berhasil.

Schedule hanya bergerak kalau agennya diberi tahu sudah sejauh mana. `Trainer` memanggil
`SetProgress` otomatis; loop buatan sendiri harus melakukannya.

## Selanjutnya

- [Simulasi](04-simulasi.md) — sasaran untuk algoritma-algoritma ini
- [Pemecahan masalah](11-pemecahan-masalah.md) — ketika agen tidak mau belajar
- [Mesin neural](05-neural-network.md) — lapisan di bawahnya
