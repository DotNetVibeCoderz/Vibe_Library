using System.Globalization;
using Lvgl.Gallery.Shell;
using Lvgl.Widgets;

namespace Lvgl.Gallery.Work;

/// <summary>
/// A stock list kept in SQLite: search it, adjust quantities with the - and + buttons, add and
/// remove products. Rows at or below their reorder level are flagged. Every change is a real
/// UPDATE, INSERT or DELETE, so it is still there next time the gallery starts.
/// </summary>
internal sealed class Inventory : Demo
{
    private static readonly string[] Groups = ["Boards", "Displays", "Sensors", "Power", "Enclosures", "Input"];

    private GalleryDatabase _db = null!;
    private LvTextArea _search = null!;
    private LvKeyboard _keyboard = null!;
    private LvObject _rows = null!;
    private LvLabel _count = null!, _units = null!, _value = null!, _low = null!;
    private LvPanel? _form;
    private LvObject _stage = null!;
    private int _width;

    public override Category Category => Category.Work;
    public override string Title => "Inventory";
    public override string Summary => "A stock list in SQLite: search, adjust, add and delete - every change is a real SQL statement.";
    public override string BuiltWith => "LvTextArea  LvKeyboard  LvDropdown  SQLite";

    protected override void Build(LvObject stage, int width, int height)
    {
        _db = Context.Database;
        (_stage, _width) = (stage, width);

        // Figures across the top.
        var tileWidth = (width - 3 * 12) / 4;
        _count = Figure(stage, 0, tileWidth, "Products");
        _units = Figure(stage, 1, tileWidth, "Units in stock");
        _value = Figure(stage, 2, tileWidth, "Stock value");
        _low = Figure(stage, 3, tileWidth, "To reorder");

        // Search and add.
        _search = new LvTextArea(stage)
        {
            IsSingleLine = true,
            PlaceholderText = "Search name, SKU or group",
        };
        _search.SetSize(width - 160, 44);
        _search.SetPosition(0, 92);
        _search.SetFont(Fonts[Face.Body, 16]);
        _search.SetRadius(10);
        _search.SetBorderColor(Palette.Rule);
        _search.SetBorderColor(Accent, LvPart.Main, LvState.Focused);
        _search.ValueChanged += (_, _) => Refresh();

        var add = Button(stage, "Add product", 148, 44, size: 16);
        add.SetPosition(width - 148, 92);
        add.Clicked += (_, _) => OpenForm();

        // The list.
        var list = Card(stage, 0, 148, width, height - 148);
        list.SetPadding(0);

        var header = Bare(new LvPanel(list, width - 2, 34));
        header.SetBackgroundOpacity(255);
        header.SetBackgroundColor(Palette.Sunken);
        Column(header, "Product", 16);
        Column(header, "Group", width - 470);
        Column(header, "Quantity", width - 336);
        Column(header, "Price", width - 176);
        Text(header, "VALUE", 11, Face.Mono, Palette.InkMuted).Align(LvAlign.RightMid, -18, 0);

        _rows = Bare(new LvPanel(list, width - 2, height - 148 - 36));
        _rows.SetPosition(0, 34);
        _rows.SetFlexFlow(LvFlexFlow.Column);
        _rows.IsScrollable = true;

        // An on-screen keyboard for touch panels, shown while the search box has focus.
        _keyboard = new LvKeyboard(stage) { TextArea = _search, IsHidden = true };
        _keyboard.SetSize(width, 190);
        _keyboard.Align(LvAlign.BottomMid);
        _search.AddHandler(LvEventCode.Focused, (_, _) => _keyboard.IsHidden = false);
        _keyboard.AddHandler(LvEventCode.Ready, (_, _) => _keyboard.IsHidden = true);
        _keyboard.AddHandler(LvEventCode.Cancel, (_, _) => _keyboard.IsHidden = true);

        Refresh();
    }

    private LvLabel Figure(LvObject stage, int index, int width, string caption)
    {
        var tile = Card(stage, index * (width + 12), 0, width, 78);
        Caption(tile, caption).SetPosition(16, 12);
        var value = Text(tile, "", 30, Face.Display, Palette.Ink);
        value.SetPosition(16, 32);
        return value;
    }

    private void Column(LvObject header, string text, int x) =>
        Text(header, text.ToUpperInvariant(), 11, Face.Mono, Palette.InkMuted).Align(LvAlign.LeftMid, x, 0);

