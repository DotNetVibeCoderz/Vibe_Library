using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using ScrapyGallery.Controls;
using ScrapyGallery.Demos;
using ScrapyGallery.Infrastructure;
using ScrapyNet.Sandbox;

namespace ScrapyGallery;

public sealed partial class MainWindow : Window
{
    private static readonly IReadOnlyDictionary<DemoGroup, (string En, string Id)> GroupNames = new Dictionary<DemoGroup, (string, string)>
    {
        [DemoGroup.Extract] = ("Extract data", "Ekstraksi data"),
        [DemoGroup.Crawl] = ("Crawl sites", "Menjelajah situs"),
        [DemoGroup.Process] = ("Process & export", "Olah & ekspor"),
        [DemoGroup.Operate] = ("Operate crawls", "Operasional crawl"),
        [DemoGroup.Intelligence] = ("AI & knowledge", "AI & pengetahuan"),
        [DemoGroup.Platform] = ("Run as a platform", "Platform crawler"),
    };

    private readonly DispatcherTimer _timer;
    private readonly Dictionary<string, TextBox> _inputs = new(StringComparer.Ordinal);
    private SandboxSite? _site;
    private Demo? _demo;
    private CrawlObserver? _observer;
    private CancellationTokenSource? _cts;
    private Task? _running;
    private int _renderedVersion = -1;
    private int _renderedItems = -1;
    private int _renderedLog = -1;
    private int _renderedImages = -1;
    private bool _indonesian;

