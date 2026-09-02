# Konsol

[← Daftar isi](README.md) · [English](../en/08-console.md)

```bash
dotnet run --project src/RLNet.Visualizer
```

![Konsol RLNet menjalankan PPO pada CartPole](../images/console-cartpole.png)

Aplikasi desktop Avalonia untuk menonton agen belajar, di Windows, Linux, dan macOS. Ia menjawab dua
pertanyaan sekaligus: **apa yang sedang dilakukan agen**, di sebelah kiri, dan **apakah berhasil**,
di sebelah kanan.

## Membaca panelnya

### Pita status

`ENVIRONMENT · ALGORITHM · EPISODE · STEPS · STEPS/SEC · BEST RETURN`

`STEPS / SEC` layak diperhatikan tersendiri. Ia memberi tahu ke mana waktunya pergi: Q-learning
tabular pada GridWorld berjalan ratusan langkah per detik, DQN sekitar 130 dengan satu gradient step
per dua langkah simulasi, dan SAC lebih rendah lagi karena satu pembaruan SAC menyentuh satu actor
dan empat critic.

### Viewport

Setiap simulasi menggambar dirinya sendiri, dan gambarnya dimaksudkan menunjukkan *tugasnya*, bukan
sekadar state-nya. CartPole menandai batas posisi tempat episode berakhir, sehingga kereta yang
melayang ke arah garis putus-putus langsung terbaca sebagai bahaya. LunarLander menyalakan semburan
mesin hanya saat menyala, sehingga keputusan policy dari saat ke saat terlihat. MountainCar
menggambar bendera yang tidak bisa dicapai mobil secara langsung.

**Lampu aksi** di bawah viewport menyala pada aksi yang baru dipilih. Tumpukan perekam menunjukkan
apakah policy membaik; lampunya menunjukkan apa yang sebenarnya dilakukan policy, sesuatu yang tidak
bisa ditunjukkan oleh ringkasan apa pun. Simulasi kontinu tidak punya aksi discrete untuk dinyalakan,
jadi ia mengatakannya dan menunjuk ke viewport — Pendulum menggambar torsi yang diberikan sebagai
busur ungu di sekitar poros.

### Tumpukan perekam

Tiga trace pada satu sumbu episode yang sama, ditempelkan berdampingan dan dipisah hanya oleh garis
rambut sehingga keseluruhannya terbaca sebagai satu instrumen, bukan tiga grafik. Tata letak itulah
intinya: cerita sebuah run training adalah *hubungan* di antara ketiganya.

| Trace | Warna | Artinya |
|---|---|---|
| **Episode return** | teal | Skornya. Satu-satunya trace yang langsung menjawab "apakah berhasil". |
| **Value loss** | rust | Seberapa salah critic-nya masih. |
| **Epsilon / policy entropy** | ungu | Seberapa banyak agen masih bereksplorasi. |

Return dan loss membawa **garis tren yang dihaluskan** di atas deret mentah yang samar. Return per
episode memang berisik — CartPole bisa bernilai 9 lalu 400 dengan policy yang sama persis — sehingga
trace mentahnya sendiri tidak bisa menjawab pertanyaan yang diajukan kepadanya. Garis halus itu
menjawabnya; yang mentah tetap terlihat agar deraunya diturunkan derajatnya, bukan disembunyikan.

Trace ketiga mengganti labelnya sendiri. Agen berbasis nilai bereksplorasi lewat epsilon; agen
policy-gradient bereksplorasi lewat entropi distribusinya sendiri. Keduanya menjawab "seberapa
banyak agen ini masih mencoba-coba", jadi keduanya berbagi trace dan legendanya menyebutkan mana
yang ditampilkan.

**Run yang sehat terlihat seperti ini:** eksplorasi turun mantap, return naik di belakangnya, loss
naik dulu — critic ditanyai tentang state yang belum pernah dilihatnya — lalu mendatar.

**Value loss yang naik biasanya wajar.** Seiring membaiknya policy, ia mengunjungi state baru yang
bernilai lebih tinggi, dan critic harus menyusul. Loss yang tidak pernah turun kembali sementara
return sudah mendatar barulah sinyal yang perlu dikejar; lihat
[pemecahan masalah](11-pemecahan-masalah.md).

### Bar kendali

Pemilih simulasi dan algoritma, start/stop, reset, dan slider kecepatan.

**Slider kecepatannya geometrik**, dari 1 langkah per frame sampai 65.536. Rentang yang menarik
mencakup empat orde besaran — satu langkah per frame untuk mengamati satu keputusan, puluhan ribu
untuk melewati training awal — dan slider linier akan menghabiskan hampir seluruh perjalanannya di
wilayah yang tampak identik. Di ujung rendah Anda bisa mengikuti aksi satu per satu; di ujung
tinggi viewport menjadi kabur dan tumpukan perekamlah yang layak dibaca.

**Reset** membangun ulang sesi dari nol. Setiap sesi bermula dari seed yang sama, jadi berganti
algoritma lalu kembali akan memutar ulang run yang sama — dan itulah yang membuat perbandingan dua
algoritma bermakna alih-alih menjadi perbandingan dua dunia acak yang berbeda.

## Baris perintah