    private void Refresh()
    {
        var products = _db.Products(_search.Text);
        var all = _db.Products();

        _count.Text = all.Count.ToString(CultureInfo.InvariantCulture);
        _units.Text = all.Sum(p => p.Quantity).ToString("#,0", CultureInfo.InvariantCulture);
        _value.Text = _db.StockValue().ToString("$#,0", CultureInfo.InvariantCulture);
        var low = all.Count(p => p.IsLow);
        _low.Text = low.ToString(CultureInfo.InvariantCulture);
        _low.SetTextColor(low > 0 ? Palette.Bad : Palette.Good);

        _rows.Clear();
        if (products.Count == 0)
        {
            var empty = Text(_rows, $"Nothing matches \"{_search.Text}\". Clear the search to see every product.", 15, Face.Body, Palette.InkMuted);
            empty.SetPadding(16);
            return;
        }

        var rowWidth = _width - 2;
        foreach (var product in products)
        {
            var row = Bare(new LvPanel(_rows, rowWidth, 52));
            Box(row, 0, 51, rowWidth, 1, Palette.Sunken);

            Text(row, product.Name, 16, Face.Body, Palette.Ink).SetPosition(16, 7);
            Text(row, product.Sku, 11, Face.Mono, Palette.InkFaint).SetPosition(16, 30);

            var chip = Box(row, _width - 472, 15, 92, 22, Tint, 11);
            Text(chip, product.Group, 12, Face.Strong, Accent).Center();

            var minus = Button(row, "-", 30, 30, Palette.Sunken, Palette.Ink, 18);
            minus.SetPosition(_width - 336, 11);
            var quantity = Text(row, product.Quantity.ToString(CultureInfo.InvariantCulture), 22, Face.Display,
                product.IsLow ? Palette.Bad : Palette.Ink);
            quantity.SetPosition(_width - 296, 12);
            var plus = Button(row, "+", 30, 30, Palette.Sunken, Palette.Ink, 18);
            plus.SetPosition(_width - 250, 11);

            if (product.IsLow)
            {
                var flag = Text(row, "LOW", 10, Face.Mono, Palette.Bad);
                flag.SetPosition(_width - 212, 19);
            }

            Text(row, product.Price.ToString("$0.00", CultureInfo.InvariantCulture), 14, Face.Mono, Palette.InkMuted)
                .SetPosition(_width - 176, 17);
            Text(row, (product.Price * product.Quantity).ToString("$#,0", CultureInfo.InvariantCulture), 14, Face.Mono, Palette.Ink)
                .Align(LvAlign.RightMid, -18, 0);

            var delete = IconButton(row, LvSymbols.Trash, 28, 28, Palette.Card, Palette.InkFaint, 14);
            delete.SetPosition(_width - 512, 12);
            delete.SetShadowColor(Palette.Card);

            var id = product.Id;
            minus.Clicked += (_, _) => { _db.AdjustQuantity(id, -1); App.Post(Refresh); };
            plus.Clicked += (_, _) => { _db.AdjustQuantity(id, +1); App.Post(Refresh); };
            delete.Clicked += (_, _) => { _db.DeleteProduct(id); App.Post(Refresh); };
        }
    }

    // A small form over the list. Refresh is posted rather than called inline because the click
    // that triggered it belongs to a widget the refresh deletes.
    private void OpenForm()
    {
        if (_form is not null) return;

        _form = Card(_stage, (_width - 420) / 2, 40, 420, 250);
        _form.SetBorderColor(Accent);
        _form.SetBorderWidth(2);

        Text(_form, "New product", 24, Face.Display, Palette.Ink).SetPosition(20, 14);

        var name = new LvTextArea(_form) { IsSingleLine = true, PlaceholderText = "Name" };
        name.SetSize(380, 42);
        name.SetPosition(20, 56);
        name.SetFont(Fonts[Face.Body, 15]);

        var group = new LvDropdown(_form, Groups);
        group.SetSize(180, 40);
        group.SetPosition(20, 110);
        group.SetFont(Drawing.LvFont.Montserrat(14));

        var quantity = new LvSpinbox(_form);
        quantity.SetRange(0, 999);
        quantity.SetDigitFormat(3);
        quantity.Value = 10;
        quantity.SetSize(110, 40);
        quantity.SetPosition(290, 110);

        var cancel = Button(_form, "Cancel", 120, 42, Palette.Sunken, Palette.Ink, 15);
        cancel.SetPosition(160, 190);
        var save = Button(_form, "Save", 120, 42, size: 15);
        save.SetPosition(290, 190);

        _keyboard.TextArea = name;

        cancel.Clicked += (_, _) => App.Post(CloseForm);
        save.Clicked += (_, _) =>
        {
            var text = name.Text.Trim();
            if (text.Length == 0) { name.SetBorderColor(Palette.Bad); return; }
            _db.AddProduct(text, group.SelectedOption, quantity.Value, 9.99m);
            App.Post(() => { CloseForm(); Refresh(); });
        };
    }

    private void CloseForm()
    {
        _form?.Delete();
        _form = null;
        _keyboard.TextArea = _search;
        _keyboard.IsHidden = true;
    }
}