    public MainWindow()
    {
        InitializeComponent();
        LangEn.Click += (_, _) => SetLanguage(false);
        LangId.Click += (_, _) => SetLanguage(true);
        RunButton.Click += (_, _) => _ = RunAsync();
        DemoList.SelectionChanged += (_, _) =>
        {
            if (DemoList.SelectedItem is ListBoxItem { Tag: Demo demo }) ShowDemo(demo);
        };
        BuildIndex();
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(80), DispatcherPriority.Background, (_, _) => Refresh());
        _timer.Start();
        Opened += async (_, _) => await EnsureSiteAsync();
        Closing += async (_, _) =>
        {
            _cts?.Cancel();
            if (_site is not null) await _site.DisposeAsync();
        };
        var first = DemoList.Items.OfType<ListBoxItem>().First(i => i.Tag is Demo);
        DemoList.SelectedItem = first;
    }

    public async Task<SandboxSite> EnsureSiteAsync()
    {
        if (_site is not null) return _site;
        _site = await SandboxSite.StartAsync();
        SiteStatus.Text = $"sandbox {_site.BaseUrl.Replace("http://", "")}";
        return _site;
    }

    // ---- index ----------------------------------------------------------------------------------------

    private void BuildIndex()
    {
        var selected = (DemoList.SelectedItem as ListBoxItem)?.Tag as Demo;
        var items = new List<ListBoxItem>();
        foreach (var group in DemoCatalog.All.GroupBy(d => d.Group))
        {
            var (en, id) = GroupNames[group.Key];
            items.Add(new ListBoxItem
            {
                IsEnabled = false,
                Focusable = false,
                Padding = new Thickness(22, items.Count == 0 ? 6 : 16, 14, 4),
                Content = new TextBlock { Text = (_indonesian ? id : en).ToUpperInvariant(), Classes = { "eyebrow" }, FontSize = 10.5, LetterSpacing = 1.4 },
            });
            foreach (var demo in group)
                items.Add(new ListBoxItem
                {
                    Tag = demo,
                    Padding = new Thickness(22, 5, 14, 5),
                    Content = new TextBlock { Text = _indonesian ? demo.TitleId : demo.Title, FontSize = 14, TextWrapping = TextWrapping.Wrap },
                });
        }
        DemoList.ItemsSource = items;
        if (selected is not null) DemoList.SelectedItem = items.FirstOrDefault(i => i.Tag == selected);
    }

    private void SetLanguage(bool indonesian)
    {
        _indonesian = indonesian;
        LangEn.IsChecked = !indonesian;
        LangId.IsChecked = indonesian;
        BuildIndex();
        if (_demo is not null) ShowHeader(_demo);
    }

    // ---- selection ------------------------------------------------------------------------------------

    private void ShowDemo(Demo demo)
    {
        if (_running is { IsCompleted: false }) _cts?.Cancel();
        _demo = demo;
        ShowHeader(demo);
        CodeText.Inlines = CodeHighlighter.Highlight(demo.SourceCode);
        InputsPanel.Children.Clear();
        _inputs.Clear();
        foreach (var input in demo.Inputs)
        {
            var box = new TextBox
            {
                Text = input.Default,
                FontFamily = Fonts.MonoFamily,
                FontSize = 12.5,
                AcceptsReturn = input.Multiline,
                TextWrapping = input.Multiline ? TextWrapping.NoWrap : TextWrapping.NoWrap,
                Height = input.Multiline ? 120 : double.NaN,
                MaxWidth = 860,
                HorizontalAlignment = HorizontalAlignment.Left,
                MinWidth = 520,
            };
            _inputs[input.Key] = box;
            InputsPanel.Children.Add(new StackPanel
            {
                Spacing = 3,
                Children =
                {
                    new TextBlock { Text = _indonesian ? input.LabelId : input.Label, Classes = { "eyebrow" } },
                    box,
                },
            });
        }
        _observer = new CrawlObserver();
        ResetViews();
        RunStatus.Text = "";
        Web.IsVisible = demo.ShowsWeb;
    }

    private void ShowHeader(Demo demo)
    {
        var (en, id) = GroupNames[demo.Group];
        Eyebrow.Text = (_indonesian ? id : en).ToUpperInvariant();
        DemoTitle.Text = _indonesian ? demo.TitleId : demo.Title;
        DemoSummary.Text = _indonesian ? demo.SummaryId : demo.Summary;
        ConceptTags.Children.Clear();
        foreach (var concept in demo.Concepts)
            ConceptTags.Children.Add(new Border { Classes = { "tag" }, Child = new TextBlock { Text = concept, Classes = { "mono" }, FontSize = 11.5 } });
        RunButton.Content = _indonesian ? "Jalankan demo" : "Run demo";
    }

    private void ResetViews()
    {
        _renderedVersion = _renderedItems = _renderedLog = _renderedImages = -1;
        Web.Update([]);
        ItemsTable.Children.Clear();
        LogText.Inlines?.Clear();
        OutputText.Text = "";
        ImagesPanel.Children.Clear();
        Chart.Update([]);
        Statuses.Update(new Dictionary<int, long>());
        RenderLedger(null);
    }

    // ---- running --------------------------------------------------------------------------------------

    /// <summary>Runs the selected demo; completes when it finishes (used by screenshot mode too).</summary>
    public async Task RunAsync()
    {
        if (_demo is null) return;
        if (_running is { IsCompleted: false })
        {
            _cts?.Cancel();
            return;
        }
        var site = await EnsureSiteAsync();
        var demo = _demo;
        _observer = new CrawlObserver();
        ResetViews();
        _cts = new CancellationTokenSource();
        var inputs = _inputs.ToDictionary(kv => kv.Key, kv => kv.Value.Text ?? "");
        var context = new DemoContext(site, _observer, inputs, _cts.Token);
        var observer = _observer;
        RunButton.Content = _indonesian ? "Hentikan" : "Stop";
        RunStatus.Text = _indonesian ? "berjalan…" : "running…";

        _running = Task.Run(() => demo.RunAsync(context));
        try
        {
            await _running;
            RunStatus.Text = string.Create(CultureInfo.InvariantCulture, $"{(_indonesian ? "selesai dalam" : "finished in")} {observer.Elapsed.TotalSeconds:0.00} s");
        }
        catch (OperationCanceledException)
        {
            RunStatus.Text = _indonesian ? "dihentikan" : "stopped";
        }
        catch (Exception ex)
        {
            RunStatus.Text = (_indonesian ? "gagal: " : "failed: ") + ex.Message;
            observer.AppendOutput(ex.ToString());
        }
        finally
        {
            observer.Finished = true;
            RunButton.Content = _indonesian ? "Jalankan demo" : "Run demo";
            Refresh();
        }
    }

    // ---- rendering ------------------------------------------------------------------------------------

    private void Refresh()
    {
        var observer = _observer;
        if (observer is null) return;
        if (_running is { IsCompleted: false }) observer.Tick();
        if (observer.Version == _renderedVersion && !Web.IsGrowing && _running is not { IsCompleted: false }) return;
        _renderedVersion = observer.Version;
        var s = observer.Snapshot();

        Web.Update(s.Nodes);
        RenderLedger(s);
        Statuses.Update(s.Statuses);
        var series = new List<ChartSeries>
        {
            new("responses", [.. s.Timeline.Select(t => new SeriesPoint(t.T, t.Responses))], Palette.Moss),
            new("items", [.. s.Timeline.Select(t => new SeriesPoint(t.T, t.Items))], Palette.Argiope),
        };
        var colors = new[] { Palette.Dusk, Palette.Rust, Palette.Ink };
        var i = 0;
        foreach (var (name, points) in s.Series) series.Add(new ChartSeries(name, points, colors[i++ % colors.Length]));
        if (s.Series.Count > 0) series.RemoveAll(x => x.Name is "responses" or "items" && s.Timeline.Count < 2);
        Chart.Title = s.Series.Count > 0 ? string.Join(" · ", s.Series.Keys) : "Crawl progress";
        Chart.XLabel = s.Series.Count > 0 ? "" : "s";
        Chart.Update(s.Series.Count > 0 ? [.. series.Where(x => s.Series.ContainsKey(x.Name))] : series);

        if (s.Items.Count != _renderedItems)
        {
            _renderedItems = s.Items.Count;
            RenderItems(s);
        }
        if (s.Log.Count != _renderedLog)
        {
            _renderedLog = s.Log.Count;
            RenderLog(s.Log);
        }
        OutputText.Text = s.Output ?? "";
        if (s.Images.Count != _renderedImages)
        {
            _renderedImages = s.Images.Count;
            ImagesPanel.Children.Clear();
            foreach (var path in s.Images.Take(48))
            {
                try
                {
                    ImagesPanel.Children.Add(new Border
                    {
                        Margin = new Thickness(0, 0, 12, 12),
                        BorderBrush = Palette.Brush(Palette.Thread),
                        BorderThickness = new Thickness(1),
                        Child = new Image { Source = new Bitmap(path), MaxHeight = 140, MaxWidth = 420, Stretch = Stretch.Uniform },
                    });
                }
                catch (Exception)
                {
                    // Not an image Avalonia can decode; skip it.
                }
            }
        }
    }

    private void RenderLedger(ObserverSnapshot? s)
    {
        Ledger.Children.Clear();
        var rate = s is null || s.Elapsed.TotalSeconds < 0.05 ? 0 : s.Responses / s.Elapsed.TotalMinutes;
        (string En, string Id, string Value)[] rows =
        [
            ("Requests", "Request", Fmt(s?.Requests)),
            ("Responses", "Respons", Fmt(s?.Responses)),
            ("Items", "Item", Fmt(s?.ItemCount)),
            ("Dropped", "Dibuang", Fmt(s?.Dropped)),
            ("Spider errors", "Error spider", Fmt(s?.Errors)),
            ("Retries", "Retry", Fmt(s?.Retries)),
            ("Redirects", "Redirect", Fmt(s?.Redirects)),
            ("Cache hits", "Cache hit", Fmt(s?.CacheHits)),
            ("Downloaded", "Terunduh", s is null ? "—" : Bytes(s.Bytes)),
            ("Pages / min", "Halaman / menit", s is null ? "—" : rate.ToString("N0", CultureInfo.InvariantCulture)),
        ];
        foreach (var (en, id, value) in rows)
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 1) };
            grid.Children.Add(new TextBlock { Text = _indonesian ? id : en, FontSize = 13, Foreground = Palette.Brush(Palette.InkSoft), VerticalAlignment = VerticalAlignment.Center });
            var v = new TextBlock { Text = value, Classes = { "mono" }, FontSize = 16, HorizontalAlignment = HorizontalAlignment.Right };
            Grid.SetColumn(v, 1);
            grid.Children.Add(v);
            Ledger.Children.Add(new Border { BorderBrush = Palette.Brush(Palette.Thread, 0.5), BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 4), Child = grid });
        }

        var legend = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        foreach (var (label, color) in new[] { ("2xx", Palette.Moss), ("3xx", Palette.Dusk), ("error", Palette.Rust), (_indonesian ? "antri" : "queued", Palette.Thread) })
        {
            legend.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 5,
                Margin = new Thickness(0, 0, 12, 4),
                Children =
                {
                    new Avalonia.Controls.Shapes.Ellipse { Width = 8, Height = 8, Fill = Palette.Brush(color), VerticalAlignment = VerticalAlignment.Center },
                    new TextBlock { Text = label, Classes = { "mono" }, FontSize = 11, Foreground = Palette.Brush(Palette.InkSoft) },
                },
            });
        }
        Ledger.Children.Add(legend);

        static string Fmt(long? v) => v is null ? "—" : v.Value.ToString("N0", CultureInfo.InvariantCulture);
        static string Bytes(long b) => b < 1024 ? $"{b} B" : b < 1024 * 1024 ? $"{b / 1024.0:0.0} KB" : $"{b / 1024.0 / 1024.0:0.0} MB";
    }

    private void RenderItems(ObserverSnapshot s)
    {
        ItemsTable.Children.Clear();
        ItemsTab.Header = s.Items.Count > 0 ? $"Items · {s.Items.Count:N0}" : "Items";
        if (s.Items.Count == 0)
        {
            ItemsTable.Children.Add(new TextBlock
            {
                Text = _indonesian ? "Belum ada item. Jalankan demo untuk mengisi tabel ini." : "No items yet. Run the demo to fill this table.",
                Foreground = Palette.Brush(Palette.InkSoft),
                Margin = new Thickness(4, 10),
            });
            return;
        }
        var columns = s.Columns.Take(8).ToList();
        var widths = columns.Select(c => Math.Clamp(Math.Max(c.Length, s.Items.Take(60).Max(r => r.GetValueOrDefault(c)?.Length ?? 0)) * 7.4 + 22, 60, 420)).ToList();
        Grid Row(IEnumerable<string> cells, bool header)
        {
            var grid = new Grid();
            foreach (var w in widths) grid.ColumnDefinitions.Add(new ColumnDefinition(w, GridUnitType.Pixel));
            var col = 0;
            foreach (var cell in cells)
            {
                var tb = new TextBlock
                {
                    Text = cell.ReplaceLineEndings(" "),
                    TextWrapping = TextWrapping.NoWrap,
                    Classes = { header ? "eyebrow" : "mono" },
                    FontSize = header ? 11 : 12,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(6, 5, 12, 5),
                };
                ToolTip.SetTip(tb, cell);
                Grid.SetColumn(tb, col++);
                grid.Children.Add(tb);
            }
            return grid;
        }
        ItemsTable.Children.Add(new Border { BorderBrush = Palette.Brush(Palette.Ink), BorderThickness = new Thickness(0, 0, 0, 1.5), Child = Row(columns.Select(c => c.ToUpperInvariant()), true) });
        var n = 0;
        foreach (var item in s.Items.Take(250))
        {
            ItemsTable.Children.Add(new Border
            {
                Background = n++ % 2 == 0 ? Brushes.Transparent : Palette.Brush(Palette.SilkDeep, 0.6),
                Child = Row(columns.Select(c => item.GetValueOrDefault(c) ?? ""), false),
            });
        }
    }

    private void RenderLog(IReadOnlyList<LogLine> lines)
    {
        var inlines = new Avalonia.Controls.Documents.InlineCollection();
        foreach (var line in lines.TakeLast(400))
        {
            var color = line.Level switch
            {
                LogLevel.Warning => Palette.Argiope,
                LogLevel.Error or LogLevel.Critical => Palette.Rust,
                LogLevel.Debug => Palette.Thread,
                _ => Palette.Ink,
            };
            inlines.Add(new Avalonia.Controls.Documents.Run(line.Text + "\n") { Foreground = Palette.Brush(color) });
        }
        LogText.Inlines = inlines;
        LogScroll.ScrollToEnd();
    }

    // ---- screenshot support -----------------------------------------------------------------------------

    public void Select(string demoId)
    {
        var item = DemoList.Items.OfType<ListBoxItem>().First(i => i.Tag is Demo d && d.Id == demoId);
        DemoList.SelectedItem = item;
    }

    public void SelectTab(int index) => Tabs.SelectedIndex = index;

    public void SetIndonesian(bool indonesian) => SetLanguage(indonesian);

    public void ForceRefresh()
    {
        _renderedVersion = -1;
        Refresh();
    }
}
