# Clustering

*Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.*
*[English](../en/06-clustering.md) · [Indeks dokumentasi](README.md)*

## Menjalankannya

```bash
actornet run --node-id node-a --port 9000 --cluster
actornet run --node-id node-b --port 9001 --seed 127.0.0.1:9000
actornet run --node-id node-c --port 9002 --seed 127.0.0.1:9000
```

Node pertama membutuhkan `--cluster`. Ia tidak punya seed sendiri, dan tanpa flag itu ia berjalan
standalone: ia akan menjawab handshake join tapi tidak pernah ber-gossip, sehingga peer-nya lama-lama
menandai node yang sebenarnya sehat sebagai tidak terjangkau.

Dalam kode:

```csharp
options.Cluster.Enabled = true;
options.Cluster.Seeds = ["10.0.1.4:9000", "10.0.1.5:9000"];
```

Satu seed yang terjangkau sudah cukup — pihak yang bergabung menerima seluruh tabel member dan
ber-gossip dari sana. Daftarkan dua atau tiga supaya restart tidak bergantung pada satu mesin saja.

## Apa yang terlihat

![Halaman cluster: ring dan tabel member](../images/console-cluster.png)

Tiga member, masing-masing dengan 128 replika di ring, memiliki 34,3%, 33,0%, dan 32,7% dari
keyspace. Garis-garisnya adalah virtual node, dan cara mereka berselang-seling itulah intinya — lihat
di bawah.

Pencacah di samping tiap member berasal dari member itu sendiri, bukan dari node ini.
`GetClusterStatusAsync` menanyai setiap peer yang terjangkau tentang angkanya sendiri lalu
mengembalikan semuanya bersama:

```csharp
var status = await system.GetClusterStatusAsync(TimeSpan.FromSeconds(2));

status.ActiveActors;   // di seluruh cluster
status.Busiest;        // node yang menanggung pekerjaan in-flight terbanyak
status.Silent;         // member yang tidak menjawab tepat waktu
```

Ring dan tabel member sejak dulu bersifat cluster-wide, sedangkan pencacahnya tidak — jadi sebuah
konsol bisa melaporkan lima member tapi hanya menunjukkan apa yang dikerjakan satu di antaranya, dan
node yang terkubur pekerjaan tampak persis seperti node menganggur bila dilihat dari tiga node
jauhnya. **Total cluster bukan angka yang menarik**; `Busiest` yang menarik, karena satu node
terkubur sementara yang lain santai adalah masalah penempatan yang justru disembunyikan oleh
penjumlahan.

**Peer yang tidak menjawab disebut namanya, bukan dibuang.** Menjumlahkan empat node lalu
menyajikannya sebagai lima akan lebih buruk daripada menyebutkan siapa yang hilang, dan peer yang
mendadak diam justru hal yang ingin diketahui orang yang sedang menatap halaman ini. Konsolnya
menampilkan tanda pisah pada barisnya.

Bertanya memakan satu perjalanan bolak-balik per peer, jadi konsol bertanya lebih jarang daripada ia
menggambar ulang. Halaman yang memungut data dari cluster dua puluh node setiap detik akan berubah
menjadi pembangkit beban bagi hal yang seharusnya ia amati.

## Penempatan: hash ring

Setiap alamat actor di-hash ke sebuah ring 64-bit. Setiap member ditempatkan di
`VirtualNodesPerMember` posisi, dan sebuah key menjadi milik posisi pertama pada atau setelah
hash-nya.

```csharp
var owner = system.Cluster.OwnerOf(ActorId.For<BankAccountActor>("alice"));
var mine  = system.Cluster.IsLocal(ActorId.For<BankAccountActor>("alice"));
```

**Kenapa consistent hashing dan bukan `hash % jumlahMember`?** Karena apa yang terjadi saat
keanggotaan berubah. Modulo mengocok ulang hampir semuanya: dari 3 node ke 4 memindahkan sekitar 3/4
key. Consistent hashing memindahkan sekitar 1/N — terukur 15–35% untuk transisi itu, dengan sebuah
tes yang memastikannya tetap di rentang tersebut. Setiap key yang berpindah adalah satu actor yang
harus dinonaktifkan di sini dan diaktifkan di sana, jadi selisihnya adalah selisih antara sebuah
rebalance dan sebuah gangguan layanan.

**Kenapa 128 virtual node?** Dengan satu posisi per member, pembagiannya sangat timpang — di mana
tiga titik acak itu kebetulan mendarat menentukan segalanya. Replika meratakannya. Pada 128, bagian
terburuk di cluster 3 node berada dalam beberapa persen dari rata, dan tesnya memastikan deviasinya
di bawah 15%.

**Kenapa hash-nya seperti itu.** FNV-1a atas UTF-8, lalu finalizer MurmurHash3. Dua sifat penting:

- *Tidak bergantung proses.* `string.GetHashCode()` diacak per proses, sehingga dua node akan
  membangun ring berbeda dari daftar member yang sama dan berselisih tentang kepemilikan. Bug itu
  hanya muncul di cluster sungguhan. Hash-nya dipatok di sebuah tes terhadap vektor yang diketahui.
- *Ber-avalanche baik.* FNV-1a mentah menggerombol buruk pada string pendek yang berbagi awalan — dan
  itu persis wujud posisi ring (`node-1#0`, `node-1#1`, …). Terukur, satu node mengambil 48% keyspace.
  Finalizer-nya memperbaiki itu.

## Keanggotaan

Protokolnya kecil:

1. Yang bergabung mengirim `Join` ke setiap seed.
2. Seed menjawab `JoinAck` berisi seluruh tabel member-nya.
3. Setelah itu setiap node secara berkala mengirim seluruh tabelnya ke setiap peer yang dikenalnya.

### Fanout

Langkah 3 tidak berarti ke semua orang, karena biayanya akan O(member²) frame per interval — tidak
berarti pada sepuluh node dan menghancurkan pada seratus. Setiap denyut meng-gossip ke
`GossipFanout` peer (bawaan 4), dan informasinya sampai ke sisanya secara tak langsung:

```csharp
options.Cluster.GossipFanout = 4;   // 0 berarti semua peer, dan cluster kecil memang begitu
```

Di bawah angka fanout tidak ada yang perlu dibatasi, jadi cluster lima node berperilaku persis
seperti sebelumnya. Di atasnya, konvergensi butuh sekitar log(member) ronde alih-alih satu — pada
denyut dua detik, itu beberapa detik untuk cluster seratus node.

**Peer diambil bergiliran, bukan acak.** Subset acak adalah pilihan buku teks dan justru keliru di
sini, karena jarak antara dua node tertentu jadi untung-untungan — sedangkan detektor kegagalan,
yang belajar seberapa sering ia mendengar tiap peer, tidak bisa mempelajari lemparan koin. Ia
terpaksa dibuat toleran terhadap kesunyian yang sekadar sial, dan toleransi itulah yang membuat
detektor jadi lambat. Rotasi membuat jaraknya tepat `ceil(peer / fanout)` denyut, dan angka itu
diberitahukan ke detektor supaya anggota yang baru ditemukan tidak dicurigai sebelum jendelanya
terisi.

### Node boleh dinyalakan dalam urutan apa pun

Langkah 1 diulang. Node yang punya seed tetapi sama sekali tidak melihat peer akan mengirim `Join`
lagi pada setiap heartbeat, sehingga node yang seed-nya belum hidup langsung bergabung begitu salah
satu seed itu ada:

```
Join sent to 0 of 1 seeds.        # belum ada yang mendengarkan di port seed
Still alone; reached 0 of 1 seeds. # sekali tiap heartbeat, di level debug
cluster: mac-c:Up, win-a:Up        # seed-nya hidup dan join-nya mendarat
```

Pengulangan berhenti begitu ada peer di ring, dan peer `Unreachable` pun dihitung — ia memang
ditunggu kembali, jadi menyemai ulang karena gangguan sesaat justru menimbulkan keriuhan, bukan
pemulihan. Ini baru terasa di luar loopback: koneksi lokal ke port tertutup ditolak seketika dan
handshake sekali-di-awal biasanya menang balapan, sedangkan di jaringan sungguhan connect yang sama
gagal dengan cara yang membuat satu percobaan itu untung-untungan.

### Status

| Status | Di ring? | Arti |
| --- | --- | --- |
| `Joining` | tidak | Terlihat, handshake belum selesai |
| `Up` | ya | Sehat |
| `Unreachable` | **ya** | Melewatkan beberapa heartbeat |
| `Down` | tidak | Sudah dilepas; key-nya dibagikan ulang |
| `Leaving` | tidak | Sedang berhenti dengan anggun |

**Unreachable tetap di ring.** Penyebab umum heartbeat yang terlewat adalah jeda GC atau gangguan
jaringan sesaat, dan memindahkan key sebuah node berongkos satu gelombang deaktivasi dan aktivasi
ulang. Menunggu lebih murah daripada salah menduga.

### Bagaimana kesunyian dinilai

Ada dua detektor, dan bawaannya yang adaptif.

```csharp
options.Cluster.FailureDetection = FailureDetection.PhiAccrual;   // bawaan
options.Cluster.PhiUnreachableThreshold = 8;                      // dicurigai, masih dirutekan
options.Cluster.PhiDownThreshold        = 12;                     // dikeluarkan dari ring
options.Cluster.AcceptableHeartbeatPause = TimeSpan.FromSeconds(3);
options.Cluster.MinimumStandardDeviation = TimeSpan.FromMilliseconds(100);
options.Cluster.HeartbeatSampleSize      = 200;
```