Konsol menerima argumen, sehingga demonstrasi bisa diskripkan dan screenshot bisa diulang:

```bash
RLNet.Visualizer --env Pendulum --algo Sac --start
RLNet.Visualizer --env PredatorPrey --start --speed 64
RLNet.Visualizer --list        # semua simulasi dan apa yang didukungnya
RLNet.Visualizer --help
```

| | |
|---|---|
| `--env`, `-e` | Simulasi berdasarkan nama katalog (default CartPole) |
| `--algo`, `-a` | `QLearning`, `Dqn`, `A2C`, `Ppo`, `Sac`, `Td3` |
| `--start`, `-s` | Langsung mulai training |
| `--speed` | Langkah per frame; dibulatkan ke posisi slider terdekat |
| `--list` | Cetak daftar simulasi lalu keluar |

## Simulasinya, sebagaimana tampil

### Kontrol kontinu — Pendulum dengan SAC

![Pendulum dengan SAC](../images/console-pendulum.png)

Busur ungu di sekitar poros adalah torsi yang diberikan: panjangnya adalah besarnya, sisinya adalah
tandanya. Aksi kontinu tidak punya bentuk alami, dan angka telanjang tidak menyampaikan bahwa policy
sedang mendorong lembut alih-alih menghantam batas.

Perhatikan trace ketiga sudah berganti label menjadi **POLICY ENTROPY**, dan return naik dari sekitar
−1950 menuju −842 sementara loss turun. Reward Pendulum berupa biaya, jadi return-nya negatif dan
policy yang baik mendekati nol dari bawah.

### Q-learning tabular — GridWorld

![GridWorld dengan Q-learning tabular](../images/console-gridworld.png)

Return terbaik 9,3, yang tepat optimal: tujuh langkah antara pada −0,1 ditambah tujuan +10. Trace
value loss berada di nol dengan lonjakan sesekali — tabelnya sudah konvergen, dan setiap lonjakan
adalah agen mengunjungi kembali state yang belum sempat mengendap.

Inilah simulasi untuk mengecek kalau ada yang salah. Policy optimalnya bisa dihitung dengan tangan,
jadi agen yang gagal di sini punya bug, bukan masalah hyper-parameter.

### Multi-agent — PredatorPrey

![PredatorPrey dengan tiga predator berbagi parameter](../images/console-predatorprey.png)

Tiga predator kuning, satu mangsa teal, di grid yang menyambung di setiap tepi. Penangkapan
membutuhkan dua predator di sel mangsa **pada saat yang sama**, jadi mustahil diselesaikan sendirian.

Konsol menjalankannya dengan parameter bersama — satu policy, pengalaman dikumpulkan dari ketiganya —
yang melatih kira-kira tiga kali lebih cepat per detik nyata dibanding tiga learner independen dan
menghilangkan sebagian besar non-stasioneritas yang jika tidak akan mereka ciptakan satu sama lain.
Lihat [multi-agent](07-multi-agent.md).

### Keuangan — Trading

![Trading terhadap deret harga mean-reverting](../images/console-trading.png)

Penandanya terisi ketika agen memegang saham dan berongga ketika ia kosong, karena posisi adalah
state yang penting dan selain itu terkubur di dalam angka. Readout-nya membawa tolok ukurnya —
`net worth`, `buy & hold`, `edge` — karena mengalahkan buy-and-hold adalah satu-satunya hasil yang
bermakna di sini, dan warnanya berubah teal atau rust mengikuti tandanya.

Ini simulasi untuk belajar, bukan sistem trading. Lihat
[simulasi](04-simulasi.md#trading) untuk apa persisnya yang dimodelkan dan tidak.

## Cara kerjanya

Training berjalan di thread UI, dalam potongan beranggaran waktu. Thread latar akan melatih lebih
cepat, tapi renderer membaca state environment secara langsung — posisi kereta, sikap pendarat,
sudut sendi — dan membacanya sementara thread lain mengubahnya adalah race condition yang akan
memaksa setiap environment menerbitkan snapshot tak-berubah setiap langkah.

`FrameBudget` (8 ms secara default) adalah katup pengamannya: berapa pun langkah yang diminta,
potongannya berhenti ketika anggarannya habis, sehingga environment yang lambat tidak akan pernah
membekukan jendela.

Konsol juga memakai **setelan agen yang lebih ringan daripada default library** — jaringan lebih
sempit, batch lebih kecil, satu gradient step per dua langkah simulasi. Hyper-parameter yang
dipublikasikan disetel untuk hasil akhir terbaik pada run panjang, dan itu pertukaran yang salah
untuk sesuatu yang tugasnya menunjukkan seperti apa pembelajaran itu: SAC pada default-nya berjalan
sekitar 20 langkah per detik di sini, dan penonton hanya akan melihat gambar diam. Lihat
`DemoPresets.cs`, yang mendokumentasikan setiap penyimpangannya.

Siapa pun yang melatih sungguhan sebaiknya memakai default `Catalog` atau setelannya sendiri.

## Selanjutnya

- [Simulasi](04-simulasi.md) — apa yang diajarkan kesembilannya
- [Algoritma](03-algoritma.md) — apa yang sedang Anda tonton
- [Pemecahan masalah](11-pemecahan-masalah.md) — ketika trace-nya terlihat aneh
