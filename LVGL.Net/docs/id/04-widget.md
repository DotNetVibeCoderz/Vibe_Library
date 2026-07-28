# Widget dan styling

## Daur hidup

LVGL memiliki pohon objeknya. Karena itu `LvObject` **tidak** mengimplementasikan `IDisposable` —
men-dispose anak yang induknya sudah membebaskannya berarti double free.

```csharp
var panel = new LvPanel(app.Screen, 300, 200);
var label = new LvLabel(panel, "di dalam panel");

panel.Delete();      // menghapus panel beserta label di dalamnya
panel.Clear();       // menghapus anak-anaknya, panel tetap ada
```

Setelah dihapus, `IsAlive` menjadi false dan akses properti berikutnya melempar
`ObjectDisposedException`, sehingga referensi basi gagal secara jelas alih-alih menulis ke memori
yang sudah dibebaskan.

`LvStyle` adalah pengecualian: ia **memang** disposable, karena LVGL menyimpan pointer mentah
kepadanya. Dispose sebuah style hanya setelah semua widget yang memakainya lenyap.

## Kumpulan widget

| Kelas | Widget LVGL | Catatan |
|-------|-------------|---------|
| `LvPanel` | `lv_obj` | Kontainer polos; balok penyusun layout |
| `LvLabel` | `lv_label` | `Text`, `LongMode` |
| `LvButton` | `lv_button` | `Text` mengelola label anak yang terpusat; `IsToggle` |
| `LvSlider` | `lv_slider` | `Value`, `SetRange`, `SetValue(v, animate)` |
| `LvBar` | `lv_bar` | Indikator progres hanya-baca |
| `LvArc` | `lv_arc` | Dial atau input putar; `IsReadOnly` |
| `LvSwitch` | `lv_switch` | `IsOn` |
| `LvCheckbox` | `lv_checkbox` | `Text`, `IsChecked` |
| `LvDropdown` | `lv_dropdown` | `Options`, `SelectedIndex`, `SelectedOption` |
| `LvRoller` | `lv_roller` | Pemilih yang ramah sentuh |
| `LvTextArea` | `lv_textarea` | `Text`, `PlaceholderText`, `IsSingleLine` |
| `LvChart` | `lv_chart` | `AddSeries`, `PointCount`, `UpdateMode` |
| `LvScreen` | objek screen | `Create()`, `Load()` untuk aplikasi banyak halaman |

## Geometri dan layout

```csharp
widget.SetPosition(20, 40);
widget.SetSize(200, 48);
widget.Align(LvAlign.Center);
widget.Align(LvAlign.TopRight, -12, 12);      // dengan offset
widget.AlignTo(other, LvAlign.OutBottomMid, 0, 8);
widget.Center();
```

Perataan dan posisi absolut adalah dua alternatif — LVGL memperlakukan offset perataan *sebagai*
posisi, jadi menyetel keduanya membuat koordinat eksplisit tidak berarti.

### Koordinat khusus

LVGL menyandikan persentase dan ukuran-mengikuti-konten pada bit tinggi sebuah `int` biasa:

```csharp
widget.Width = LvCoord.Percent(50);
widget.SetSize(LvCoord.SizeContent, LvCoord.SizeContent);   // menyusut mengikuti isi
```

**Jangan pernah melakukan aritmetika pada nilai-nilai ini.** `LvCoord.Percent(100) - 190` bukan
berarti "100% dikurangi 190 piksel" — itu merusak penyandiannya dan menghasilkan jumlah piksel yang
tidak bermakna. Hitunglah dalam piksel:

```csharp
panel.SetSize(app.Display.Width - SidebarWidth, app.Display.Height);
```

### Flex

```csharp
container.SetFlexFlow(LvFlexFlow.RowWrap);
container.SetFlexAlign(LvFlexAlign.SpaceBetween, LvFlexAlign.Center, LvFlexAlign.Start);
container.SetGap(rowGap: 12, columnGap: 12);
```

## Styling

Dua tingkat. Properti lokal, disetel per widget:

```csharp
button.SetBackgroundColor(LvColor.FromRgb(0x38BDF8u));
button.SetRadius(10);
button.SetPadding(12);
button.SetFontSize(20);
button.SetBorderWidth(0);
```

Dan style bersama, ketika tampilan yang sama berulang:

```csharp
_cardStyle = new LvStyle()
    .BackgroundColor(surface)
    .Radius(14)
    .Padding(16)
    .BorderWidth(0);

card1.AddStyle(_cardStyle);
card2.AddStyle(_cardStyle);
```

LVGL menyimpan satu tabel properti dan widget-widget merujuknya, alih-alih masing-masing membawa
salinannya sendiri.

### Part dan state

Setiap setter style menerima part dan state opsional, yang bersama-sama membentuk selector style
LVGL:

```csharp
slider.SetBackgroundColor(accent, LvPart.Indicator);              // bagian yang terisi
slider.SetBackgroundColor(accent, LvPart.Knob);                   // pegangannya
button.SetBackgroundColor(accent.Darken(0.35f), LvPart.Main, LvState.Pressed);
toggle.SetBackgroundColor(green, LvPart.Indicator, LvState.Checked);
```

Nilai `LvPart`: `Main`, `Indicator`, `Knob`, `Items`, `Selected`, `Cursor`, `Scrollbar`.
Nilai `LvState` mencakup `Pressed`, `Checked`, `Focused`, `Disabled`, `Hovered`.

### Warna

`LvColor` adalah nilai RGB 24-bit dengan beberapa kemudahan:

```csharp
LvColor.FromRgb(0x38BDF8u)
LvColor.Parse("#38BDF8")
LvColor.TryParse(userInput, out var color)      // false, bukan melempar exception
accent.Darken(0.3f)
accent.Lighten(0.2f)
background.ContrastingText()                    // hitam atau putih, mana yang lebih terbaca
```

Font tersedia dalam ukuran Montserrat bawaan: 12, 14, 16, 20, 24, 28, 36. Ukuran yang tidak
dikompilasi ke dalam library native akan jatuh ke font bawaan, bukan gagal.

## Event

```csharp
button.Clicked      += (sender, e) => { };
slider.ValueChanged += (sender, e) => { };
widget.Deleted      += (sender, e) => { };

widget.AddHandler(LvEventCode.LongPressed, OnLongPress);
widget.RemoveHandler(LvEventCode.LongPressed, OnLongPress);
```

Nilai `LvEventCode` adalah pengenal stabil milik LVGL.Net; shim native menerjemahkannya ke
`lv_event_code_t` pada build yang dimuat, yang ordinalnya berubah sepanjang seri 9.x.

Exception yang dilempar dari handler akan ditelan — ia tidak boleh naik ke tumpukan C milik LVGL.
Tangani kegagalan di dalam handler itu sendiri.

## Chart

Dirancang untuk data mengalir:

```csharp
var chart = new LvChart(parent);
chart.SetSize(600, 240);
chart.Type = LvChartType.Line;
chart.PointCount = 120;
chart.UpdateMode = LvChartUpdateMode.Shift;      // sampel terlama keluar dari sisi kiri
chart.SetRange(LvChartAxis.PrimaryY, 0, 100);

var cpu = chart.AddSeries(LvColor.Blue);
cpu.Fill(0);                                     // isi awal, agar tidak merambat naik dari nol

// selanjutnya, sekali per sampel
cpu.AddPoint(value);
```

LVGL mengelola ring buffer-nya secara internal, jadi satu sampel hanya berbiaya satu panggilan
native dan tidak mengalokasikan apa pun.

## Simbol

```csharp
new LvButton(parent, $"{LvSymbols.Save} Simpan");
label.Text = $"{LvSymbols.Warning} Sensor terputus";
```

`LvSymbols` menyediakan glif FontAwesome bawaan LVGL: `Ok`, `Close`, `Settings`, `Home`, `Refresh`,
`Play`, `Pause`, `Warning`, `Wifi`, `BatteryFull`, `Charge`, `Trash`, `Save`, dan lainnya.