Phi adalah ukuran keheranan, bukan stopwatch: kira-kira berapa "sembilan" keyakinan node ini bahwa
peer sesunyi itu sudah berhenti, dinilai terhadap berapa lama denyut peer tersebut biasanya makan
waktu. Phi 8 berarti "salah sekitar sekali dalam 10^8, menurut riwayat peer itu sendiri".

Gunanya mengukur adalah satu ambang jadi berarti berbeda di tautan yang berbeda. Peer di seberang
hop nirkabel yang denyutnya meleset setengah detik diberi kelonggaran itu; peer di switch yang sama
yang berdenyut tiap 200ms dicurigai jauh lebih cepat, karena bagi *dia* diam dua detik memang tidak
wajar. Tenggat tetap harus disetel untuk tautan terburuk di cluster dan karenanya terlalu lambat di
semua tautan lain — pada uji tiga mesin yang melatarbelakangi ini, node yang sehat melewati tenggat
tetap sepuluh detik yang tak akan dilewati sepasang node satu ruangan dalam semenit.

`AcceptableHeartbeatPause` adalah kelonggaran stop-the-world, ditambahkan ke rata-rata sebelum
kecurigaan dimulai. Tanpa itu, peer yang denyutnya metronomik nyaris tak punya sebaran terukur, dan
jeda GC sepersekian detik melewati intervalnya sudah terbaca sebagai kegagalan.
`MinimumStandardDeviation` adalah pertahanan yang sama dari sisi lain: lantai bagi sebaran, supaya
phi tidak melompat dari nol ke raksasa dalam satu milidetik.

Tenggat tetap tetap tersedia, untuk saat angka datar yang tersurat lebih berharga daripada akurasi —
terutama tes yang ingin sebuah node mati pada detik yang diketahui:

```csharp
options.Cluster.FailureDetection  = FailureDetection.Deadline;
options.Cluster.HeartbeatInterval = TimeSpan.FromSeconds(2);
options.Cluster.UnreachableAfter  = TimeSpan.FromSeconds(10);   // dicurigai, masih dirutekan
options.Cluster.DownAfter         = TimeSpan.FromSeconds(30);   // dikeluarkan dari ring
options.Cluster.JoinTimeout       = TimeSpan.FromSeconds(5);    // lama start menunggu seed
```

`Validate()` menolak `DownAfter <= UnreachableAfter` (jeda singkat akan mengeluarkan node sehat),
`HeartbeatInterval >= UnreachableAfter` (sebuah node akan dinyatakan tidak terjangkau sebelum denyut
berikutnya jatuh tempo), dan `PhiDownThreshold <= PhiUnreachableThreshold` (peer akan dikeluarkan
dari ring tanpa pernah diberi keraguan yang menguntungkan).

`JoinTimeout` hanya membatasi proses start. Seed dihubungi serentak, bukan bergiliran, sehingga satu
seed di balik firewall yang membuang paket tidak bisa menelantarkan seed lain; dan ketika tenggatnya
lewat, node itu naik sendirian lalu terus mencoba lagi. Node yang belum selesai start tidak bisa
melayani aktor yang sudah menjadi miliknya, dan itu lebih buruk daripada sebentar sendirian.

### Ketika cluster terbelah dua

Partisi terlihat sama dari kedua sisi: separuh anggota berhenti menjawab, dan separuh tempat Anda
berdiri tampak sehat. Bila dibiarkan, kedua belahan mengambil kunci milik lawannya, dan aktor yang
sama diaktifkan dua kali dengan dua versi state yang tak akan pernah didamaikan.

Tidak ada yang dilakukan soal itu kecuali Anda memintanya:

```csharp
options.Cluster.SplitBrainStrategy = SplitBrainStrategy.KeepMajority;
options.Cluster.SplitBrainStabilityWindow = TimeSpan.FromSeconds(7);
```

| Strategi | Sebuah sisi bertahan bila |
| --- | --- |
| `None` (bawaan) | Selalu. Kedua belahan terus melayani. |
| `KeepMajority` | Ia melihat lebih dari separuh keanggotaan terakhir yang disepakati |
| `StaticQuorum` | Ia melihat sedikitnya `StaticQuorumSize` anggota |

Dari CLI cukup dua flag:

```bash
actornet run --node-id a --host 10.0.1.5 --port 9000 --cluster --split-brain keep-majority
actornet run --node-id b --host 10.0.1.6 --port 9000 --seed 10.0.1.5:9000 \
             --split-brain static-quorum --quorum 3
```

Sisi yang kalah mengeluarkan dirinya dari ring lalu **berhenti**. Itu bukan kegagalan menangani:
node yang terus melayani aktor yang bukan lagi miliknya adalah hal yang justru ingin dicegah
strategi ini. Untuk bergabung lagi, nyalakan ulang node-nya.

Tiga hal memikul bobotnya:

**Mayoritas dihitung terhadap keanggotaan terakhir yang disepakati semua**, bukan terhadap apa yang
bisa dilihat satu sisi sekarang. Kalau tidak, tiap belahan akan menghitung dirinya sebagai mayoritas
atas dirinya sendiri. Node yang bergabung setelah pembelahan juga tidak punya suara, kalau tidak
sebuah minoritas bisa merekayasa mayoritas dengan menyalakan node baru.

**Pembelahan sama rata diputus oleh node id terkecil.** Tanpa itu, cluster dua node akan kehilangan
kedua belahannya hanya karena satu tautan putus — lebih buruk daripada split brain yang dijaga.

**Anggota yang pamit baik-baik bukan belahan yang hilang.** Kepergian diingat terpisah dari
kegagalan; kalau tidak, mengecilkan cluster akan terbaca sebagai partisi dan yang tersisa akan
mematikan dirinya sendiri.

Jendela waktunya mencegah keputusan diambil atas keanggotaan yang masih bergerak — partisi jarang
berupa potongan rapi, dan node berguguran selama beberapa detik. Ia memakan ketersediaan sisi yang
kalah selama itu, dan membeli keputusan yang diambil sekali saja.

Ini batas jujurnya: keputusan diambil dari pandangan satu node, dan tidak ada protokol antar
belahan. Dua sisi yang berbeda pendapat soal siapa yang terjangkau bisa sama-sama merasa mayoritas,
dan itulah sebabnya `StaticQuorum` lebih aman ketika ukuran cluster-nya tetap.

### Nomor inkarnasi

Entri setiap node membawa penghitung monoton. Pandangan sebuah node tentang dirinya sendiri selalu
menang: bila seorang peer meng-gossip bahwa node ini tidak terjangkau, node itu menaikkan
inkarnasinya dan menyanggah klaim itu ke mana pun ia menyebar.

Kabar dari pihak ketiga hanya menang dengan inkarnasi yang benar-benar lebih baru — selain itu kontak
langsung yang berlaku. Ini satu-satunya bagian SWIM yang layak diambil tanpa sisanya.

## Rebalancing

Saat keanggotaan berubah, actor yang key-nya tidak lagi menjadi milik node ini dinonaktifkan:

```csharp
options.Cluster.RebalanceOnMembershipChange = true;   // bawaan
```

Itulah separuh "elastis" dari penskalaan elastis. Deaktivasi menuliskan state, dan pesan berikutnya
mengaktifkan actor itu di pemilik barunya dari store — sehingga scale-out memigrasikan kira-kira 1/N
actor dan sisanya tidak bergerak.

**Ini membutuhkan store yang bisa dibaca kedua node.** Dengan store memori bawaan — atau store file
dan SQLite, yang bersifat per-proses — actor yang berpindah tidak menemukan apa pun. Pakai PostgreSQL,
SQL Server, MySQL, atau Redis; lihat [Persistensi](05-persistensi.md).

**State yang hanya di memori tidak selamat dari rebalance.** Memigrasikan state hidup berarti protokol
serah-terima terdistribusi; store sudah menyelesaikan masalahnya.

### Menarik node keluar dengan sengaja

Menghentikan node bukan peristiwa yang sama dengan kehilangan node, dan ia mendapat satu hal yang
tidak bisa ditawarkan kegagalan: kesempatan menaruh state aktor-aktornya di tempat yang akan dilihat
pemilik berikutnya **sebelum** memberi tahu siapa pun bahwa ia akan pergi.

Karena itu `StopAsync` berjalan dalam urutan ini:

1. Menonaktifkan setiap aktor yang hidup dan menunggunya, yang menuliskan state mereka.
2. Mengumumkan kepergian, supaya peer mengeluarkan node ini dari ring.
3. Menguras sekali lagi, untuk apa pun yang aktif selagi langkah 1 berjalan — node ini masih
   memiliki kunci-kunci itu dan memang benar melayaninya.
4. Meminta tiap penerus mengaktifkan aktor yang baru saja diwarisinya.
5. Menutup transport.

Urutannya justru intinya. Mengumumkan lebih dulu berarti peer membangun ulang ring-nya dan pesan
pertama ke kunci yang berpindah mengaktifkan aktor itu dari store yang belum ditulisi node ini:
aktivasi baru berangkat dari versi basi, dan penulisan yang belum sempat terjadi mendarat di atas
apa pun yang sudah dikerjakannya. Pada store di memori jendela itu berukuran mikrodetik dan tak akan
pernah terlihat; pada basis data di seberang jaringan, selebar waktu satu penulisan.

Lalu ia menyerahkan aktor-aktornya. Untuk tiap kunci yang berpindah, node yang pergi menghitung
siapa penerimanya — entri sesudah dirinya pada preference list ring — dan meminta node itu
mengaktifkannya sekarang:

