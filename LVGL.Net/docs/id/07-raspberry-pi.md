# Panduan Raspberry Pi

Ditujukan untuk Raspberry Pi 4 dengan Raspberry Pi OS 64-bit. Pi 3 atau Zero 2 W juga bisa; hanya
penyetelan compiler `--pi4` yang spesifik papan.

## Contohnya

`samples/LVGL.Raspi` membaca sensor bawaan papan dan menggambarkannya secara langsung:

- Beban CPU, dari `/proc/stat` (selisih antar sampel — pembacaan pertama bernilai nol)
- Suhu SoC, dari `/sys/class/thermal/thermal_zone0/temp`
- Tekanan memori, dari `/proc/meminfo`
- Waktu nyala, dari `/proc/uptime`

Tanpa perangkat keras tambahan dan tanpa paket tambahan. Pengambilan sampel berjalan di thread latar,
karena membaca berkas-berkas itu menyentuh sistem berkas dan run loop yang terblokir langsung terlihat
sebagai grafik yang tersendat. Hasilnya sampai ke UI lewat `app.Post(...)`.

![Dasbor, dirender di luar layar dengan sensor simulasi](../images/screenshots/raspi-dashboard.png)

## Menyiapkan Pi

```bash
sudo apt update
sudo apt install -y cmake build-essential git

# .NET 10
curl -sSL https://dot.net/v1/dotnet-install.sh | bash /dev/stdin --channel 10.0
echo 'export PATH=$PATH:$HOME/.dotnet' >> ~/.bashrc
source ~/.bashrc

# Framebuffer dan perangkat input dimiliki oleh grup
sudo usermod -aG video,input $USER
```

Keluar dan masuk kembali agar perubahan grup berlaku.

## Build dan jalankan

```bash
git clone <repositori-anda> && cd LVGL.Net
./native/build.sh --pi4
dotnet run --project samples/LVGL.Raspi -c Release
```

Opsi:

```bash
--sdl              # render ke jendela (butuh sesi desktop)
--drm [device]     # DRM/KMS dengan page flipping - tanpa tearing; butuh layar sendirian
--simulate         # telemetri palsu, untuk mengembangkan tanpa Pi
--fb /dev/fb1      # perangkat framebuffer lain
--interval 500     # interval pengambilan sampel dalam ms
--bench 30         # ukur selama 30 dtk dan cetak laporan JSON (lihat di bawah)
--headless         # render di luar layar (dengan --bench, di mesin tanpa layar)
--screenshot a.png # render dengan data simulasi dan simpan sebagai PNG
```

Tanpa `--sdl` atau `--drm`, contoh ini membuka `/dev/fb0`, mendeteksi perangkat sentuh atau tetikus
secara otomatis, dan jatuh kembali ke jendela SDL bila framebuffer tidak tersedia — sehingga biner
yang sama bekerja lewat SSH dengan X forwarding maupun langsung di panel.

**Utamakan `--drm` saat boot ke konsol.** Pada Raspberry Pi OS terkini, layar berjalan di atas driver
KMS dan `/dev/fb0` hanyalah lapisan emulasi: menggambar ke dalamnya langsung terlihat saat proses
berlangsung dan terjadi tearing saat ada gerakan. `--drm` merender ke buffer tersembunyi dan membalik
buffer saat vertical blank. Opsi ini membutuhkan layar untuk dirinya sendiri, jadi bila desktop
berjalan di layar yang sama, tetap pakai backend framebuffer atau SDL.

## Catatan framebuffer

**Kursor konsol terus berkedip di atas gambar:**

```bash
sudo sh -c 'echo 0 > /sys/class/graphics/fbcon/cursor_blink'
```

**Paksa mode 32-bit.** Mode 16-bit tetap bekerja, tetapi menambah konversi per piksel pada setiap
blit. Dalam `/boot/firmware/config.txt`:

```ini
framebuffer_depth=32
framebuffer_ignore_alpha=1
```

**Kunci resolusi** agar framebuffer tidak berubah ukuran saat monitor dicabut:

```ini
hdmi_group=2
hdmi_mode=87
hdmi_cvt=800 480 60 6 0 0 0
```

## Publish tanpa SDK

```bash
dotnet publish samples/LVGL.Raspi -c Release -r linux-arm64 --self-contained
```

Lalu salin `bin/Release/net10.0/linux-arm64/publish/` ke Pi bersama
`runtimes/linux-arm64/native/liblvglnet.so`. Proyek ini sudah menyetel `PublishTrimmed`, yang menjaga
ukuran keluaran tetap nyaman untuk dipindahkan lewat jaringan.

## Menjalankan saat boot

`/etc/systemd/system/lvgl-dashboard.service`:

```ini
[Unit]
Description=Dasbor LVGL.Net
After=multi-user.target

[Service]
Type=simple
User=pi
SupplementaryGroups=video input
WorkingDirectory=/home/pi/dashboard
ExecStart=/home/pi/dashboard/LVGL.Raspi
Restart=always
RestartSec=5

[Install]
WantedBy=multi-user.target
```

