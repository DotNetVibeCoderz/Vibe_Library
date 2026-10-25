# Pemecahan masalah

## `LvglNativeNotFoundException`

Library native belum dibangun, atau dibangun di tempat yang tidak diperiksa resolver. Exception-nya
mencantumkan setiap jalur yang dicoba.

```bash
./native/build.sh          # atau ./native/build.ps1 di Windows
```

Untuk memakai library dari lokasi lain:

```bash
export LVGLNET_NATIVE_PATH=/opt/lvglnet/liblvglnet.so    # berkas atau direktori pemuatnya
```

## "Native shim ABI *n* does not match the expected *m*"

Assembly managed dan library native berasal dari commit yang berbeda - biasanya library yang
dibangun sebelum perpindahan ke LVGL 9.6 (shim ABI 3) berdampingan dengan kode managed yang lebih baru
(ABI 4). Bangun ulang sisi native; bila CMake mengeluh bahwa cache-nya milik direktori lain, bangun
ulang dari bersih (`./native/build.ps1 -Clean`, atau hapus `native/build`).

## "The native library was built with LV_COLOR_DEPTH=16"

LVGL.Net memerlukan warna 32-bit: backend menyalin word XRGB8888 mentah, dan build 16-bit akan
menghasilkan tampilan kacau. Kembalikan `#define LV_COLOR_FORMAT_DEFAULT LV_COLOR_FORMAT_XRGB8888`
di `native/lv_conf.h` lalu bangun ulang.

## Semua integration test "lulus" dalam sepersekian detik

Test-test itu sebenarnya terlewati: tanpa library native yang dapat dimuat, setiap integration test
berhenti lebih awal. Set `LVGLNET_REQUIRE_NATIVE=1` dan prosesnya justru gagal, disertai alasan
mengapa library tidak termuat.

## `SDL_Init failed` / SDL2 tidak ditemukan

```bash
sudo apt install libsdl2-2.0-0        # Debian, Ubuntu, Raspberry Pi OS
brew install sdl2                     # macOS
```