```csharp
options.WarmHandoffLimit = 1000;   // 0 untuk mematikannya
```

Tanpa ini, pesan pertama ke setiap kunci yang berpindah membayar satu pembacaan store, dan pada
restart bergilir itu berarti semua aktor yang ditahan node tersebut sekaligus, tepat ketika lalu
lintas datang. Dengan ini, penerusnya sudah memegang mereka.

Semuanya bersifat sebisanya. Penerus yang tidak menjawab akan mengaktifkan saat diminta nanti —
persis yang akan terjadi tanpa fitur ini — dan node yang sedang pergi bukan tempat yang tepat untuk
memaksakan apa pun. Batasnya ada karena ini satu pesan per aktor dan sebuah node bisa menahan sangat
banyak; melewati batas itu, sisanya aktif saat diminta.

Dengan begitu restart bergilir jadi aman sekaligus hangat. Yang masih kurang adalah *upgrade*
bergilir dalam arti yang lebih besar: tidak ada yang mengatur urutan node dimatikan, jadi menarik dua
node sekaligus masih hal yang harus dihindari operator, bukan hal yang ditolak cluster.

## Mengirim antar node

Tidak ada yang berubah di kode Anda:

```csharp
await system.TellAsync(ActorId.For<BankAccountActor>("alice"), new Deposit(100m));
```

Ring yang memutuskan. Lokal: satu penulisan channel. Remote: serialisasi, lalu serahkan ke transport.
Ask bekerja dengan cara yang sama — balasannya kembali lewat koneksi milik node yang menjawab dan
dicocokkan berdasarkan correlation id, bukan berdasarkan socket mana permintaannya tiba.

Pesan remote yang masuk selalu dikirimkan secara lokal, bahkan bila ring sudah memindahkan key itu.
Pengirimnya merutekan dengan pandangan yang ia punya, dan memantulkannya lebih jauh berisiko
menciptakan lingkaran antara dua node yang sedang berselisih selama rebalance.

## Transport

Satu listener TCP, satu koneksi keluar persisten per peer.

Koneksi berumur panjang secara sengaja. Satu koneksi per pesan berongkos handshake setiap kali, dan
di Windows menghabiskan rentang port efemeral saat beban tinggi — mode kegagalannya adalah node yang
bekerja saat demo dan mati saat benchmark. Penulisan diserialkan per koneksi, karena dua thread yang
menulis ke satu socket akan menyelipkan byte mereka menjadi frame yang tidak dikirim keduanya.

Penyambungan ulang memakai backoff eksponensial berbatas: node yang mati sejam tidak boleh dihubungi
ribuan kali per detik, dan node yang mati 200 ms tidak boleh menunggu semenit.

## Menerapkan di produksi

- **`NodeId` harus stabil dan unik.** Itulah yang di-hash ring, jadi node yang kembali dengan id
  berbeda mengambil irisan keyspace yang berbeda. Di Kubernetes, pakai nama pod dari StatefulSet,
  bukan nama acak.
- **`Host` dan `Port` harus terjangkau oleh peer**, bukan sekadar ter-bind lokal. Peer menghubungi
  alamat yang diiklankan sebuah node.
- **Seed masih berupa string statis.** Penemuan lewat DNS atau API Kubernetes ada di roadmap.
- **TLS dan autentikasi tersedia tapi mati secara bawaan.** Lihat di bawah. Sampai keduanya
  dinyalakan, jalankan cluster di jaringan tepercaya - allow-list tipe membatasi apa yang bisa
  dibuat peer, tapi itu bukan pengganti jaringan tertutup.

## Menerapkan lintas mesin

`Host` merangkap dua tugas: alamat yang di-bind listener **dan** alamat yang diberitahukan ke peer
untuk dihubungi. Itu punya satu konsekuensi yang perlu diketahui sebelum menerapkannya.

### Mesin atau VM terpisah

Setel `Host` ke alamat yang benar-benar dimiliki mesin itu dan bisa dirutekan peer:

```bash
# di mesin 10.0.1.5
actornet run --node-id a --host 10.0.1.5 --port 9000 --cluster

# di mesin 10.0.1.6
actornet run --node-id b --host 10.0.1.6 --port 9000 --seed 10.0.1.5:9000
```

![Satu cluster di tiga mesin, dilihat dari konsol](../images/console-cluster-3nodes.png)

Sudah diuji lintas tiga mesin, bukan loopback: Windows di x64, macOS 15 di Intel, dan macOS 13 di
Apple Silicon bergabung dalam satu cluster lewat LAN nirkabel, sepakat pada tabel tiga anggota yang
sama, dan saling menjawab `ask`. Hash ring-nya tidak bergantung arsitektur maupun proses, dan itulah
yang membuat node arm64 dan node x64 sepakat siapa pemilik sebuah kunci.

