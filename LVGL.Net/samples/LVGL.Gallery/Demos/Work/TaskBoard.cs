using Lvgl.Drawing;
using Lvgl.Gallery.Shell;
using Lvgl.Widgets;

namespace Lvgl.Gallery.Work;

/// <summary>
/// A three-column work board backed by SQLite. Tap a card to move it one column on; a card in Done
/// goes back to To do. The bar at the top tracks finished story points.
/// </summary>
internal sealed class TaskBoard : Demo
{
    private static readonly (string Status, string Title)[] Columns = [("todo", "To do"), ("doing", "In progress"), ("done", "Done")];

    private static readonly Dictionary<string, uint> People = new()
    {
        ["Ayu"] = 0x0B8F63, ["Budi"] = 0x2F62E8, ["Citra"] = 0xD8336B, ["Dewi"] = 0xC9690E,
    };

    private GalleryDatabase _db = null!;
    private readonly List<(LvObject Body, LvLabel Count)> _columns = [];
    private LvBar _progress = null!;
    private LvLabel _progressText = null!;
    private int _columnWidth;

    public override Category Category => Category.Work;
    public override string Title => "Task board";
    public override string Summary => "Work items in three columns. Tap a card to move it on - the status is written to SQLite immediately.";
    public override string BuiltWith => "LvPanel  LvBar  flex layout  SQLite";

    protected override void Build(LvObject stage, int width, int height)
    {
        _db = Context.Database;

        Text(stage, "Sprint 14", 26, Face.Display, Palette.Ink).SetPosition(0, 0);
        _progressText = Text(stage, "", 13, Face.Mono, Palette.InkMuted);
        _progressText.Align(LvAlign.TopRight, 0, 8);

        _progress = new LvBar(stage);
        _progress.SetSize(width, 8);
        _progress.SetPosition(0, 40);
        _progress.SetBackgroundColor(Palette.Sunken);
        _progress.SetBackgroundColor(Accent, LvPart.Indicator);

        _columnWidth = (width - 2 * 14) / 3;
        for (var i = 0; i < Columns.Length; i++)
        {
            var column = Box(stage, i * (_columnWidth + 14), 64, _columnWidth, height - 64, Palette.Sunken, 14);

            Text(column, Columns[i].Title, 20, Face.Display, Palette.Ink).SetPosition(14, 10);
            var count = Text(column, "", 12, Face.Mono, Palette.InkMuted);
            count.Align(LvAlign.TopRight, -14, 16);

            var body = Bare(new LvPanel(column, _columnWidth - 20, height - 64 - 48));
            body.SetPosition(10, 44);
            body.SetFlexFlow(LvFlexFlow.Column);
            body.SetGap(8, 0);
            body.IsScrollable = true;

            _columns.Add((body, count));
        }

        Refresh();
    }

    private void Refresh()
    {
        var items = _db.WorkItems();

        for (var c = 0; c < Columns.Length; c++)
        {
            var (body, count) = _columns[c];
            body.Clear();

            var inColumn = items.Where(item => item.Status == Columns[c].Status).ToList();
            count.Text = $"{inColumn.Count}  ·  {inColumn.Sum(i => i.Points)} pts";

            foreach (var item in inColumn) Card(body, item, c == Columns.Length - 1);
            if (inColumn.Count == 0) Text(body, "Nothing here.", 14, Face.Body, Palette.InkFaint);
        }

        var total = items.Sum(i => i.Points);
        var done = items.Where(i => i.Status == "done").Sum(i => i.Points);
        _progress.SetRange(0, Math.Max(total, 1));
        _progress.SetValue(done, animate: true);
        _progressText.Text = $"{done} of {total} points done";
    }

    private void Card(LvObject body, WorkItem item, bool done)
    {
        var card = Box(body, 0, 0, _columnWidth - 20, 84, Palette.Card, 10);
        card.IsClickable = true;
        card.SetBackgroundColor(Tint, LvPart.Main, LvState.Pressed);

        var title = Text(card, item.Title, 16, Face.Body, done ? Palette.InkMuted : Palette.Ink);
        title.SetSize(_columnWidth - 48, 22);
        title.SetPosition(12, 10);
        title.LongMode = LvLabelLongMode.Dot;

        var colour = LvColor.FromRgb(People.GetValueOrDefault(item.Owner, 0x5B6878u));
        var avatar = Box(card, 12, 46, 26, 26, colour, 13);
        Text(avatar, item.Owner[..1], 13, Face.Strong, Palette.White).Center();
        Text(card, item.Owner, 13, Face.Body, Palette.InkMuted).SetPosition(46, 50);

        var points = Box(card, _columnWidth - 20 - 50, 48, 38, 22, done ? Palette.Sunken : Tint, 11);
        Text(points, item.Points.ToString(), 13, Face.Strong, done ? Palette.InkMuted : Accent).Center();

        var next = Columns[(Array.FindIndex(Columns, c => c.Status == item.Status) + 1) % Columns.Length].Status;
        card.Clicked += (_, _) =>
        {
            _db.MoveWorkItem(item.Id, next);
            App.Post(Refresh);   // the card being clicked is one Refresh deletes
        };
    }
}
