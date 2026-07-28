# Backend

Backend adalah segala hal yang spesifik platform: tempat menaruh piksel, dan tempat mengambil input.
Library native sama sekali tidak memuat driver tampilan, jadi di sinilah satu-satunya kode platform
berada.

```csharp
public interface ILvglBackend : IDisposable
{
    int Width { get; }
    int Height { get; }
    LvPointerState Pointer { get; }

    void Blit(in LvArea area, nint pixels, bool isLastFlush);
    bool PumpEvents();          // false => pengguna meminta menutup
}
```

Seluruh anggotanya hanya dipanggil dari thread LVGL.

## SDL — `LVGL.Net.Sdl`

Keluaran berjendela di Windows, macOS, dan desktop Linux mana pun, termasuk Pi yang menjalankan X11
atau Wayland.

```csharp
using var backend = new SdlBackend(1024, 600, "Aplikasi saya", vsync: true);
```

Setiap region yang di-flush masuk ke tekstur streaming `ARGB8888`; tekstur ditampilkan sekali per
frame, pada region terakhir. Karena tekstur mempertahankan isinya antar frame, mode gambar-ulang
parsial milik LVGL bekerja benar di sini — hanya potongan yang berubah yang diunggah.

`SdlBackend` juga mengimplementasikan `ILvglKeyboardSource`, sehingga input papan ketik bekerja bila
`LvglOptions.EnableKeyboard` diaktifkan. Keycode SDL dipetakan ke kode navigasi `LV_KEY_*` milik
LVGL; ASCII yang dapat dicetak diteruskan apa adanya.

Bila tidak ada renderer berakselerasi — CI tanpa layar, sebagian konfigurasi Pi — ia otomatis
beralih ke renderer perangkat lunak milik SDL.

## Framebuffer Linux — `LVGL.Net.Linux`

Keluaran langsung ke `/dev/fb0` tanpa sistem jendela. Inilah backend untuk Pi yang menggerakkan
panel sebagai instrumen atau kiosk.

```csharp
var pointer = EvdevPointer.AutoDetect(screenWidth, screenHeight);
using var backend = new FramebufferBackend("/dev/fb0", pointer);
```

Ia memetakan framebuffer ke memori dan menyalin setiap region yang di-flush ke dalamnya, sehingga
satu gambar-ulang berarti satu `memcpy` per baris pindai. Perangkat 32-bit dan 16-bit didukung;
urutan kanal merah/biru dibaca dari driver dan hanya ditukar bila berbeda dari keluaran LVGL.

`EvdevPointer` membaca `/dev/input/eventN` dan menangani kedua jenis perangkat: panel sentuh
melaporkan posisi absolut, yang diskalakan ulang memakai rentang yang diiklankan driver melalui
`EVIOCGABS`, sedangkan tetikus melaporkan gerakan relatif, yang diakumulasi dan dibatasi.

Keduanya memerlukan keanggotaan grup:

```bash
sudo usermod -aG video,input $USER    # lalu keluar dan masuk kembali
```

## Headless — di dalam `LVGL.Net`

Merender ke `byte[]` managed. Dua hal memakainya: preview langsung di designer, yang menyalin buffer
ke `WriteableBitmap`, dan test, yang dapat memeriksa piksel tanpa display server.

```csharp
using var backend = new HeadlessBackend(800, 480);
using var app = LvglApplication.Create(backend, new LvglOptions { EnablePointer = false });

app.RunFrame();

if (backend.IsDirty)
{
    ReadOnlySpan<byte> frame = backend.Frame;   // XRGB8888, stride = Width * 4
    backend.ClearDirty();
}

backend.SetPointer(120, 80, pressed: true);     // input sintetis
```

## Membuat backend sendiri

Seluruh kontraknya hanya empat anggota. Backend untuk layar SPI, perangkat DRM/KMS, atau aliran
frame jarak jauh cukup satu kelas.

```csharp
public sealed class MyBackend : ILvglBackend
{
    public int Width => 320;
    public int Height => 240;
    public LvPointerState Pointer { get; private set; }

    public void Blit(in LvArea area, nint pixels, bool isLastFlush)
    {
        // `pixels` berformat XRGB8888, rapat, stride = area.Width * 4.
        // Hanya valid selama pemanggilan - salin, jangan disimpan.
        // Batas `area` bersifat inklusif: region 1x1 punya X1 == X2.

        if (isLastFlush) Present();
    }

    public bool PumpEvents()
    {
        Pointer = ReadTouchPanel();
        return true;
    }

    public void Dispose() { }
}
```

Tiga aturan:

- **`isLastFlush` adalah saat untuk menampilkan.** Perangkat dengan swap chain atau buffer ganda
  harus membalik buffer di sana, bukan pada setiap region.
- **Jangan menyimpan `pixels`.** Pointer itu menunjuk ke draw buffer LVGL yang langsung dipakai
  ulang.
- **Melempar exception diperbolehkan.** Exception ditangkap dan dilempar ulang dari run loop, bukan
  naik ke C.

Implementasikan juga `ILvglKeyboardSource` bila perangkat memiliki tombol.

## Mode render

| Mode | Buffer | Untuk |
|------|--------|-------|
| `Partial` (bawaan) | Dua potongan setinggi `PartialBufferLines` baris | Target tertanam; memori paling hemat |
| `Direct` | Dua buffer sepenuh layar | Blit lebih sedikit dan lebih besar saat memori berlimpah |
| `Full` | Dua buffer sepenuh layar, digambar ulang seluruhnya | Perangkat yang tidak mendukung pembaruan parsial |

```csharp
var app = LvglApplication.Create(backend, new LvglOptions
{
    RenderMode = LvRenderMode.Partial,
    PartialBufferLines = 32,     // 0 = sepersepuluh tinggi layar
});
```

`app.Display.AllocatedBytes` melaporkan biaya sesungguhnya dari pilihan tersebut.
