# Supervisi

*Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.*
*[English](../en/04-supervision.md) · [Indeks dokumentasi](README.md)*

## Gagasannya

Saat sebuah handler melempar exception, actor-nya tidak membuat proses crash dan tidak menelan
kesalahan itu diam-diam. Sebuah **supervisor** yang memutuskan apa yang terjadi, dan keputusan itu
berupa konfigurasi, bukan sesuatu yang harus diakali di dalam actor.

Skenario `Supervision` di sample desktop membuatnya konkret: empat actor dari *class yang sama*,
didaftarkan dengan strategi berbeda, diberi exception yang sama.

![Supervisi: empat hasil untuk satu exception](../images/samples-supervision.png)

Resume mempertahankan totalnya. Restart kembali ke nol dengan satu restart tercatat. Stop
dinonaktifkan. Yang ber-budget me-restart tapi akan berhenti alih-alih me-restart selamanya.

## Empat directive

| Directive | Yang terjadi | Pakai saat |
| --- | --- | --- |
| `Resume` | Buang pesan bermasalah; pertahankan instance dan state-nya | Pesannya yang buruk, actor-nya baik-baik saja |
| `Restart` | Bangun ulang instance. Alamat, mailbox, dan anak selamat; state di memori tidak | State actor mungkin sudah rusak |
| `Stop` | Nonaktifkan. Pesan berikutnya mengaktifkan instance baru | Kegagalannya fatal atau berulang identik |
| `Escalate` | Hentikan actor ini dan biarkan strategi induk yang memutuskan | Actor ini tidak bisa menilai kegagalannya |

## Melampirkan strategi

```csharp
system.RegisterActor<PaymentActor>(new OneForOneStrategy(ex => ex switch
{
    InsufficientFundsException => Directive.Resume,
    HttpRequestException       => Directive.Restart,
    TimeoutException           => Directive.Restart,
    _                          => Directive.Escalate,
})
{
    MaxRestarts = 5,
    Window = TimeSpan.FromMinutes(1),
});
```

Actor yang didaftarkan tanpa strategi memakai `Options.DefaultSupervisorStrategy`.

## Strategi bawaan

```csharp
SupervisorStrategy.Default          // restart, tapi stop untuk bug perakitan
SupervisorStrategy.StopOnFailure    // kegagalan apa pun menonaktifkan
SupervisorStrategy.ResumeOnFailure  // catat dan lewati pesannya, state dipertahankan
```

`Default` layak dibaca:

```csharp
ex switch
{
    ActorTypeNotRegisteredException => Directive.Stop,
    UnknownMessageTypeException     => Directive.Stop,
    FormatException                 => Directive.Stop,
    _                               => Directive.Restart,
}
```

Pemisahannya disengaja. Registrasi yang hilang atau alamat yang salah bentuk akan gagal identik
setiap kali, jadi me-restart-nya adalah busy-loop. Selain itu — galat basis data sementara, payload
buruk — mendapat instance baru, dan itulah inti keberadaan sebuah supervisor.

## Budget restart

```csharp
new OneForOneStrategy(_ => Directive.Restart)
{
    MaxRestarts = 10,
    Window = TimeSpan.FromMinutes(1),
}
```

Lewati `MaxRestarts` dalam `Window` yang bergeser, dan directive-nya diturunkan menjadi `Stop`.

Ini bukan sekadar kemewahan. Tanpa itu, satu pesan beracun yang duduk di kepala mailbox membeli
instance baru selamanya sambil membakar satu core. Dengan itu, actor-nya pergi dan masalahnya menjadi
terlihat sebagai sebuah alamat yang berhenti merespons.

## Cakupan: one-for-one dan all-for-one

```csharp
new OneForOneStrategy(...)   // hanya actor yang gagal — bawaan
new AllForOneStrategy(...)   // semua saudara di bawah induk yang sama ikut terdampak
```

