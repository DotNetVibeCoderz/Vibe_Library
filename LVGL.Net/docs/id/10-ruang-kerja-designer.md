# Ruang kerja designer

Designer adalah satu jendela dengan tiga bagian: kanvas visual, editor kode di atas dokumen yang
sama, dan asisten yang ditambatkan di sisi kanan.

```
┌───────────────────────────────────────────────────────────────────────┬──────────────┐
│ New Open Save Export C# [Design|Code] [Toolbox][Inspector] 800 x 480  │  Ask Jack ▣  │
├─────────┬───────────────────────────────────────┬─────────────────────┼──────────────┤
│         │                                       │  OUTLINE            │              │
│ TOOLBOX │      kanvas  atau  editor kode        ├─────────────────────┤    asisten   │
│         │                                       │  PROPERTIES         │              │
├─────────┴───────────────────────────────────────┴─────────────────────┴──────────────┤
│ status                                                        [Undo assistant change] │
└───────────────────────────────────────────────────────────────────────────────────────┘
```

## Panel

Ketiga panel samping disembunyikan dan ditampilkan dengan cara yang sama, dan ketiganya bisa diubah
lebarnya dengan menyeret pembatasnya. Masing-masing mengingat lebar yang Anda pilih, jadi membukanya
kembali memulihkan tata letak Anda alih-alih kembali ke ukuran bawaan.

| Panel | Tombol | Pintasan |
|---|---|---|
| Toolbox (kiri) | **Toolbox** di toolbar, atau ✕ di kepalanya | `Ctrl+B` |
| Outline dan properties (kanan) | **Inspector**, atau ✕ di kepalanya | `Ctrl+Shift+B` |
| Asisten (paling kanan) | **Ask Jack**, atau ✕ di kepalanya | `Ctrl+J` |

Pada layar kecil, menyembunyikan toolbox dan inspector memberi kanvas hampir seluruh jendela sambil
tetap membuka asisten di sisinya.

Mode Code menyembunyikan toolbox dan inspector secara otomatis — keduanya bekerja pada kanvas, jadi
tidak ada yang bisa dilakukan — sekaligus menonaktifkan tombolnya. Pilihan Anda tetap diingat dan
kembali saat Anda beralih ke mode Design.

## Mode Design dan Code

Sakelar **Design | Code** pada toolbar mengubah isi panel tengah. Keduanya bekerja pada dokumen
yang sama, jadi berpindah mode tidak menghilangkan apa pun.

**Design** adalah kanvas visual dengan toolbox dan inspector di kedua sisinya.

**Code** menggantikan kanvas dengan editor. Sebuah dropdown menentukan apa yang ditampilkan:

| Tampilan | Bisa disunting | Isinya |
|---|---|---|
| Layout (JSON) | Ya | Dokumennya sendiri. **Apply to design** mem-parsing dan menerapkannya ke kanvas |
| Generated C# | Tidak | Kelas builder, persis seperti hasil Export C# |
| Event handlers | Tidak | Belahan tulisan tangan, dengan kerangka per widget interaktif |

Kedua tampilan hasil generate sengaja hanya-baca: suntingan di sana akan hilang diam-diam saat
regenerasi berikutnya, dan itu lebih buruk daripada tidak menawarkannya sama sekali. Layout JSON
adalah satu-satunya tampilan yang bisa disunting, dan itulah yang menutup lingkarannya — sunting
dokumen dengan tangan, terapkan, lalu lihat hasilnya di kanvas.

Suntingan yang tidak valid ditolak dengan alasannya di status strip, bukan diterapkan begitu saja.

## Editor

| Perintah | Pintasan |
|---|---|
| Undo / Redo | `Ctrl+Z` / `Ctrl+Y` |
| Cut / Copy / Paste | `Ctrl+X` / `Ctrl+C` / `Ctrl+V` |
| Select all | `Ctrl+A` |
| Find and replace | `Ctrl+F`, `F3` untuk berikutnya |
| Go to line | `Ctrl+G` |
| Apply to design | `Ctrl+Enter` |

Semuanya juga tersedia di toolbar dan menu klik-kanan, jadi tidak ada yang hanya bisa lewat papan
ketik.

**Line numbers** dan **Wrap** adalah tombol centang di toolbar. Status strip menampilkan posisi
kursor dan panjang seleksi.

Pewarnaan sintaks mengikuti tampilan — JSON untuk layout, C# untuk kode hasil generate.

## Panel asisten

**Ask Jack** di toolbar, atau `Ctrl+J`, menampilkan dan menyembunyikan asisten. Seret pembatasnya
untuk mengubah lebar; lebarnya diingat saat ditutup dan dibuka lagi. Tombol **✕** di kepala panel
juga menyembunyikannya.

Panel ini adalah asisten yang sama seperti pada [bab 9](09-asisten.md) — sesi, penyedia model,
lampiran, dan tools tidak berubah. Yang berbeda dari jendela terpisah adalah apa yang terjadi saat
ia menghasilkan layout:

**Layout langsung diterapkan ke kanvas.** Tidak ada tombol yang perlu ditekan. Minta sebuah layar,
lalu lihat ia muncul. Bila Anda sedang di mode Code, editornya ikut diperbarui.

Itu aman karena ada tombol **Undo assistant change** yang muncul di status bar. Tombol itu
mengembalikan dokumen ke keadaan sebelum perubahan, satu langkah. Penerapan bersifat tidak merusak,
jadi siklusnya tetap cepat — minta, lihat, kembalikan bila bukan yang Anda mau, minta lagi.

Peringatan pada layout yang diterapkan (widget di luar layar, offset perataan yang tampak seperti
koordinat absolut) ditampilkan di status bar, bukan menghalangi penerapan.

Asisten dimulai secara malas: sesi designer yang tidak pernah membuka panel tidak pernah membangun
kernel, tidak menjalankan host lampiran, dan tidak membaca kunci API.
