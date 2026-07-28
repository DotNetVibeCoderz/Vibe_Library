# Memulai

## Prasyarat

| Kebutuhan | Catatan |
|-----------|---------|
| .NET SDK 10.0 | `dotnet --version` harus menampilkan 10.x |
| CMake 3.20+ dan compiler C | Hanya diperlukan untuk membangun library native |
| Git | Proses build native mengkloning LVGL v9 |
| SDL2 | Hanya untuk backend SDL (jendela desktop) |

Di Windows, perkakas C kemungkinan besar sudah tersedia: instalasi Visual Studio dengan workload
*Desktop development with C++* sudah menyertakan CMake, Ninja, dan MSVC. `native/build.ps1` mencari
sendiri lokasinya, jadi tidak ada yang perlu ditambahkan ke `PATH` dan tidak perlu membuka Developer
Command Prompt.

Memasang SDL2:

```bash
sudo apt install libsdl2-2.0-0        # Debian, Ubuntu, Raspberry Pi OS
brew install sdl2                     # macOS
```

Windows tidak menyediakan SDL2 bawaan sistem. Unduh `SDL2-<versi>-win32-x64.zip` dari
[rilis SDL](https://github.com/libsdl-org/SDL/releases) lalu letakkan `SDL2.dll` di
`runtimes/win-x64/native/`, bersebelahan dengan `lvglnet.dll` — backend menelusuri jalur pencarian
yang sama dengan library inti, jadi tidak ada yang perlu ditambahkan ke `PATH`:

```powershell
$url = 'https://github.com/libsdl-org/SDL/releases/download/release-2.32.10/SDL2-2.32.10-win32-x64.zip'
Invoke-WebRequest $url -OutFile sdl2.zip
Expand-Archive sdl2.zip -DestinationPath sdl2 -Force
Copy-Item sdl2/SDL2.dll runtimes/win-x64/native/
```

Backend framebuffer Linux sama sekali tidak memerlukan SDL — justru itulah tujuannya.

## Bangun library native

LVGL adalah library C, jadi ada satu langkah kompilasi sebelum kode managed dapat dijalankan:

```bash
./native/build.sh          # Linux, macOS
./native/build.ps1         # Windows
```

Perintah ini mengunduh LVGL v9 ke `native/lvgl`, mengompilasinya bersama shim ABI di `native/shim`,
lalu menempatkan hasilnya di `runtimes/<rid>/native/`. Proyek managed mengambilnya dari sana secara
otomatis — tidak ada yang perlu disalin manual.

Langkah ini hanya perlu diulang saat Anda mengubah `native/lv_conf.h`, mengubah shim, atau berpindah
versi LVGL.

## Build dan test sisi managed

```bash
dotnet build LVGL.Net.slnx
dotnet test  LVGL.Net.slnx
```

Test sengaja **tidak** memerlukan library native, sehingga `dotnet test` tetap berjalan di mesin
tanpa CMake. Test yang membutuhkan LVGL akan memeriksa ketersediaannya dan berhenti lebih awal.

## Jendela pertama Anda

```csharp
using Lvgl;
using Lvgl.Sdl;
using Lvgl.Widgets;

using var backend = new SdlBackend(480, 320, "Hello LVGL");
using var app = LvglApplication.Create(backend);

var label = new LvLabel(app.Screen, "Halo dari .NET");
label.Align(LvAlign.TopMid, 0, 40);
label.SetFontSize(20);

var button = new LvButton(app.Screen, "Tekan saya");
button.SetSize(160, 48);
button.Center();
button.Clicked += (_, _) => label.Text = $"Ditekan pukul {DateTime.Now:HH:mm:ss}";

app.Run();
```

Tiga hal yang perlu diperhatikan:

- **`LvglApplication.Create` mengklaim thread pemanggil.** LVGL menyimpan state-nya pada variabel
  global tanpa penguncian, jadi hanya satu thread yang boleh menyentuhnya. Pekerjaan dari thread
  lain harus melewati `app.Post(...)`.
- **Widget tidak di-dispose.** LVGL memiliki pohon objeknya; menghapus induk menghapus seluruh
  anaknya. Panggil `Delete()` bila Anda benar-benar ingin membuang sebuah widget.
- **`app.Run()` memblokir** sampai jendela ditutup, `Stop()` dipanggil, atau cancellation token
  aktif.

## Menjalankan contoh

```bash
dotnet run --project samples/LVGL.Desktop                    # peramban komponen
dotnet run --project samples/LVGL.Desktop -- --width 1280 --height 720
dotnet run --project samples/LVGL.Raspi -- --sdl --simulate  # dasbor, sensor palsu, dalam jendela
```

Untuk memeriksa keseluruhan tumpukan tanpa harus mengamati jendela — berguna di CI — render
sejumlah frame tetap lalu keluar:

```bash
dotnet run --project samples/LVGL.Desktop -- --frames 120 --no-vsync
# LVGL 9.2.2  |  1024x600  |  draw buffers 480 KiB
# Rendered 120 frames in 224 ms (14 region flushes)
```

Kode keluar bukan-nol berarti tidak ada region yang pernah di-flush, alias tidak ada yang tergambar.

## Selanjutnya

- [Arsitektur](02-arsitektur.md) — mengapa wrapper ini dirancang seperti ini
- [Widget dan styling](04-widget.md) — API sehari-hari