**macOS 15 menyaring akses jaringan lokal per biner.** Build self-contained yang dijalankan lewat
SSH mendapat `SocketException (65): No route to host` saat menghubungi peer di LAN, padahal `nc`
dari shell yang sama tersambung - alamatnya benar, aplikasinya yang ditolak. Tandatangani binernya
sebelum peluncuran pertama:

```bash
codesign -s - --force ./ActorNet.Cli
```

Itu cukup untuk uji coba. Node yang benar-benar diterapkan sebaiknya ditandatangani dengan identitas
sungguhan dan diberi izin Local Network sekali di System Settings, kalau tidak macOS akan terus
membuat jaringan yang sehat tampak seperti kegagalan routing.

### Docker atau Kubernetes

Pakai nama yang bisa diselesaikan container lain:

```yaml
services:
  node-a:
    command: run --node-id a --host node-a --port 9000 --cluster
  node-b:
    command: run --node-id b --host node-b --port 9000 --seed node-a:9000
```

`Host` yang tidak bisa di-parse sebagai IP membuat listener bind ke semua antarmuka sambil tetap
mengiklankan namanya, dan itu persis yang dibutuhkan container. Sudah diuji dengan hostname di satu
mesin; belum diuji lintas container sungguhan.

**Sebuah nama seed mewakili semua alamat di baliknya.** Headless service Kubernetes menerjemah
menjadi satu alamat per pod, jadi satu entri seed sudah menjadi seluruh konfigurasi cluster:

```yaml
# Headless service - clusterIP: None - memberi satu record A per pod yang siap.
command:
  - run
  - --host=0.0.0.0
  - --advertised-host=$(POD_IP)
  - --port=9000
  - --seed=actornet-headless.default.svc.cluster.local:9000
```

Setiap alamat hasil penerjemahan nama itu dikirimi join, bukan hanya record yang kebetulan kembali
lebih dulu. Itu penting saat rollout: menghubungi namanya saja hanya menyentuh satu pod, dan bila pod
itu kebetulan yang masih menyala, node-nya akan menunggu denyut berikutnya lalu mengulang undian yang
sama.

Nama diterjemahkan ulang pada setiap percobaan, jadi pod yang muncul belakangan ikut terjaring tanpa
restart; dan nama yang gagal diterjemahkan tetap dihubungi apa adanya — salah ketik muncul sebagai
error koneksi yang menyebut seed-nya, bukan sebagai seed yang diam-diam berhenti dicoba. Setel
`Cluster.ResolveSeedHostnames = false` untuk kembali menyerahkan pencarian nama ke proses connect.

### Mem-bind satu alamat dan mengiklankan alamat lain

`Host` dan `Port` adalah yang di-bind listener. `AdvertisedHost` dan `AdvertisedPort` adalah yang
diberitahukan ke peer untuk dihubungi. Biarkan pasangan iklan kosong dan pasangan bind yang dipakai,
dan itu tepat setiap kali sebuah node terikat ke alamat yang sudah bisa dirutekan peer.

Keduanya berbeda dalam dua kasus yang umum:

```bash
# Terima di semua antarmuka, tapi beri tahu peer alamat yang bisa dirutekan.
actornet run --node-id a --host 0.0.0.0 --advertised-host 10.0.1.5 --port 9000 --cluster

# Bind 9000 di dalam container yang mempublikasikannya sebagai 19000.
actornet run --node-id a --host 0.0.0.0 --advertised-host node-a.example.com   --port 9000 --advertised-port 19000 --cluster
```

Sudah diuji: dua node terikat ke `0.0.0.0`, mengiklankan alamat LAN, konvergen tanpa satu pun
percobaan koneksi yang gagal.

**Mengiklankan alamat bind ditolak saat start.** `0.0.0.0`, `::`, dan `*` berarti "semua antarmuka"
bagi listener dan tidak berarti apa pun bagi yang menghubungi, jadi node ber-cluster yang
dikonfigurasi begitu langsung gagal dengan pesan yang menyebutkan perbaikannya — alih-alih tetap
jalan, ditemukan sekali, lalu ditandai `Unreachable` padahal sehat.

Port `0` tidak masalah: port sesungguhnya baru diketahui setelah listener naik, dan itulah yang
diiklankan.

## Format kabel biner

JSON adalah bawaan yang tepat — bisa dibaca langsung pada tangkapan paket, dan itulah yang dipahami
klien Go, Python, dan Node — sekaligus pilihan yang keliru untuk lalu lintas antar node, yang tak
dibaca siapa pun dan isinya kebanyakan string pendek yang berulang: alamat tujuan, node pengirim,
alias pesan, id korelasi. Dalam JSON masing-masing membawa kunci berkutip, tanda kutip, dan koma.

```csharp
options.WireFormat = WireFormat.Binary;   // bawaannya Json
```

Diukur di mesin ini, hanya amplopnya:

