# Simulasi

[← Daftar isi](README.md) · [English](../en/04-environments.md)

Sembilan simulasi dalam lima kategori. Masing-masing ada untuk mengajarkan sesuatu yang spesifik,
dan halaman ini menjelaskan apa.

| Simulasi | Kategori | Aksi | Observasi | Batas langkah | Mengajarkan |
|---|---|---|---|---|---|
| [GridWorld](#gridworld) | Klasik | 4 discrete | 25 one-hot | 100 | Basis. Untuk mengecek kalau ada yang salah. |
| [CartPole](#cartpole) | Klasik | 2 discrete | 4 kontinu | 500 | Benchmark rujukan |
| [MountainCar](#mountaincar) | Klasik | 3 discrete | 2 kontinu | 200 | Eksplorasi saat reward datar |
| [LunarLander](#lunarlander) | Klasik | 4 discrete | 6 kontinu | 1000 | Reward shaping |
| [Pendulum](#pendulum) | Kontrol | 1 kontinu | 3 kontinu | 200 | Kontrol kontinu; truncation |
| [Reacher](#reacher) | Robotika | 2 kontinu | 8 kontinu | 200 | Fungsi nilai multi-modal |
| [Trading](#trading) | Keuangan | 3 discrete | 6 kontinu | 511 | Observasi bebas skala |
| [SupplyChain](#supplychain) | Operasi | 7 discrete | 8 kontinu | 180 | Kredit tertunda |
| [PredatorPrey](#predatorprey) | Multi-agent | 5 discrete × 3 | 53 kontinu | 200 | Koordinasi |

## GridWorld

Menyeberangi grid 5×5 dari pojok kiri atas ke tujuan tanpa menginjak jebakan.

```
┌───┬───┬───┬───┬───┐
│ A │   │   │   │   │   A  agen (mulai di sini)
├───┼───┼───┼───┼───┤   X  jebakan (-10, episode berakhir)
│   │ X │   │ X │   │   G  tujuan  (+10, episode berakhir)
├───┼───┼───┼───┼───┤
│   │   │ X │   │   │   langkah lain: -0.1
├───┼───┼───┼───┼───┤
│   │   │   │   │   │   menabrak dinding: -1, episode lanjut
├───┼───┼───┼───┼───┤
│   │   │   │   │ G │   return optimal: 9.3
└───┴───┴───┴───┴───┘
```

**Simulasi untuk mengecek kalau ada yang salah.** Policy optimalnya bisa dihitung dengan tangan,
jadi agen yang gagal di sini punya bug, bukan masalah hyper-parameter. Q-learning tabular mencapai
9.3 — tepat optimal — dalam beberapa ratus episode.

Biaya kecil per langkah itulah yang mengubah "capai tujuan" menjadi "capai tujuan dengan *cepat*".
Tanpanya, setiap jalur yang akhirnya sampai bernilai sama dan agen tidak punya alasan memilih yang
pendek.

**Observasinya one-hot per sel, bukan pasangan `(x, y)`.** Memberi koordinat ke jaringan menyiratkan
sel 4 adalah dua kali sel 2 dalam arti tertentu, yang salah pada sebuah grid, dan agen neural
belajar jelas lebih buruk karenanya. Agen tabular tidak terpengaruh.

## CartPole

Menyeimbangkan tongkat berengsel di atas kereta dengan mendorong kereta ke kiri atau kanan.

Konstanta dan batas terminasinya persis sama dengan `CartPole-v1` di Gymnasium, jadi skor di sini
berarti sama dengan skor dari Stable-Baselines3. **500 itu sempurna; di atas 475 dihitung selesai.**

Reward-nya +1 per langkah bertahan, jadi return dan panjang episode adalah angka yang sama — kurva
naik berarti tongkat berdiri lebih lama. Itulah yang membuatnya benchmark paling gampang dibaca di
kumpulan ini.

Berakhir ketika tongkat melewati ±12° atau kereta melewati ±2.4 satuan.

## MountainCar

Mengeluarkan mobil bertenaga kurang dari lembah dengan menaiki lereng seberang lebih dulu.

**Benchmark eksplorasi.** Mesinnya tidak bisa melawan gravitasi secara langsung, jadi satu-satunya
solusi adalah berakselerasi *menjauhi* tujuan untuk membangun momentum. Reward-nya −1 per langkah
sampai bendera, artinya sampai keberhasilan pertama, setiap policy bernilai tepat −200 dan
gradiennya sama sekali tidak membawa sinyal.

Sifat itulah alasan ia ada di sini. DQN epsilon-greedy sering tidak pernah menyelesaikannya; agen
yang sama dengan prioritised replay biasanya bisa. Ini demonstrasi termurah di library ini bahwa
eksplorasi adalah masalah terpisah dari optimisasi.

## LunarLander

Mendaratkan pesawat di landasan di antara dua bendera, dengan mesin utama dan dua pendorong sikap.

> **Bukan Box2D.** LunarLander di Gymnasium adalah simulasi benda tegar. Yang ini model analitik
> lebih ringan — massa titik, sikap digerakkan torsi, tanpa penyelesai kontak — supaya library ini
> bebas dependensi fisika native. Susunan observasi, himpunan aksi, dan bentuk reward mengikuti
> aslinya, jadi agen bisa dipindahkan, tapi **skor absolutnya tidak sebanding** dengan angka
> LunarLander-v2 yang dipublikasikan.

Reward-nya dibentuk, bukan jarang: sebagian besar berasal dari potensial atas jarak, kecepatan, dan
kemiringan, jadi agen mendapat gradien jauh sebelum ia pernah mendarat. Mendarat dibayar +100,
menabrak dikenai −100, dan menyalakan mesin memakan bahan bakar setiap langkah — itulah yang
mencegah agen melayang selamanya begitu ia menemukan bahwa tidak menabrak itu menguntungkan.

Pembentukannya **berbasis potensial** (Ng, Harada, dan Russell, 1999): reward-nya adalah *perubahan*
potensial, bukan nilainya. Itu terbukti tidak mengubah policy optimal. Memberi reward atas nilainya
langsung akan membayar agen untuk berkeliaran di dekat landasan, dan itu tugas yang berbeda dari
mendarat di atasnya.

Pendaratan hanya dihitung di landasan, tegak, dan cukup pelan untuk selamat.

## Pendulum

Mengayun pendulum sampai tegak dan menahannya di sana dengan torsi yang terlalu lemah untuk
mengangkatnya langsung.

Konstantanya sama dengan `Pendulum-v1` di Gymnasium. Tugas rujukan untuk agen kontinu — SAC dan TD3
sama-sama menyelesaikannya dalam puluhan ribu langkah, sementara tidak ada agen discrete yang bisa
mengekspresikan kontrol torsi halus yang dibutuhkannya.

**Observasinya `[cos θ, sin θ, θ̇]`, bukan `[θ, θ̇]`.** Sudut itu periodik, jadi θ = −π dan θ = π
adalah state yang sama tapi merupakan dua angka yang paling berjauhan. Jaringan yang diberi sudut
mentah harus belajar merekatkan kedua ujungnya, dan umumnya gagal. Pasangan sinus-kosinus membuat
topologi lingkarannya eksplisit.

**Pendulum sama sekali tidak punya kondisi akhir** — episodenya hanya berakhir pada batas 200
langkah. Itu menjadikannya simulasi tempat bootstrapping truncation paling menentukan: memperlakukan
batas itu sebagai terminasi menghabiskan beberapa ratus poin return akhir, dan agennya tetap tampak
seperti sedang belajar. Lihat [arsitektur](02-arsitektur.md#termination-vs-truncation).

Reward-nya berupa biaya, jadi return-nya negatif. Sekitar −150 berarti selesai; policy yang tidak
pernah berhasil mengayun ke atas bernilai sekitar −1200.

## Reacher

Menggerakkan lengan dua sendi di bidang datar agar ujung jarinya mencapai target yang berpindah
setiap episode.

> **Bukan MuJoCo.** Dimodelkan dari `Reacher-v4`, tapi lengannya di sini adalah pendulum ganda yang
> digerakkan torsi dengan redaman, bukan simulasi mesin fisika — MuJoCo berarti dependensi native
> dan lisensi. Skornya tidak sebanding dengan angka MuJoCo yang dipublikasikan.

Nilai ajarnya ada pada **redundansi**: sebagian besar target bisa dicapai lewat dua konfigurasi
sendi yang berbeda, jadi fungsi nilainya benar-benar multi-modal dan policy yang merata-ratakan
kedua solusi tidak mencapai keduanya. Menonton SAC memilih salah satunya adalah gambaran terjelas di
library ini tentang kenapa policy stokastik dengan suku entropi berperilaku berbeda dari yang
deterministik.

Target masuk ke observasi sebagai **vektor dari ujung jari**, bukan posisi absolut. Itu membuat
observasinya menyatakan "ke arah mana harus bergerak", yaitu besaran yang dibutuhkan policy, dan itu
menggeneralisasi antar target alih-alih menghafalnya.

## Trading

Memperdagangkan satu instrumen atas deret harga sintetis — beli, tahan, atau jual.

> **Simulasi untuk belajar, bukan sistem trading.** Tidak ada slippage, tidak ada dampak pasar,
> tidak ada selisih bid-ask selain komisi datar 10bp, dan proses harganya sama sekali tidak
> menyerupai instrumen nyata. Agen yang untung di sini tidak mengatakan apa pun tentang pasar
> sungguhan.

Harganya mengikuti geometric random walk dengan komponen mean-reverting ringan, jadi ada keunggulan
yang benar-benar bisa dipelajari. Random walk murni mustahil dipelajari secara konstruksi, dan itu
akan menghasilkan demonstrasi yang selalu gagal; mean reversion itulah yang menjadikannya sebuah
tugas.

**Observasinya tidak memuat harga absolut.** Isinya return atas tiga horizon, simpangan dari
rata-rata bergerak, posisi, dan rasio kas — semuanya bebas skala, semuanya diklem ke `[-1, 1]`.
Memberi harga mentah akan membuat agen menghafal deret yang dilatihkan dan tidak mempelajari apa pun
yang bisa dipindahkan.

**Reward-nya adalah log return ekuitas**, bukan perubahannya. Log return menjumlah sepanjang waktu,
jadi jumlah terdiskonto yang dimaksimalkan agen adalah laju pertumbuhan majemuk — yang memang
dipedulikan seorang trader. Laba mentah akan membuat kenaikan dari 10.000 ke 10.100 terlihat sama
dengan kenaikan dari 100.000 ke 100.100.

`BuyAndHoldValue` adalah tolok ukur yang harus dikalahkan, dan konsol menampilkannya di samping net
worth.

## SupplyChain

Memutuskan berapa banyak stok yang dipesan ulang tiap hari di bawah permintaan tak pasti dan jeda
pengiriman tiga hari.

Yang menjadikan ini masalah reinforcement learning alih-alih soal aritmetika adalah **lead time**:
pesanan hari ini tiba tiga hari kemudian, jadi agen harus bertindak atas permintaan yang belum bisa
dilihatnya, dan akibat sebuah keputusan baru terlihat lama setelah keputusan itu dibuat. Penugasan
kredit yang tertunda itulah gunanya temporal-difference learning.

Permintaannya musiman dengan derau pada siklus 60 hari, jadi jumlah pesan ulang tetap tidak mungkin
optimal — agen harus membaca fase musim dari permintaan terkini. Musimnya masuk ke observasi sebagai
pasangan sinus-kosinus, dengan alasan yang sama seperti sudut di tempat lain.

**Ada baseline bagus yang sudah diketahui.** `BaseStockAction()` menghitung pesanan menurut policy
base-stock — mengisi posisi persediaan sampai level tetap setiap hari — sehingga kurva training bisa
dibaca terhadap garis yang bermakna, bukan terhadap nol. Agen yang kompeten seharusnya menyamai atau
mengalahkannya.

Biaya: 0,10 per unit yang disimpan per hari, 1,50 per unit permintaan tak terpenuhi, 5,00 untuk
setiap pemesanan, terhadap margin 1,00 per unit terjual.

## PredatorPrey

Tiga predator bekerja sama di grid 9×9 yang menyambung di tepinya untuk menyudutkan mangsa yang
kabur.

**Penangkapan mengharuskan dua predator berada di sel mangsa pada saat yang sama.** Predator yang
hanya mengejar tidak pernah mencetak angka. Reward-nya dibagi bersama, dan perilaku yang
menghasilkannya harus terkoordinasi — itulah seluruh alasan simulasi ini ada di library.

**Grid-nya menyambung**, yang menghilangkan pojok. Pada grid berbatas, predator belajar menggiring
mangsa ke pojok dan tugasnya runtuh menjadi jauh lebih mudah; pada torus tidak ada tempat untuk
menyudutkannya, jadi pengepungan sungguhan adalah satu-satunya strategi yang berhasil.

Setiap predator hanya melihat jendela 5×5 di sekitarnya ditambah arah menyambung ke mangsa — bukan
seluruh papan. Keterbatasan pengamatan itulah yang membuat masalah koordinasinya tidak sepele:
dengan state global, setiap predator bisa saja menghitung sendiri rencana bersamanya.

Mangsanya heuristik tetap, bukan pembelajar: ia kabur dari predator terdekat, dengan gerakan acak
sesekali agar predator tidak bisa mempelajari lawan yang murni reaktif. Membiarkannya tetap membuat
sinyal training cukup stasioner untuk dibaca.

Reward penangkapan diberikan ke **semua** predator, bukan hanya yang berdiri di atas mangsa. Membayar
hanya penghuninya berarti memberi imbalan kepada yang datang terakhir dan tidak mengajarkan apa pun
kepada yang lain tentang manuver yang menyiapkannya.

Lihat [multi-agent](07-multi-agent.md) untuk cara melatihnya.

## Membuat simulasi sendiri

Lihat [memperluas](10-memperluas.md). Versi singkatnya:

```csharp
public sealed class MyEnvironment : DiscreteEnvironmentBase
{
    public MyEnvironment() : base(
        BoxSpace.Uniform(4, -1f, 1f),
        new DiscreteSpace(2, ["Kiri", "Kanan"]),
        maxEpisodeSteps: 500) => Reset();

    public override string Name => "MyEnvironment";

    protected override void OnReset() { /* kondisi awal */ }
    protected override void WriteObservation(Span<float> destination) { /* terbitkan state */ }
    protected override StepResult OnStep(int action) => Advance(reward, terminated);
}
```

`Advance` menangani penghitung langkah, batas waktu, dan flag truncation, jadi tidak ada environment
yang bisa lupa melaporkan truncation — persis bug yang ingin dicegah oleh pemisahan
terminated/truncated.
