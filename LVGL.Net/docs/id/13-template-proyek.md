# Template proyek

`LVGL.Net.Templates` menambahkan dua template ke `dotnet new`: aplikasi lengkap yang berjalan di
jendela desktop maupun di layar Raspberry Pi, dan template item untuk menambahkan layar ke dalamnya.

```bash
dotnet new install LVGL.Net.Templates
dotnet new lvgl -n Thermostat            # aplikasi dengan UI dibangun di C#
dotnet new lvgl -n Thermostat --layout json   # aplikasi dengan UI berupa layout designer
cd Thermostat
dotnet run
```

<table>
<tr>
<td><img src="../images/screenshots/template-code.png" alt="dotnet new lvgl --layout code" /></td>
<td><img src="../images/screenshots/template-json.png" alt="dotnet new lvgl --layout json" /></td>
</tr>
<tr>
<td align="center"><code>--layout code</code></td>
<td align="center"><code>--layout json</code></td>
</tr>
</table>

Kedua varian menghasilkan layar yang sama: slider yang menggerakkan bar dan angka, switch yang
menyalakan lampu status, dan jam yang diperbarui oleh `LvTimer`. Bedanya hanya di mana layout
disimpan.

## `dotnet new lvgl`

| Opsi | Bawaan | Efek |
|---|---|---|
| `--layout code\|json` | `code` | `code` membangun layar di C#. `json` memuat `Screens/MainScreen.lvgl.json` dengan `UiBuilder` dan menambahkan `LVGL.Net.Ui` |
| `--width`, `--height` | `800`, `480` | Ukuran rancangan UI: ukuran jendela dan bawaan untuk `--drm`/`--fb` |
| `--lvgl-version` | rilis template | Versi paket LVGL.Net yang direferensikan |
| `--no-restore` | mati | Lewati restore setelah pembuatan |

Yang dibuat:

| Berkas | Isinya |
|---|---|
| `Program.cs` | Memilih backend dari baris perintah dan menjalankan loop |
| `MainScreen.cs` | Layar pertama, dengan `Build(root)` dan `Destroy()` |
| `Screens/MainScreen.lvgl.json` | Layout (hanya varian json). Buka di designer |
| `README.md` | Cara menjalankan, deploy ke Pi, dan aturan yang perlu diketahui |

Proyek mereferensikan `LVGL.Net`, `LVGL.Net.Sdl` dan `LVGL.Net.Linux`. Ketiganya kecil, dan membawa
kedua backend itulah yang membuat satu build bisa berjalan di meja kerja maupun di perangkat:

```bash
dotnet run                                       # jendela desktop (SDL)
dotnet run -- --drm                              # layar Pi lewat DRM/KMS - page flipping, tanpa tearing
dotnet run -- --fb /dev/fb1                      # framebuffer, misalnya panel SPI
dotnet run -- --headless --screenshot shot.png   # tanpa layar: render 60 frame, simpan PNG, keluar
```

Input sentuh pada `--drm` dan `--fb` dicari dengan `EvdevPointer.AutoDetect`; tanpa panel sentuh
atau mouse, UI tetap tampil.

Berkas proyek sudah mengatur yang dibutuhkan Pi: GC workstation (GC server memesan heap per core) dan
`InvariantGlobalization` (tidak perlu memasang ICU). Deploy tanpa SDK di perangkat:

```bash
dotnet publish -c Release -r linux-arm64 --self-contained
```

## `dotnet new lvgl-screen`

Jalankan di dalam proyek untuk menambahkan layar:

```bash
dotnet new lvgl-screen -n Settings
```

Perintah ini membuat `Settings.cs` dan `Screens/Settings.lvgl.json` di root namespace proyek. Kelas
tersebut memuat layout, memicu `BackRequested` saat tombol Back ditekan, dan membebaskan style-nya di
`Destroy()`. Aplikasi varian json sudah siap untuk itu. Aplikasi `--layout code` perlu dua hal yang
sudah dimiliki varian json - paket loader, dan layout yang disalin ke samping executable:

```bash
dotnet add package LVGL.Net.Ui
```

```xml
<ItemGroup>
  <None Remove="Screens/**" />
  <Content Include="Screens/*.lvgl.json" Link="Screens/%(Filename)%(Extension)" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

Berpindah layar cukup dengan mengosongkan root lalu membangun layar berikutnya:

```csharp
void Show(Settings settings)
{
    app.Screen.Clear();          // widget dulu...
    current.Destroy();           // ...baru style yang dipakainya
    settings.Build(app.Screen);
}
```

## Cara template diuji

`templates/test-templates.sh` mem-pack LVGL.Net dan template ke feed lokal, memasangnya ke hive
template sementara, membuat aplikasi untuk setiap `--layout`, menambahkan layar ke varian json,
mem-build keduanya dengan warning sebagai error, lalu menjalankan masing-masing tanpa layar sampai
menyimpan PNG. CI menjalankannya di Windows, Linux dan macOS; screenshot di atas berasal dari skrip
ini:

```bash
LVGLNET_TEMPLATE_SCREENSHOTS=docs/images/screenshots bash templates/test-templates.sh
```

`TemplateTests` di unit test menangkap kesalahan yang lebih murah lebih awal: versi paket bawaan
template harus sama dengan versi di `Directory.Build.props`, dan kedua layout template harus valid
serta memuat setiap widget yang dicari kodenya berdasarkan nama.
