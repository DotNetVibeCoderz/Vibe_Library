# Build library native

## Apa yang dibangun

Satu shared library, `lvglnet`, berisi LVGL v9 resmi ditambah shim ABI dari `native/shim`. Satu
artefak berarti satu nama `DllImport` dan tidak ada masalah urutan pemuatan antar library yang
saling bergantung.

| Platform | Berkas | Ditempatkan ke |
|----------|--------|----------------|
| Windows | `lvglnet.dll` | `runtimes/win-x64/native/` |
| Linux | `liblvglnet.so` | `runtimes/linux-x64/native/` atau `linux-arm64` |
| macOS | `liblvglnet.dylib` | `runtimes/osx-arm64/native/` atau `osx-x64` |

## Build cepat

```bash
./native/build.sh                # build untuk mesin ini
./native/build.sh --pi4          # disetel untuk Cortex-A72 (Raspberry Pi 4)
./native/build.sh --no-demos     # lewati scene demo bawaan LVGL
```

```powershell
./native/build.ps1
./native/build.ps1 -NoDemos
```

## CMake manual

```bash
cmake -S native -B native/build -DCMAKE_BUILD_TYPE=Release
cmake --build native/build --config Release
```

| Opsi | Bawaan | Efek |
|------|--------|------|
| `LVGL_TAG` | `v9.2.2` | Rilis LVGL yang diunduh |
| `LVGLNET_WITH_DEMOS` | `ON` | Mengompilasi `lv_demo_widgets` dan `lv_demo_benchmark` |
| `LVGLNET_TUNE_PI4` | `OFF` | Menambahkan `-mcpu=cortex-a72`; biner menjadi tidak portabel |
| `LVGLNET_RID` | dideteksi otomatis | Folder `runtimes/<rid>/native` tujuan |

Checkout lokal di `native/lvgl` diutamakan dibanding pengunduhan, sehingga build offline dan
air-gapped tetap berjalan: kloning LVGL ke sana sendiri dan CMake akan memakainya.

## Konfigurasi: `native/lv_conf.h`

Sengaja dibuat ringkas. `lv_conf_internal.h` milik LVGL menyediakan nilai bawaan untuk setiap opsi
yang tidak didefinisikan, jadi hanya pengaturan yang benar-benar dipakai LVGL.Net yang dicantumkan —
artinya rilis kecil LVGL berikutnya tidak perlu digabung manual.

Satu pengaturan yang tidak boleh diubah:

```c
#define LV_COLOR_DEPTH 32
```

Backend menyalin word XRGB8888 mentah. Build 16-bit akan menghasilkan tampilan kacau, sehingga
`LvglRuntime.Initialize` menolaknya dengan pesan tegas alih-alih menggambar hal yang salah.

Pengaturan yang wajar untuk diubah:

| Pengaturan | Alasan |
|------------|--------|
| `LV_FONT_MONTSERRAT_*` | Setiap ukuran yang aktif memakan ruang biner; matikan yang tak terpakai |
| Flag `LV_USE_*` widget | Mematikan widget yang tidak dipakai memperkecil library |
| `LV_DEF_REFR_PERIOD` | Irama refresh dalam ms; 16 ≈ 60 FPS |
| `LV_DRAW_SW_DRAW_UNIT_CNT` | Thread gambar tambahan. Dibiarkan 1: pada Pi 4 inti, umumnya hanya menambah rebutan sumber daya |
| `LV_USE_LOG` | Setel 1 saat menelusuri masalah rendering |

Setelah menyunting, bangun ulang library native. Kode managed tidak perlu dibangun ulang.

## Cross-compile untuk Pi

Dua pilihan, berurutan sesuai preferensi:

**Bangun di Pi.** Paling sederhana dan andal:

```bash
sudo apt install cmake build-essential git
./native/build.sh --pi4
```

**Cross-compile dari Linux x86-64:**

```bash
sudo apt install gcc-aarch64-linux-gnu
cmake -S native -B native/build-arm64 \
      -DCMAKE_BUILD_TYPE=Release \
      -DCMAKE_C_COMPILER=aarch64-linux-gnu-gcc \
      -DCMAKE_SYSTEM_NAME=Linux \
      -DCMAKE_SYSTEM_PROCESSOR=aarch64 \
      -DLVGLNET_RID=linux-arm64 \
      -DLVGLNET_TUNE_PI4=ON
cmake --build native/build-arm64
```

`LVGLNET_RID` wajib diisi saat cross-compile: tanpa itu jalur penempatan akan ditebak dari mesin
host.

## Bagaimana sisi managed menemukan library

`LvglNativeLibrary` memasang resolver yang memeriksa, berurutan:

1. `LVGLNET_NATIVE_PATH` — jalur lengkap ke berkas, atau direktori yang memuatnya
2. `AppContext.BaseDirectory` dan setiap direktori induknya, baik langsung maupun di bawah
   `runtimes/<rid>/native/`
3. Jalur pencarian bawaan loader sistem operasi

Penelusuran ke atas dari direktori keluaran inilah yang membuat `dotnet run --project samples/...`
menemukan folder `runtimes/` di akar repositori tanpa langkah penyalinan.

Bila semua gagal, exception mencantumkan seluruh jalur yang dicoba beserta perintah untuk membangun
library.

## Memverifikasi hasil build

```csharp
LvglRuntime.Initialize();
Console.WriteLine(LvglRuntime.LvglVersion);   // mis. 9.2.2
Console.WriteLine(LvglRuntime.ColorDepth);    // harus 32
```

Atau cukup jalankan contoh desktop dan buka halaman *LVGL demos* — bila showcase widget tampil,
build native sudah lengkap.
