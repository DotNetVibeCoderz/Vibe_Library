# APL.Net Gallery

[English](../en/gallery.md) · [Indeks](README.md)

`samples/APL.Net.Gallery` adalah aplikasi desktop Avalonia (Windows, Linux, macOS) yang
menjalankan APL.Net dan TPL berdampingan **di perangkat Anda sendiri**. Setiap kasus menampilkan:

- sebuah angka utama: seberapa jauh varian APL.Net terbaik lebih cepat (atau lebih lambat) dari
  `Parallel.For`;
- satu batang per varian (sekuensial abu-abu batu, TPL amber, APL.Net ultramarine), dengan median
  waktu, rasio terhadap TPL, dan byte yang dialokasikan per run;
- pengecekan bahwa keluaran APL.Net sama dengan keluaran loop sekuensial;
- kode APL.Net dan TPL untuk kasus tersebut;
- gambar hasilnya, jika ada (filter gambar, Monte Carlo, Mandelbrot);
- untuk kasus penjadwalan, sebuah **lane chart**: satu lajur per thread, menunjukkan kapan setiap
  thread sibuk dan kapan menganggur;
- hal yang perlu diperhatikan, ditulis apa adanya, termasuk kapan TPL menang.

```sh
dotnet run --project samples/APL.Net.Gallery -c Release
dotnet run --project samples/APL.Net.Gallery -c Release -- --case mandelbrot --lang id
```

Tombol ⇄ di pojok kiri bawah mengganti bahasa antarmuka antara English dan Bahasa Indonesia.

![Ikhtisar](../images/gallery-overview-id.png)

## Kasus-kasus

Sidebar mengelompokkan kasus di bawah glyph APL untuk gagasannya. Arahkan kursor ke glyph untuk
melihat artinya.

| Glyph | Kelompok | Kasus |
|---|---|---|
| `⍳` iota | Loop | Aritmetika sederhana · Komputasi sedang · Banyak loop kecil · Partikel (ForEach via ref) · Filter gambar · Perkalian matriks |
| `⍤` rank | SIMD | Skala dan geser (AXPY) |
| `+/` reduce | Reduksi | Jumlah · Dot product · Monte Carlo π |
| `⊆` partition | Penjadwalan | Himpunan Mandelbrot · Beban kerja timpang (Zipf) |
| `⎕` quad | Tingkat rendah | Pointer mentah dan function pointer · Source generator `[AplBody]` |

![Mandelbrot dalam Bahasa Indonesia](../images/gallery-mandelbrot-id.png)

![Aritmetika sederhana](../images/gallery-trivial.png)

![Banyak loop kecil: waktu dan alokasi](../images/gallery-overhead.png)

![Mandelbrot dengan lajur](../images/gallery-mandelbrot-full.png)

![Beban kerja Zipf](../images/gallery-zipf-full.png)

![Filter gambar](../images/gallery-image-full.png)

![Semua kasus di mesin ini](../images/gallery-summary.png)

## Cara pengukurannya

Setiap varian dipanaskan dulu sampai tiered compilation stabil (minimal 3 run), lalu diukur selama
sekitar 0,5 detik (5 sampai 200 run), dan yang dilaporkan adalah **median**. Ada jeda di antara
varian agar thread render tidak berebut core saat varian berikutnya diukur. Alokasi dihitung dalam
putaran terpisah ketika antarmuka sedang diam.

Harness di dalam aplikasi ini cepat dan jujur, tetapi bukan BenchmarkDotNet. Desktop juga
menjalankan proses lain, dan loop yang membagi kerja secara statis ke semua core paling terpengaruh
karena satu core yang sibuk menahan seluruh loop. Angka yang layak dikutip ada di
[hasil benchmark](benchmark-results.md).

## Screenshot

Semua gambar di dokumentasi berasal dari aplikasi ini sendiri:

```sh
dotnet run --project samples/APL.Net.Gallery -c Release -- --screenshot docs/images
```

Mode ini membuka setiap kasus, menekan *Run benchmark*, menunggu batang dan lajur muncul, lalu
menyimpan tangkapan jendela yang sebenarnya (ditambah tangkapan satu halaman penuh). Di akhir, ia
mengambil tabel ringkasan dan tampilan Bahasa Indonesia. Ambil ulang screenshot setiap kali
antarmuka berubah.

## Desain

Paletnya abu-abu mineral yang sejuk, seperti panel instrumen. Warna hanya dipakai untuk membawa
makna: satu warna per pustaka, dipakai dengan cara yang sama di mana-mana. **Martian Mono** adalah
huruf untuk angka ukur dan wordmark. **APL385** dipakai untuk glyph kategori, **Inter** untuk teks
bacaan, dan **JetBrains Mono** untuk kode. Lisensi font ada di `Assets/Fonts/*-OFL.txt`; APL385
berstatus domain publik.