| Frame | JSON | Biner | |
| --- | --- | --- | --- |
| `tell` dengan payload kecil | 179 B | 94 B | 47% lebih kecil |
| `ask` dengan id korelasi | 202 B | 125 B | 38% lebih kecil |
| `ask` yang sama membawa trace | 255 B | 182 B | 29% lebih kecil |
| Satu denyut gossip, lima anggota | 364 B | 115 B | 68% lebih kecil |

Gossip paling banyak untungnya, dan itu justru yang paling kecil artinya per frame dan paling besar
artinya secara total: ia lalu lintas yang tidak pernah berhenti.

**Payload-nya tetap JSON.** Yang disandikan di sini adalah amplop di sekelilingnya. Membuat badan
pesannya ikut biner berarti satu codec per tipe untuk setiap pesan terdaftar — perkara yang jauh
lebih besar, dan tak satu pun klien lintas bahasa bisa mengikutinya. Mengatakan "format kabel biner"
padahal yang dimaksud amplopnya perlu disebutkan terus terang.

**Tidak ada yang dinegosiasikan.** Sebuah frame menyatakan sendiri sandinya — JSON diawali `{`, biner
diawali `0xAC` — dan sebuah koneksi dijawab dengan sandi yang dipakai menyapanya. Jadi:

- sebuah cluster bisa dipindahkan dari satu setelan ke setelan lain **satu node sekali jalan**,
  dengan kedua belahannya tetap saling bicara sepanjang proses;
- klien SDK tidak terpengaruh bagaimanapun ini disetel, karena mereka menyapa node dengan JSON dan
  dijawab dengan JSON.

Setelan ini hanya menentukan apa yang ditulis sebuah node ketika ia membuka koneksi. Tidak ada klien
biner di keempat SDK itu.

## Mengamankan cluster

Enkripsi dan autentikasi keduanya mati secara bawaan — itulah sebabnya catatan penerapan menyarankan
menjaga cluster di jaringan tepercaya sampai keduanya dinyalakan. Keduanya menjawab pertanyaan
berbeda dan berdiri sendiri-sendiri.

### Autentikasi: shared secret

```bash
actornet run --node-id a --port 9000 --cluster --secret "$ACTORNET_SECRET"
actornet run --node-id b --port 9001 --seed 10.0.1.5:9000 --secret "$ACTORNET_SECRET"
```

```csharp
options.Security.SharedSecret = Environment.GetEnvironmentVariable("ACTORNET_SECRET");
```

**Secret-nya tidak pernah dikirim.** Sisi yang mendengarkan menawarkan nonce acak, sisi yang
menyambung menjawab dengan HMAC atasnya, dan jawabannya dibandingkan dalam waktu tetap. Pengamat
pasif hanya mendapat sebuah nonce dan sebuah MAC, dan keduanya tidak bisa dipakai ulang — jadi ini
aman dijalankan tanpa TLS, dan operator bisa menyalakan autentikasi tanpa harus lebih dulu
menyelesaikan urusan distribusi sertifikat.

Inilah yang mencegah *proses tak berwenang* bergabung ke cluster. Tanpa itu, apa pun yang bisa
menjangkau port-nya bisa mengirim `Join` dan mulai menerima actor. Secret di bawah 16 karakter
ditolak saat start.

Sudah diuji: node yang dijalankan dengan secret salah tidak pernah muncul di tabel member, dan
penolakannya tercatat di node yang menolaknya.

### Enkripsi: TLS

```bash
actornet run --node-id a --port 9000 --cluster   --tls-cert ./node.pfx --tls-password "$PFX_PASSWORD" --tls-pin A1B2C3...
```

```csharp
options.Security.ServerCertificate = X509CertificateLoader.LoadPkcs12FromFile("node.pfx", password);
options.Security.PinnedThumbprint("A1B2C3…");     // atau isi RemoteCertificateValidation
```

TLS 1.2 atau 1.3, dinegosiasikan sebelum satu frame pun dibaca.

Node cluster biasanya menyajikan sertifikat dari CA privat atau yang ditandatangani sendiri, dan
bawaan platform akan menolaknya — jadi pin thumbprint-nya, atau isi validasi Anda sendiri.
`AcceptAnyCertificate()` ada untuk pengembangan dan sengaja dibuat sebagai metode bernama alih-alih
sebuah flag supaya mudah dicari saat review: ia mengenkripsi lalu lintas dan tidak mengautentikasi
siapa pun.

**Semua node harus sepakat soal TLS.** Node yang menyalakannya tidak bisa bicara dengan yang
mematikannya, dan kegagalannya berupa galat handshake alih-alih sesuatu yang halus — cluster yang
setengah bermigrasi gagal dengan nyaring. Sebarkan sertifikatnya ke semua node sebelum menyalakannya
di mana pun.

### Mutual TLS

