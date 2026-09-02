# Dokumentasi RLNet

*Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.*

[English](../en/README.md)

## Daftar isi

| | |
|---|---|
| [01 — Memulai](01-memulai.md) | Instalasi, training pertama, evaluasi, menyimpan policy |
| [02 — Arsitektur](02-arsitektur.md) | Susunan bagian-bagiannya, dan dua kontrak yang menyatukannya |
| [03 — Algoritma](03-algoritma.md) | Apa yang dilakukan keenam algoritma, kapan dipakai, apa yang disetel |
| [04 — Simulasi](04-simulasi.md) | Kesembilan simulasi, apa yang diajarkan, dan struktur reward-nya |
| [05 — Mesin neural](05-neural-network.md) | MLP, SIMD, Adam, dan kenapa bukan PyTorch |
| [06 — GPU](06-gpu.md) | Backend opsional, dan kapan benar-benar berguna |
| [07 — Multi-agent](07-multi-agent.md) | Simulasi gerak-serentak dan learner independen |
| [08 — Konsol](08-konsol.md) | Membaca visualizer, dan arti setiap trace |
| [09 — Benchmark](09-benchmark.md) | Throughput terukur, dan cara mengulanginya |
| [10 — Memperluas](10-memperluas.md) | Simulasi, algoritma, buffer, dan backend baru |
| [11 — Pemecahan masalah](11-pemecahan-masalah.md) | Kenapa agen tidak belajar, dan cara mengenali penyebabnya |

## Ringkasnya

RLNet adalah library reinforcement learning untuk .NET 10 tanpa dependensi eksternal. Semuanya —
simulasi, neural network, optimizer, algoritma — adalah kode C# di repositori ini.

```csharp
var environment = Catalog.CreateDiscrete("CartPole");
var agent = Catalog.CreateAgent(Algorithm.Ppo, environment.ObservationSpace, environment.ActionSpace);
var report = Trainer.Train(environment, agent, new TrainingOptions { MaxSteps = 150_000 });
```

Tiga hal yang perlu diketahui sebelum membaca lebih jauh, karena ketiganya membentuk seluruh API:

1. **Termination dan truncation adalah dua flag terpisah.** Episode yang berakhir karena agen gagal
   dan episode yang berakhir karena kehabisan waktu membutuhkan target bootstrap yang berbeda. Lihat
   [arsitektur](02-arsitektur.md#termination-vs-truncation).

2. **Observasi berupa span ke memori yang dipakai ulang oleh environment.** Span menjadi tidak valid
   pada langkah berikutnya. Salin apa pun yang harus bertahan lebih lama. Lihat
   [arsitektur](02-arsitektur.md#kontrak-observasi).

3. **Tidak ada alokasi di jalur panas.** Semua buffer dialokasikan sekali saat konstruksi. Inilah
   sebabnya library ini bisa menjalankan jutaan langkah tanpa garbage collector menjadi hambatan.

## Cakupan, dan apa yang sengaja di luarnya

RLNet **berbentuk seperti Gym, bukan kompatibel dengan Gym**. Kontrak environment-nya mencerminkan
desain `gymnasium` — space yang mendeskripsikan dirinya sendiri, reset ber-seed, terminated/truncated
sebagai dua flag terpisah — sehingga agen yang ditulis untuk salah satunya bisa dipindahkan secara
konseptual ke yang lain, dan hyper-parameter yang dipublikasikan berarti seperti biasanya. Tapi tidak
ada lapisan interop: RLNet tidak bisa memuat environment Gym dari Python, dan membangunnya berarti
menghadirkan runtime Python di dalam loop — persis hal yang ingin dihindari library ini.

Konsekuensi praktisnya:

| | |
|---|---|
| **Atari** | Tidak didukung. Butuh frame stacking, konvolusi, dan binding ALE — semuanya di luar cakupan library CPU tanpa dependensi. |
| **MuJoCo** | Tidak didukung. [Reacher](04-simulasi.md#reacher) adalah pengganti analitik untuk *tugas* robotikanya, bukan port mesin fisika; skornya tidak sebanding. |
| **LunarLander** | Ada, tapi sebagai model analitik yang lebih ringan alih-alih simulasi Box2D milik Gymnasium. Perilakunya berpindah; skor absolutnya tidak. |
| **CartPole, MountainCar, Pendulum** | Konstantanya sama persis dengan Gymnasium, jadi skornya **memang** langsung sebanding. |
| **Observasi piksel** | Di luar cakupan. Semua di sini bekerja pada vektor fitur. |

Sebagai gantinya Anda mendapat satu paket NuGet portabel tanpa binary native, yang berjalan identik
di Windows, Linux, macOS, dan ARM, dan setiap barisnya — environment, jaringan, optimizer, algoritma
— adalah C# yang bisa Anda telusuri langkah demi langkah.
