# Galeri

`samples/LVGL.Gallery` menunjukkan LVGL.Net bekerja pada jenis layar yang benar-benar dibuat orang:
tujuh belas demo dalam enam kasus penggunaan, masing-masing satu berkas C# yang bisa dibaca dari
dalam aplikasinya sendiri. Galeri berjalan di jendela desktop dan, tanpa perubahan, di layar sentuh
Raspberry Pi.

```bash
dotnet run --project samples/LVGL.Gallery                        # jendela (butuh SDL2)
dotnet run --project samples/LVGL.Gallery -- --demo mandelbrot   # buka satu demo
dotnet run --project samples/LVGL.Gallery -- --list              # daftar demo
dotnet run --project samples/LVGL.Gallery -- --drm               # layar Pi, tanpa desktop
dotnet run --project samples/LVGL.Gallery -- --screenshots out   # setiap demo sebagai PNG, tanpa layar
```

![Galeri: Mandelbrot, di kategori Scientific](../images/screenshots/gallery-scientific-mandelbrot.png)

Rel di kiri berisi daftar kasus penggunaan. Kategori yang dipilih mewarnai bidang di sebelah kanan,
dan cipnya menyambung ke bidang itu seperti tab map. Di bagian atas ada demo-demo kategori tersebut,
dan **Demo / Source** mengganti lembar antara demo yang berjalan dan berkas sumbernya sendiri,
diwarnai sintaksnya dengan fitur recolour label LVGL:

![Tampilan Source](../images/screenshots/gallery-source.png)

## Daftar demo

| Kasus | Demo | Yang ditunjukkan | Dibangun dengan |
|---|---|---|---|
| Scientific | Function plotter | Lima keluarga fungsi (gelombang kotak Fourier, cosinus teredam, sinc, Gaussian, Lissajous) digambar ulang setiap slider bergerak | `LvCanvas`, `LvSlider` |
| | Oscilloscope | Jejak dua kanal dari generator sinyal; frekuensi, peak-to-peak dan RMS diukur dari sampelnya | `LvChart`, `LvTimer` |
| | Mandelbrot | Ketuk untuk zoom; dirender per pita baris di antara frame agar UI tetap responsif di Pi | `LvCanvas`, `TryGetPointer` |
| Work & data | Inventory | Daftar stok di SQLite: cari, ubah jumlah, tambah, hapus, dengan keyboard layar | `LvTextArea`, `LvKeyboard`, SQLite |
| | Task board | Tiga kolom pekerjaan; ketukan memindahkan kartu dan menyimpan statusnya | layout flex, `LvBar`, SQLite |
| | Sales report | Penjualan setahun yang dikelompokkan dengan SQL, sebagai diagram batang, bisa difilter per wilayah | `LvChart` (bar), SQLite |
| Multimedia | Music player | Transport, seek bar yang bisa digeser, volume, playlist; sampul dibuat per lagu | `LvCanvas`, `LvSlider` |
| | Visualizer | Penganalisis spektrum 48 pita pada ketukan sintetis, tiga tampilan, tanpa alokasi per frame | `LvCanvas`, `LvTimer` |
| | Photo lab | Delapan gaya dan tiga penyesuaian, pembatas sebelum/sesudah dan histogram RGB langsung | `LvCanvas`, `Span<uint>` |
| Games | Snake | Tombol panah, usapan, atau pad di layar | `LvCanvas`, `LvKey`, `GestureDirection` |
| | 2048 | Geser dan gabungkan ubin; penggabungan diberi animasi | `LvPanel`, `LvAnimation` |
| | Noughts and crosses | Melawan pencarian minimax yang tidak bisa kalah | `LvButton`, `LvTimer` |
| Tools | Calculator | Kurung dan prioritas operator, dengan pita kertas hasil; ketik atau ketuk | `LvButton`, `LvGroup` |
| | Unit converter | Enam besaran; satu nilai ditampilkan dalam setiap satuan sejenis | `LvDropdown` |
| | Stopwatch | Seperseratus detik di cincin 60 detik; lap tercepat dan terlambat ditandai | `LvArc`, `LvTimer` |
| Home & IoT | Smart home | Ruangan, dial termostat, lampu yang bisa diredupkan, skenario untuk seluruh rumah | `LvArc`, `LvSwitch`, `LvLed` |
| | Energy monitor | Surya lawan konsumsi, baterai, gauge jaringan bertitik nol di tengah, total hari ini | `LvChart`, `LvScale`, `LvTable` |

