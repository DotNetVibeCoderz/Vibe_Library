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
kepadanya. Dispose sebuah style hanya setelah semua widget yang memakainya lenyap. Aturan yang sama
berlaku untuk hal-hal lain yang dirujuk widget - `LvFont` TrueType dan `LvImageSource` piksel - serta
untuk `UiBuilder`, yang memiliki style dan font dari layout yang dibangunnya.

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
| `LvImage` | `lv_image` | Berkas PNG/BMP, piksel saat runtime, atau simbol; `Rotation`, `Scale`, `InnerAlign` |
| `LvTabView` / `LvTabPage` | `lv_tabview` | `AddTab` mengembalikan halamannya; `ActiveTab`, `TabBarPosition` |
| `LvList` / `LvListButton` | `lv_list` | `AddText` untuk judul, `AddButton(text, symbol)` |
| `LvTileView` / `LvTile` | `lv_tileview` | `AddTile(column, row, allowedDirections)`, `ShowTile` |
| `LvMessageBox` | `lv_msgbox` | `Show(title, text, buttons...)` untuk dialog modal; `Close` |
| `LvKeyboard` | `lv_keyboard` | Tombol di layar untuk `LvTextArea`; `Mode`, `ShowPopovers` |
| `LvSpinner` | `lv_spinner` | Indikator sibuk; `SetAnimation(duration, sweep)` |
| `LvLed` | `lv_led` | `Color`, `Brightness`, `On`/`Off`/`Toggle` |
| `LvScale` | `lv_scale` | Tick dan label untuk gauge dan penggaris; `Mode`, `SetRange`, jumlah tick |
| `LvTable` | `lv_table` | `this[row, column]`, `SetRows`, `SetColumnWidth` |
| `LvSpinbox` | `lv_spinbox` | Nilai integer yang ditampilkan dengan titik desimal; `Increment`/`Decrement` |
| `LvScreen` | objek screen | `Create()`, `Load()` untuk aplikasi banyak halaman |

Beberapa widget yang lebih baru dalam penggunaan:

```csharp
var tabs = new LvTabView(app.Screen);
var status = tabs.AddTab("Status");
new LvLabel(status, "on the first page");

var list = new LvList(tabs.AddTab("Settings"));
list.AddText("Connectivity");
list.AddButton("Wi-Fi", LvSymbols.Wifi).Clicked += (_, _) => OpenWifi();

// Sebuah gauge: scale bundar untuk tick, arc di atasnya untuk nilainya.
var scale = new LvScale(status) { Mode = LvScaleMode.RoundInner, TotalTickCount = 21, MajorTickEvery = 5, AngleRange = 270, Rotation = 135, ShowLabels = true };
scale.SetRange(0, 100);

var field = new LvTextArea(app.Screen) { IsSingleLine = true };
var keyboard = new LvKeyboard(app.Screen) { TextArea = field };

var box = LvMessageBox.Show("Delete profile", "This cannot be undone.", "Cancel", "Delete");
box.Buttons[0].Clicked += (_, _) => box.Close();
```

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

`X` dan `Y` relatif terhadap area konten induknya. `GetBounds()` mengembalikan posisi widget yang
sebenarnya di layar - setelah padding dan scroll setiap leluhurnya diperhitungkan - yang dibutuhkan
untuk hit-testing dan overlay. `ZIndex` membaca dan mengubah urutan gambar di antara
saudara-saudaranya.

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
Nilai `LvState` mencakup `Pressed`, `Checked`, `Focused`, `Disabled`, `Hovered`, `Alt`, serta
`User1`-`User4` untuk state Anda sendiri.

Sebagian part punya ukurannya sendiri - penanda titik pada chart, knob pada slider. `SetPartSize`
menyetelnya; nol menyembunyikan penanda chart:

```csharp
chart.SetPartSize(0, 0, LvPart.Indicator);   // garis polos, tanpa titik pada setiap sampel
```

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

`SetFontSize` memilih salah satu ukuran Montserrat bawaan: 12, 14, 16, 20, 24, 28, 36. Ukuran yang
tidak dikompilasi ke dalam library native akan jatuh ke font bawaan, bukan gagal. Untuk ukuran atau
aksara lain, muat font TrueType - lihat [Gambar, font, dan animasi](11-gambar-font-animasi.md).

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

Exception yang dilempar dari handler tidak boleh naik ke tumpukan C milik LVGL, jadi exception itu
ditangkap di batas callback dan dilaporkan melalui `LvglRuntime.CallbackFailed` - berlanggananlah
pada event itu untuk mencatatnya. Tangani kegagalan yang sudah diperkirakan di dalam handler itu
sendiri.

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
cpu.Fill(LvChartSeries.NoValue);                 // kosong: garis dimulai dari sampel pertama

// selanjutnya, sekali per sampel
cpu.AddPoint(value);
```

LVGL mengelola ring buffer-nya secara internal, jadi satu sampel hanya berbiaya satu panggilan
native dan tidak mengalokasikan apa pun.

Sejak LVGL 9.5, garis pembagi grid digambar dengan gaya garis milik part **main**, sehingga
`chart.SetLineColor(color)` mewarnainya (di 9.2 dulu `LvPart.Items`). LVGL juga menggambar penanda
pada setiap titik secara bawaan; `SetPartSize(0, 0, LvPart.Indicator)` mematikannya.

## Simbol

```csharp
new LvButton(parent, $"{LvSymbols.Save} Simpan");
label.Text = $"{LvSymbols.Warning} Sensor terputus";
```

`LvSymbols` menyediakan glif FontAwesome bawaan LVGL: `Ok`, `Close`, `Settings`, `Home`, `Refresh`,
`Play`, `Pause`, `Warning`, `Wifi`, `BatteryFull`, `Charge`, `Trash`, `Save`, dan lainnya.
