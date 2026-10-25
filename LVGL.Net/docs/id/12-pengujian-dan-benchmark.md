# Pengujian dan benchmark

## Rangkaian test

| Proyek | Framework | Cakupan |
|---|---|---|
| `tests/LVGL.Net.Tests` | xUnit | Library: unit test, integration test terhadap build LVGL sungguhan, model layout, generator kode, plugin asisten, ABI kernel milik backend DRM, referensi API |
| `tests/LVGL.Designer.Tests` | xUnit v3 + Avalonia.Headless | Designer: model penyuntingannya, geometri pengubahan ukuran, dan jendela sungguhan yang digerakkan dengan pointer dan papan ketik |

```bash
dotnet test LVGL.Net.slnx
```

### Tanpa dan dengan library native

Unit test tidak memerlukan apa pun; integration test memeriksa ketersediaan LVGL dan berhenti lebih
awal bila tidak ada, sehingga rangkaian test tetap berjalan di mesin tanpa CMake. Setelah library
native dibangun, jalankan dengan

```bash
LVGLNET_REQUIRE_NATIVE=1 dotnet test LVGL.Net.slnx
```

agar library yang gagal dimuat menggagalkan proses test, alih-alih setiap integration test diam-diam
lulus sebagai test yang terlewati. CI selalu berjalan dengan cara ini.

### Apa yang dibuktikan integration test

- **Rendering.** Warna yang disetel melalui shim ABI kembali persis sama pada piksel hasil render;
  gambar dari piksel managed, berkas PNG, dan berkas BMP merender warnanya dengan benar; PNG yang
  ditulis `HeadlessBackend.SavePng` dibaca kembali oleh decoder milik LVGL sendiri dan warnanya tetap
  benar.
- **ABI.** Setiap enum managed yang mencerminkan enum LVGL dibandingkan, anggota demi anggota, dengan
  nilai di build yang dimuat (`LvglConstantsTests`); setiap event code dapat diterjemahkan; penyandian
  koordinat sesuai dengan `lv_pct` milik LVGL sendiri.
- **Daur hidup.** Menghapus sebuah widget mematikan wrapper-nya dan wrapper anak-anaknya; menghapus
  target sebuah animasi menghentikan animasinya; style, font, dan gambar dibebaskan setelah
  widget-nya.
- **Jalur render tidak mengalokasikan.** 150 frame yang digambar ulang - dengan chart yang mengalir,
  bar yang berubah, serta sebuah animasi dan timer yang berjalan - harus mengalokasikan tepat 0 byte
  di heap managed.
- **Layout.** Dokumen "kitchen sink" dengan setiap jenis widget, style bersama, sebuah font, dan
  sebuah aset gambar dibangun menjadi widget hidup; style bersama sampai ke layar; aset yang hilang
  menjadi diagnostik, bukan crash.

### Kode hasil generate dapat dikompilasi

Test generator kode mengompilasi keluarannya dengan Roslyn terhadap assembly `LVGL.Net` yang
sesungguhnya - untuk setiap jenis widget secara terpisah, untuk layout kitchen sink, dan bersama
belahan handler event yang ditulis asisten. Perubahan generator yang menghasilkan kode yang tidak bisa
di-build akan gagal di sini.

### Designer, digerakkan layaknya pengguna

Test designer membuka jendela sungguhan di platform headless Avalonia, dengan LVGL merender
preview-nya, lalu memakai input pointer dan papan ketik: klik sebuah widget di board dan periksa
seleksinya; seret lalu periksa bahwa ia berpindah ke grid dan dapat di-undo dalam satu langkah; tarik
pad tembaga dan periksa bahwa tepi seberangnya tetap diam; ketik nama di inspector dan periksa bahwa
undo mengembalikan seluruh kata; terapkan JSON yang disunting di mode Code; buka panel asisten. Satu
test membaca piksel di tengah sebuah tombol dan memeriksa bahwa warnanya adalah warna yang digambar
LVGL.

Geometri pengubahan ukuran juga diuji tersendiri, terhadap transkripsi independen dari aturan
perataan LVGL, untuk setiap perataan dan setiap tepi (176 kasus).

### Terhadap model sungguhan

Opt-in, karena membutuhkan kunci API dan memakan satu request - lihat
[bab asisten](09-asisten.md#pengujian-dengan-model-sungguhan).

## Screenshot

Setiap screenshot dalam dokumen-dokumen ini dihasilkan secara otomatis, sehingga bisa diperbarui
setelah ada perubahan alih-alih diambil ulang secara manual:

```bash
dotnet run --project samples/LVGL.Desktop -c Release -- --screenshots docs/images/screenshots
dotnet run --project samples/LVGL.Raspi -c Release -- --screenshot docs/images/screenshots/raspi-dashboard.png
LVGLNET_DOC_SCREENSHOTS=$PWD/docs/images/screenshots dotnet test tests/LVGL.Designer.Tests --filter DocumentationScreenshots
```

Gambar designer memakai `samples/layouts/smart-home.lvgl.json`. Screenshot asisten berasal dari test
live, dengan `LVGLNET_DOC_SCREENSHOTS` juga disetel.

## Benchmark

`benchmarks/LVGL.Net.Benchmarks` mengukur panggilan yang dilakukan dasbor hidup setiap frame dengan
BenchmarkDotNet, baik waktu maupun alokasi managed:

```bash
dotnet run -c Release --project benchmarks/LVGL.Net.Benchmarks -- --filter '*' --job short
```

Diukur pada Intel Core i7-8650U, .NET 10.0.12, LVGL 9.6.0, 800 × 480
(`docs/benchmarks/render-path.md`):

| Panggilan | Rata-rata | Alokasi |
|---|---:|---:|
| `ChartAddPoint` | 129 ns | 0 B |
| `SetBarValue` | 133 ns | 0 B |
| `SetBackgroundColor` (melalui shim warna) | 272 ns | 0 B |
| `RunFrameIdle` (tidak ada yang perlu digambar ulang) | 71 ns | 0 B |
| `RedrawFullScreen` (invalidasi dan render 800 × 480) | 544 µs | 0 B |
| `SetLabelText` | 300 ns | 0 B |

`SetLabelText` juga tidak mengalokasikan apa pun, karena `[LibraryImport]` me-marshal string pendek
ke UTF-8 di stack.

Mode `--bench` pada contoh Pi mengukur seluruh dasbor dari ujung ke ujung; lihat
[panduan Raspberry Pi](07-raspberry-pi.md#mengukur---bench).

## Referensi API

`docs/api` dihasilkan dari dokumentasi XML oleh `tools/LVGL.ApiDocs` (referensinya dalam bahasa
Inggris):

```bash
dotnet run --project tools/LVGL.ApiDocs
```

Sebuah test menghasilkannya ulang ke folder sementara dan gagal bila salinan yang di-commit berbeda,
sehingga perubahan pada API publik tanpa menghasilkan ulang referensinya tidak akan lolos CI.