Di Windows, letakkan `SDL2.dll` dari [rilis SDL](https://github.com/libsdl-org/SDL/releases) ke
`runtimes/win-x64/native/`. Perhatikan DLL ini tidak ikut di-commit — `.gitignore` mengecualikan
biner di `runtimes/` — jadi kloning baru perlu mengulangi langkah ini.

Lewat SSH, SDL memerlukan tampilan: gunakan `ssh -X`, atau pakai backend framebuffer.

## `Could not open /dev/fb0` — izin ditolak

```bash
sudo usermod -aG video,input $USER    # lalu keluar dan masuk kembali
groups                                # pastikan 'video' dan 'input' tercantum
```

## `LvglThreadAccessException`

Sebuah widget disentuh dari thread yang tidak memiliki LVGL. LVGL menyimpan state-nya pada variabel
global tanpa penguncian, jadi hal ini ditangkap alih-alih dibiarkan merusak memori.

```csharp
// salah - pada thread latar
label.Text = reading.ToString();

// benar
app.Post(() => label.Text = reading.ToString());
```

`Post` bersifat fire-and-forget; `Invoke` memblokir sampai aksi selesai.

## `ObjectDisposedException` dari properti widget

Widget sudah dihapus — langsung, atau karena induknya atau `Clear()` membawanya serta. Periksa
`IsAlive` bila sebuah referensi mungkin hidup lebih lama daripada widget-nya:

```csharp
if (_readout is { IsAlive: true }) _readout.Text = message;
```

## Layout salah posisi setelah memakai `LvCoord.Percent`

Penyebab yang lazim adalah aritmetika pada koordinat tersandi. LVGL menyimpan penanda persentase pada
bit tinggi, jadi `LvCoord.Percent(100) - 190` bukan "100% dikurangi 190 piksel" — itu nilai rusak yang
kebetulan tampak seperti jumlah piksel biasa.

```csharp
// salah
panel.SetSize(LvCoord.Percent(100) - SidebarWidth, LvCoord.Percent(100));

// benar
panel.SetSize(app.Display.Width - SidebarWidth, app.Display.Height);
```

## Tidak ada yang tergambar, atau jendela hitam

- Apakah ada objek yang diinduki ke layar aktif? `new LvLabel(app.Screen, "test")` adalah pemeriksaan
  tercepat.
- Apakah `app.Run()` (atau loop `RunFrame()`) benar-benar berjalan?
- Pada framebuffer, apakah ada proses lain — sesi desktop, instansi lain — yang juga menggambar?

## Warna salah (merah dan biru tertukar)

Pada backend buatan sendiri, sumbernya berformat XRGB8888: byte B, G, R, X pada mesin little-endian.
Backend framebuffer mendeteksi urutan kanal perangkat dan menukarnya bila perlu; backend tulisan
tangan harus menyesuaikan dengan targetnya sendiri.

## Grid chart berubah menjadi putih terang, atau setiap sampel punya titik

Keduanya berubah di LVGL 9.5. Garis grid kini memakai gaya garis milik part **main** - warnai dengan
`chart.SetLineColor(color)` alih-alih `LvPart.Items` - dan penanda titik digambar secara bawaan:
`chart.SetPartSize(0, 0, LvPart.Indicator)` menghilangkannya.

## Chart tidak bergulir

`UpdateMode` harus `Shift` untuk jendela bergulir. `Circular` menimpa di tempat, seperti sapuan
osiloskop.

```csharp
chart.UpdateMode = LvChartUpdateMode.Shift;
```

## Designer menampilkan "Preview offline" tetapi aplikasi tetap berjalan

Wajar bila library native belum ada — panel di papan desain menjelaskannya, dan status bar
menampilkan *LVGL offline*. Menyunting, menyimpan, dan mengekspor C# tidak memerlukan LVGL.

## Gambar atau font tidak tampil

`UiBuilder.Diagnostics` (dan *things to check* di designer) mencantumkan setiap aset yang tidak dapat
ditemukannya. Jalur aset di dalam layout relatif terhadap berkas layout; di dalam kode,
`LvImageSource.FromFile` dan `LvFont.FromFile` melempar `FileNotFoundException` sebelum LVGL melihat
jalurnya. Bila PNG tetap tidak tampil, pastikan berkas itu benar-benar PNG atau BMP - hanya kedua
decoder itulah yang dikompilasi.

## `DrmBackend`: "DRM_IOCTL_MODE_SETCRTC failed"

Program lain sedang menguasai layar - biasanya desktop. Boot ke konsol atau hentikan display
manager, atau pakai backend SDL selama desktop berjalan. "No DRM device has a connected display"
berarti KMS mati (`dtoverlay=vc4-kms-v3d` di `/boot/firmware/config.txt`) atau panel tidak
terhubung; backend framebuffer tetap bekerja dalam kasus itu.

## Handler event berjalan tetapi exception-nya hilang

Exception managed tidak boleh naik ke tumpukan C milik LVGL, jadi exception itu ditangkap di batas
callback. Exception itu tidak hilang: berlanggananlah pada `LvglRuntime.CallbackFailed` untuk
mencatatnya. Timer dan animasi juga melapor ke sana.

## CPU tinggi saat menganggur

Pastikan `MinIdleSleepMs` tidak nol — nol berarti menunggu sibuk. Nilai bawaan 1 ms melepaskan
prosesor. Bila UI terus menggambar ulang, cari animasi atau timer yang meng-invalidasi widget setiap
frame; `app.Display.FlushCount` yang naik terus pada layar statis memastikannya.

## Diagnostik

```csharp
LvglRuntime.LvglVersion            // build LVGL yang benar-benar dimuat
LvglRuntime.ColorDepth             // harus 32
app.Display.AllocatedBytes         // memori draw buffer
app.Display.FlushCount             // region yang di-flush sejak awal
app.FrameCount
LvglNativeLibrary.ProbedPaths      // tempat resolver mencari
```

Aktifkan logging bawaan LVGL dengan menyetel `LV_USE_LOG` menjadi 1 di `native/lv_conf.h` lalu
membangun ulang.