<table>
<tr>
<td><img src="../images/screenshots/gallery-scientific-oscilloscope.png" alt="Oscilloscope" /></td>
<td><img src="../images/screenshots/gallery-work-inventory.png" alt="Inventory" /></td>
</tr>
<tr>
<td><img src="../images/screenshots/gallery-multimedia-photo-lab.png" alt="Photo lab" /></td>
<td><img src="../images/screenshots/gallery-games-2048.png" alt="2048" /></td>
</tr>
<tr>
<td><img src="../images/screenshots/gallery-tools-calculator.png" alt="Calculator" /></td>
<td><img src="../images/screenshots/gallery-home-energy-monitor.png" alt="Energy monitor" /></td>
</tr>
</table>

Semua screenshot ada di `docs/images/screenshots/gallery-*.png`.

## Data

Demo Work menyimpan datanya di berkas SQLite - `%LOCALAPPDATA%\LVGL.Net\gallery.db` di Windows,
`~/.local/share/LVGL.Net/gallery.db` di Linux - yang dibuat dan diisi data awal saat pertama dipakai,
sehingga perubahan tetap ada setelah aplikasi dibuka ulang. `--db <berkas>` memakai berkas lain;
hapus berkasnya untuk mulai dari awal. Kuerinya ADO.NET biasa (`Microsoft.Data.Sqlite`) di
`Demos/Work/GalleryDatabase.cs`, yang membawa SQLite native untuk setiap platform termasuk
`linux-arm64`.

## Susunan sebuah demo

Sebuah demo diturunkan dari `Demo` dan dibangun di atas panggung (stage) yang diberikan galeri:

```csharp
internal sealed class Stopwatch : Demo
{
    public override Category Category => Category.Tools;
    public override string Title => "Stopwatch";
    public override string Summary => "...";
    public override string BuiltWith => "LvArc  LvTimer  LvButton  LvLabel";

    protected override void Build(LvObject stage, int width, int height)
    {
        _ring = new LvArc(stage);
        // ...
        Every(33, Refresh);   // timer yang berhenti bersama demonya
    }
}
```

Tiga aturan membuat perpindahan antar demo bebas kebocoran, dan ketiganya adalah aturan LVGL.Net
sendiri:

- **Galeri menghapus widget dulu, baru memanggil `Unmount()`.** Semua yang dibuat demo tetapi
  tidak dimiliki LVGL - timer, style, gambar - lewat `Track(...)` atau `Every(...)`, dan `Unmount`
  membebaskannya setelah widget-nya hilang.
- **Instans baru setiap kali dibuka.** Shell membuat objek demo baru setiap kali demo dibuka,
  sehingga demo selalu mulai dari keadaan awalnya.
- **Jangan hapus widget yang handler-nya sedang berjalan.** Klik yang membangun ulang daftar
  menjadwalkan pembangunan ulang itu dengan `App.Post(Refresh)`, bukan memanggilnya langsung.

`Context.Screenshot` bernilai true saat `--screenshots`; demo memakainya untuk menampilkan keadaan
yang representatif - permainan yang sedang berjalan, zoom yang dalam - agar gambar dokumentasi
selalu sama.

### Menambah demo

1. Tambahkan kelas di `Demos/<Kategori>/`. Berkasnya otomatis disematkan sebagai resource - itulah
   yang ditampilkan tampilan Source.
2. Tambahkan ke `DemoCatalog.Create()`.
3. Jalankan `--demo <judul>` untuk mencobanya, dan `--screenshots` untuk memeriksa gambarnya.

`GalleryIntegrationTests` memasang setiap demo pada LVGL sungguhan, membiarkan timer-nya berjalan,
membongkarnya, lalu membangun tampilan Source-nya, dan gagal jika ada handler atau timer yang
melempar exception. CI juga menjalankan `--screenshots`, yang keluar dengan kode bukan nol jika ada
demo yang bermasalah.

## Font

Galeri memakai tipografinya sendiri: Barlow Condensed untuk judul dan angka besar, Barlow untuk teks
dan Martian Mono untuk kode dan data, dirasterisasi saat runtime oleh TinyTTF milik LVGL (keduanya
berlisensi SIL Open Font License; lihat `Assets/Fonts`). Glif simbol LVGL ada di Montserrat bawaan,
jadi ikon dan panah dropdown memakai font itu - lihat `Demo.Icon`. Jika berkas font tidak ada,
galeri kembali ke Montserrat sehingga tetap berjalan dari direktori kerja yang tidak biasa.