```bash
sudo systemctl enable --now lvgl-dashboard
journalctl -u lvgl-dashboard -f
```

## Kinerja

Nilai bawaan yang berpengaruh di Pi, beserta alasannya:

| Pengaturan | Nilai | Alasan |
|------------|-------|--------|
| `RenderMode` | `Partial` | Dua potongan kecil, bukan dua buffer sepenuh layar |
| `PartialBufferLines` | 32 pada contoh | 200 KiB (dua potongan 100 KiB) pada 800×480, bukan 3 MiB untuk dua buffer sepenuh layar |
| `ServerGarbageCollection` | mati | Server GC memakan memori tanpa manfaat pada papan kecil |
| `ConcurrentGarbageCollection` | mati | Thread GC latar bersaing dengan proses render |
| `LV_DRAW_SW_DRAW_UNIT_CNT` | 1 | Thread gambar tambahan umumnya hanya menambah rebutan pada empat inti |
| `InvariantGlobalization` | aktif | Menghilangkan dependensi ICU |

Bila UI terasa lambat:

1. Periksa `app.Display.FlushCount` — angka tinggi per frame berarti banyak invalidasi kecil. Widget
   yang bertumpuk atau beranimasi terus-menerus adalah penyebab yang lazim.
2. Jalankan benchmark bawaan (halaman *LVGL demos* pada contoh desktop) untuk memisahkan biaya
   rendering dari biaya aplikasi.
3. Utamakan latar polos dibanding gradien, dan hindari bayangan buram yang besar: keduanya mahal pada
   rendering perangkat lunak.
4. Naikkan `PartialBufferLines` bila memori masih longgar — blit lebih sedikit dan lebih besar.

## Mengukur: `--bench`

`--bench <detik>` menjalankan dasbor dengan kecepatan penuh - sampel simulasi setiap 50 ms, sehingga
gauge, label, dan chart terus digambar ulang - lalu melaporkan hasilnya:

```bash
dotnet run --project samples/LVGL.Raspi -c Release -- --drm --bench 30 --report bench.json
```

Sebuah laporan hasil pengukuran (desktop x64 yang merender di luar layar dengan `--headless`,
disimpan di `docs/benchmarks/dashboard-headless-windows-x64.json`):

```json
{
  "Lvgl": "9.6.0",
  "Backend": "HeadlessBackend",
  "Width": 800,
  "Height": 480,
  "FramesPerSecond": 63.6,
  "FrameMsMedian": 0.013,
  "FrameMsP95": 1.538,
  "FrameMsP99": 2.081,
  "BytesAllocatedPerFrame": 53.2,
  "Gen0Collections": 0,
  "DrawBufferKiB": 200,
  "WorkingSetMiB": 34.7
}
```

Waktu frame adalah waktu di dalam `RunFrame` - timer, layout, rendering, dan blit - tanpa waktu tidur
saat idle; median yang mendekati nol berarti sebagian besar putaran loop tidak punya apa pun untuk
digambar ulang. Angka alokasi berasal dari pemformatan label milik dasbor sendiri: jalur render itu
sendiri tidak mengalokasikan apa pun, dan hal ini ditegakkan oleh sebuah test
(`The_render_path_does_not_allocate`). Ini bukan angka Pi; proses hardware-in-the-loop di bawah ini
menghasilkan angka Pi yang sesungguhnya.

## Hardware-in-the-loop

`.github/workflows/lvgl-net-pi4.yml` menjalankan seluruh rangkaian test dan bench **di Pi 4
sungguhan**, setiap minggu dan sesuai permintaan. Workflow ini membutuhkan runner GitHub Actions
self-hosted di Pi:

1. Di Pi, ikuti langkah *Settings → Actions → Runners → New self-hosted runner* dari GitHub untuk
   Linux ARM64, dan beri runner tersebut label tambahan `pi4`.
2. Pasang sebagai service (`sudo ./svc.sh install && sudo ./svc.sh start`) di bawah pengguna yang
   tergabung dalam grup `video` dan `input`, dengan CMake dan compiler terpasang seperti pada
   *Menyiapkan Pi*.
3. Boot ke konsol (atau hentikan desktop) agar `--drm` dapat mengambil alih layar; bila tidak,
   workflow melakukan benchmark di luar layar dan menyebutkannya pada kolom `Backend` di laporan.

Setiap proses membangun library native dengan `--pi4`, menjalankan setiap test terhadapnya dengan
`LVGLNET_REQUIRE_NATIVE=1`, dan mengunggah `bench-pi4.json`.

## Kalibrasi sentuh

`EvdevPointer` menskalakan ulang koordinat absolut memakai rentang yang dilaporkan driver melalui
`EVIOCGABS`, yang sudah benar untuk sebagian besar panel. Bila sentuhan mendarat di tempat yang salah,
pastikan panel dan framebuffer sepakat soal orientasi — layar yang diputar memerlukan rotasi yang
diterapkan di `/boot/firmware/config.txt` agar keduanya melihat geometri yang sama.
