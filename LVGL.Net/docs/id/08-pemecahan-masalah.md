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

Assembly managed dan library native berasal dari commit yang berbeda. Bangun ulang sisi native.

## "The native library was built with LV_COLOR_DEPTH=16"

LVGL.Net memerlukan warna 32-bit: backend menyalin word XRGB8888 mentah, dan build 16-bit akan
menghasilkan tampilan kacau. Kembalikan `#define LV_COLOR_DEPTH 32` di `native/lv_conf.h` lalu
bangun ulang.

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

## Chart tidak bergulir

`UpdateMode` harus `Shift` untuk jendela bergulir. `Circular` menimpa di tempat, seperti sapuan
osiloskop.

```csharp
chart.UpdateMode = LvChartUpdateMode.Shift;
```

## Preview designer kosong tetapi aplikasi tetap berjalan

Wajar bila library native belum ada — panel di atas area preview menjelaskannya. Menyunting,
menyimpan, dan mengekspor C# tidak memerlukan LVGL.

## Handler event berjalan tetapi exception-nya hilang

Ini disengaja: exception managed tidak boleh naik ke tumpukan C milik LVGL, jadi handler menelannya.
Tangkap dan catat di dalam handler.

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
