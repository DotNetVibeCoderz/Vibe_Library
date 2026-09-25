# Diagnostik

[English](../en/diagnostics.md) · [Indeks](README.md)

`AplNet.Diagnostics.AplEventSource` menerbitkan EventCounter dengan nama **`AplNet`**:

| Counter | Arti |
|---|---|
| `loops-executed` | Loop yang berjalan di lebih dari satu worker |
| `inline-loops-executed` | Loop yang terlalu kecil untuk dibagi dan berjalan di pemanggil |
| `chunks-executed` | Rentang yang diserahkan ke body: partisi, garis, blok, atau grain |
| `steals` | Pencurian yang berhasil oleh `WorkStealingPartitioner` |

**Counter nonaktif secara default, dan gratis selama nonaktif.** Setiap penambahan berada di balik
`EventSource.IsEnabled()`, yang hanya membaca satu field, dan counter-nya sendiri baru dibuat
setelah ada listener yang mengaktifkan source.

```sh
dotnet-counters monitor --counters AplNet -p <pid>
```

Di dalam proses, baca totalnya langsung selama ada listener yang terpasang:

```csharp
long steals = AplEventSource.Log.StealCount;
long chunks = AplEventSource.Log.ChunksExecuted;
```

**Yang perlu diperhatikan:**

- **Sedikit chunk per loop, tetapi lajurnya timpang:** coba striping atau work stealing.
- **Sangat banyak chunk dengan iterasi murah:** naikkan `CancellationCheckInterval` atau grain
  work stealing.
- **Banyak loop inline:** loop Anda di bawah `MinChunkSize`. Untuk loop kecil, biasanya itu sudah
  benar.

Di NativeAOT, dukungan EventSource dipangkas kecuali aplikasi menyetel
`<EventSourceSupport>true</EventSourceSupport>`. APL.Net tetap bekerja sama saja, hanya berhenti
menghitung.
