# Dokumentasi LVGL.Net (Bahasa Indonesia)

> English documentation: [`docs/en`](../en/README.md)
>
> **Di port oleh Kang Fadhil dari Gravicode Studios.**

LVGL.Net adalah wrapper .NET 10 multi-platform untuk [LVGL](https://github.com/lvgl/lvgl) v9,
library grafis untuk sistem tertanam, dilengkapi designer visual berbasis WPF dan dua aplikasi
contoh.

| # | Dokumen | Isi |
|---|---------|-----|
| 1 | [Memulai](01-memulai.md) | Prasyarat, build pertama, jendela pertama |
| 2 | [Arsitektur](02-arsitektur.md) | Bagaimana sisi managed dan native disatukan, dan alasannya |
| 3 | [Build library native](03-build-native.md) | CMake, shim ABI, `lv_conf.h`, cross-compile |
| 4 | [Widget dan styling](04-widget.md) | API widget, event, layout, style |
| 5 | [Backend](05-backend.md) | SDL, framebuffer Linux, headless, membuat sendiri |
| 6 | [Designer](06-designer.md) | Menyunting layout, preview langsung, ekspor C# |
| 7 | [Panduan Raspberry Pi](07-raspberry-pi.md) | Deploy ke Pi 4, perizinan, autostart |
| 8 | [Pemecahan masalah](08-pemecahan-masalah.md) | Kegagalan yang paling sering ditemui |
| 9 | [Asisten desain](09-asisten.md) | Jack The Code Bender: penyedia model, tools, lampiran, prompt |
| 10 | [Ruang kerja designer](10-ruang-kerja-designer.md) | Mode Design/Code, editor kode, panel asisten yang ditambatkan |

## Struktur repositori

```
src/LVGL.Net           Wrapper inti - agnostik platform, tanpa dependensi framework UI
src/LVGL.Net.Sdl       Backend SDL2 (Windows, macOS, desktop Linux)
src/LVGL.Net.Linux     Backend framebuffer + evdev (Raspberry Pi, perangkat kiosk)
src/LVGL.Net.Ui        Model layout serializable, loader runtime, generator C#
src/LVGL.Net.Assistant Jack The Code Bender - asisten Semantic Kernel, penyedia model, tools
tools/LVGL.Designer    Designer visual WPF (khusus Windows, karena sifat WPF)
samples/LVGL.Desktop   Peramban demo komponen lintas platform
samples/LVGL.Raspi     Dasbor sensor realtime untuk Raspberry Pi 4
tests/LVGL.Net.Tests   Unit test - berjalan tanpa library native
native/                Build LVGL, shim ABI, lv_conf.h, skrip build
```

## Rujukan cepat

```bash
./native/build.sh                                  # build library native (Linux/macOS)
./native/build.ps1                                 # build library native (Windows)
dotnet build LVGL.Net.slnx                         # build seluruh sisi managed
dotnet test  LVGL.Net.slnx                         # jalankan test
dotnet run --project samples/LVGL.Desktop          # demo komponen
dotnet run --project samples/LVGL.Raspi -- --sdl   # dasbor sensor dalam jendela
dotnet run --project tools/LVGL.Designer           # designer visual (Windows)
```