All-for-one tepat saat saudara-saudara berbagi invarian — sekumpulan shard worker yang hanya masuk
akal di-restart sebagai satu kelompok. Ia hanya berlaku pada actor yang *punya* induk: di akar setiap
actor akan terhitung sebagai saudara, dan me-restart seluruh node karena satu actor melempar exception
tidak pernah menjadi maksudnya.

## Eskalasi

```csharp
new OneForOneStrategy(_ => Directive.Escalate)
```

Anaknya dihentikan — ia menolak menangani kegagalannya sendiri, jadi ia tidak layak melanjutkan — dan
strategi induk dimintai pendapat tentang exception yang sama. Bila induknya juga mengeskalasi, ia naik
lagi. Di akar, kegagalannya dicatat sebagai `ActorFailureEscalatedException` dan actor tetap berhenti.

## Apa yang sebenarnya dilakukan restart

```
OnRestartAsync(cause)     ← pada instance yang gagal; kesempatan terakhir melepas sesuatu
  ↓
instance baru dikonstruksi
  ↓
OnActivateAsync           ← pada instance baru; muat ulang state di sini
```

Dipertahankan: alamat, mailbox beserta pesan yang mengantre, anak-anaknya, `RestartCount`.
Hilang: semua isi field instance lama.

Untuk `PersistentActor`, instance baru memuat ulang dari store, jadi restart hampir transparan. Untuk
actor di memori, ia adalah reset — dan itu persis yang ditunjukkan sample supervisi.

## Kegagalan dan ask

Actor yang melempar exception saat menangani pesan yang sedang ditunggu seseorang **tidak**
meninggalkan pemanggilnya menunggu sampai timeout. Ask yang tertunda diselesaikan dengan kegagalannya:

```csharp
try
{
    await system.AskAsync<Receipt>(id, new Charge(50m));
}
catch (ActorNetException ex)
{
    // ex.InnerException adalah yang dilempar handler.
}
```

Pemanggil yang mendapat exception tahu apa yang salah. Pemanggil yang mendapat timeout hanya tahu ia
sudah menunggu, dan itu bug yang jauh lebih sulit dikejar. Ini juga bekerja lintas node, lewat frame
`AskFailure`.

## Kegagalan dan persistensi

`PersistentActor` melewatkan penulisannya bila alasan deaktivasi adalah `Supervision`:

```csharp
case Boom:
    State.Balance += 999m;                   // diubah
    throw new InvalidOperationException();    // …lalu melempar
```

Menuliskan itu kembali akan mengubah bug sementara menjadi data yang salah secara permanen. Ada tes
khusus untuk ini.

## Memilih strategi

Dua pertanyaan:

**Mungkinkah state actor ini sekarang salah?** Bila handler mengubah state sebelum ia bisa melempar
exception, `Resume` mempertahankan kerusakannya. Lebih baik `Restart`, atau pindahkan perubahannya ke
belakang semua yang bisa gagal.

**Apakah mencoba lagi akan membantu?** Gangguan jaringan: ya, restart. Pesan salah bentuk yang akan
dibaca ulang pada setiap aktivasi: tidak, `Stop` — dan periksa dari mana asalnya.

Bawaan yang masuk akal untuk sebuah service:

```csharp
new OneForOneStrategy(ex => ex switch
{
    // Sementara: layak diberi instance baru.
    TimeoutException or HttpRequestException or IOException => Directive.Restart,

    // Pesannya yang buruk, bukan actor-nya.
    ArgumentException or FormatException or JsonException => Directive.Resume,

    // Tidak dikenal: bangun ulang alih-alih menganggap state-nya selamat.
    _ => Directive.Restart,
})
{
    MaxRestarts = 10,
    Window = TimeSpan.FromMinutes(1),
}
```

## Yang belum ada

- **Backoff antar restart.** Restart terjadi seketika; hanya budget yang membatasinya.
- **Watch / notifikasi `Terminated`.** Sebuah actor belum bisa berlangganan kematian actor lain.

Keduanya ada di [roadmap](../../Plan.md).

## Selanjutnya

- [Persistensi](05-persistensi.md)
- [Pemecahan masalah](11-pemecahan-masalah.md)