```csharp
options.Security.RequireClientCertificate = true;
options.Security.ClientCertificate = X509CertificateLoader.LoadPkcs12FromFile("node.pfx", password);
```

Pilihan terkuat sekaligus paling banyak kerjanya: setiap node butuh sepasang kunci dan cara
merotasinya. Shared secret adalah jawaban yang lebih murah untuk pertanyaan yang sama, dan keduanya
bisa digabung.

### Backpressure melintasi batas node

`MailboxCapacity` bawaannya tak berbatas, jadi tidak ada dari ini yang aktif sampai Anda membatasi
sebuah mailbox. Begitu dibatasi, jalur lokal dan remote harus berbeda:

**Pengirim lokal menunggu.** Thread yang diperlambat adalah thread yang menghasilkan pekerjaannya,
dan itu persis tugas backpressure.

**Pengirim remote tidak bisa diperlambat dengan cara yang sama.** Thread yang akan terblokir adalah
reader milik koneksi itu, dan lalu lintas semua aktor lain di koneksi tersebut mengantre di
belakangnya — satu aktor sibuk akan menghentikan seluruh node. Jadi pengiriman masuk menunggu, tapi
hanya selama `RemoteDeliveryTimeout` (bawaan 5 detik). Lewat itu pesannya menjadi dead letter
`MailboxFull` dan ask yang menunggu dijawab dengan `MailboxFullException`.

```csharp
options.MailboxCapacity = 10_000;                          // memilih memakai backpressure
options.RemoteDeliveryTimeout = TimeSpan.FromSeconds(5);   // berapa lama pesan masuk boleh menunggu
options.SendTimeout = TimeSpan.FromSeconds(30);            // berapa lama pengiriman menunggu antrean
options.OutboundQueueCapacity = 8_192;                      // frame yang ditampung per peer
```

Jalur masuk sengaja tetap berurutan. Mengirimkannya secara bersamaan akan membuat pesan yang dibaca
belakangan bisa sampai lebih dulu, dan itu melanggar jaminan bahwa pesan dari satu pengirim ke satu
aktor tiba berurutan. Kompromi jujurnya adalah stall yang **berbatas**, bukan tanpa stall: lalu
lintas lain di koneksi itu menunggu paling lama `RemoteDeliveryTimeout` di belakang mailbox penuh.

Sebuah `tell` yang ditolak begini dicatat di node *penerima*, karena `tell` tidak punya pemanggil
untuk diberi tahu. Sebuah `ask` dijawab di kedua sisi.

### Membedakan kemacetan, gangguan, dan aktor lambat

Tiga kegagalan yang dulu tampak serupa, padahal menuntut respons berbeda:

| | Artinya | Lakukan |
| --- | --- | --- |
| `NodeUnreachableException` | Peer-nya mati atau tak pernah terjangkau | Rutekan ke tempat lain; periksa halaman cluster |
| `NodeCongestedException` | Peer-nya hidup tapi tidak sanggup mengejar | Kirim lebih sedikit, atau naikkan `SendTimeout` untuk lonjakan yang wajar |
| `MailboxFullException` | Satu aktor tidak sanggup mengejar | Periksa aktor itu, bukan jaringannya |
| `AskTimeoutException` | Pesannya sampai dan balasannya tidak datang | Periksa handler-nya |

Antrean penuh pada peer yang **belum pernah tersambung** dilaporkan sebagai tidak terjangkau, bukan
macet — "kirim lebih sedikit" adalah saran yang salah untuk node yang memang mati.

Kemacetan juga tersedia sebagai metrik `actornet.node.congested`, ditandai nama node-nya. Layak
diberi alert terpisah dari dead letter: dead letter biasanya berarti salah perakitan, kemacetan
berarti cluster-nya membawa lebih banyak daripada yang sanggup diterima sebuah peer.

## Batas yang diketahui

- **Resolusi split-brain mati secara bawaan, dan sepihak.** `KeepMajority` dan `StaticQuorum`
  tersedia, tapi tiap node memutuskan sendiri dari pandangannya; tidak ada protokol antar belahan
  untuk menyepakati siapa yang kalah.
- **Kecurigaan bersifat per-node, dan tidak ada yang mendamaikan dua node yang berbeda pendapat.**
  Phi diukur terhadap riwayat masing-masing peer, jadi satu node bisa menyebut sebuah peer tidak
  terjangkau sementara node lain tidak. Itu jujur — keterjangkauan memang tidak simetris — tapi
  belum ada protokol untuk menyelesaikannya.
- **`PreferenceList` ada dan tidak dipakai apa pun.** Penempatan replika belum diimplementasikan.

Ketiganya ada di [roadmap](../../Plan.md).

## Selanjutnya

- [Persistensi](05-persistensi.md) — kenapa store bersama adalah prasyaratnya
- [Perkakas](09-perkakas.md) — menyaksikan sebuah cluster konvergen
