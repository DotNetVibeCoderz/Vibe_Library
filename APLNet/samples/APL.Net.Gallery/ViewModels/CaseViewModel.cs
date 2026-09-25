// APL.Net - Another Parallel Library for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using Avalonia.Media.Imaging;
using Avalonia.Threading;
using AplNet.Gallery.Cases;
using AplNet.Gallery.Infrastructure;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AplNet.Gallery.ViewModels;

public sealed partial class CaseViewModel : ObservableObject
{
    private static readonly TimeSpan s_budget = TimeSpan.FromMilliseconds(500);
    private readonly bool _hasLanes;

    public CaseViewModel(GalleryCase galleryCase)
    {
        Case = galleryCase;
        _hasLanes = galleryCase is MandelbrotCase or ZipfWorkloadCase;
    }

    public GalleryCase Case { get; }

    /// <summary>Set by the shell so a sidebar entry can open its own case.</summary>
    public Action<CaseViewModel>? Navigate { get; set; }

    [ObservableProperty]
    private bool _isSelected;

    [RelayCommand]
    private void Open() => Navigate?.Invoke(this);

    [RelayCommand]
    private void ShowApl()
    {
        CodeTab = 0;
        OnPropertyChanged(nameof(ShowsAplCode));
        OnPropertyChanged(nameof(ShowsTplCode));
    }

    [RelayCommand]
    private void ShowTpl()
    {
        CodeTab = 1;
        OnPropertyChanged(nameof(ShowsAplCode));
        OnPropertyChanged(nameof(ShowsTplCode));
    }

    private static Loc L => Loc.Instance;

    public string Glyph => Case.Category.Glyph;

    public string CategoryName => L.Pick(Case.Category.NameEn, Case.Category.NameId);

    public string GlyphMeaning => L.Pick(Case.Category.GlyphMeaningEn, Case.Category.GlyphMeaningId);

    public string Title => L.Pick(Case.TitleEn, Case.TitleId);

    public string Summary => L.Pick(Case.SummaryEn, Case.SummaryId);

    public string Takeaway => L.Pick(Case.TakeawayEn, Case.TakeawayId);

    public string Size => L.Pick(Case.SizeEn, Case.SizeId);

    public bool HasPreview => Case.HasPreview;

    public bool HasLanes => _hasLanes;

    public string Code => CodeTab == 0 ? Case.CodeApl : Case.CodeTpl;

    public bool ShowsAplCode => CodeTab == 0;

    public bool ShowsTplCode => CodeTab == 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Code), nameof(ShowsAplCode), nameof(ShowsTplCode))]
    private int _codeTab;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RunLabel))]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    private bool _isRunning;

    [ObservableProperty]
    private string _status = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResults))]
    private IReadOnlyList<Measurement> _measurements = [];

    [ObservableProperty]
    private string _headlineValue = "—";

    [ObservableProperty]
    private string _headlineLabel = string.Empty;

    [ObservableProperty]
    private string _headlineDetail = string.Empty;

    [ObservableProperty]
    private bool _headlineIsWin = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VerificationText), nameof(IsVerified), nameof(IsNotVerified))]
    private bool? _verified;

    [ObservableProperty]
    private Bitmap? _preview;

    [ObservableProperty]
    private string _previewCaption = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLaneData))]
    private IReadOnlyList<LaneTrace> _lanes = [];

    public bool HasLaneData => Lanes.Count > 0;

    public bool HasResults => Measurements.Count > 0;

    public string RunLabel => IsRunning ? L["Running"] : L["Run"];

    public bool IsVerified => Verified == true;

    public bool IsNotVerified => Verified == false;

    public string VerificationText => Verified switch
    {
        true => "✓ " + L["Verified"],
        false => "✗ " + L["NotVerified"],
        _ => string.Empty,
    };

    /// <summary>Best APL.Net median against the Parallel.For median, for the overview's table.</summary>
    public double? Speedup { get; private set; }

    public Measurement? TplResult { get; private set; }

    public Measurement? AplResult { get; private set; }

    public void RefreshLanguage()
    {
        OnPropertyChanged(string.Empty);
        if (Measurements.Count > 0)
            UpdateHeadline();
        else
            HeadlineLabel = L["NotRun"];
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    public async Task RunAsync()
    {
        IsRunning = true;
        Verified = null;
        Measurements = [];
        Lanes = [];
        HeadlineValue = "…";
        HeadlineLabel = L["Running"];
        HeadlineDetail = string.Empty;

        try
        {
            await Task.Factory.StartNew(Measure, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            UpdateHeadline();
            if (Case.HasPreview)
            {
                Preview = Case.CreatePreview();
                PreviewCaption = Case is MonteCarloPiCase ? $"π ≈ {MonteCarloPiCase.Estimate():F5}" : string.Empty;
            }
        }
        catch (Exception ex)
        {
            Status = ex.GetBaseException().Message;
            HeadlineValue = "!";
        }
        finally
        {
            IsRunning = false;
        }
    }

    private bool CanRun() => !IsRunning;

    private void Measure()
    {
        Post(() => Status = L.Pick("Preparing data…", "Menyiapkan data…"));
        Case.Setup();

        IReadOnlyList<Variant> variants = Case.CreateVariants();
        var results = new List<Measurement>();
        for (int i = 0; i < variants.Count; i++)
        {
            Variant variant = variants[i];
            int index = i;
            Post(() => Status = $"{L.Pick("Timing", "Mengukur")} {variant.Name} ({index + 1}/{variants.Count})");

            // Let the previous bar finish growing first: the render loop is a thread competing for the
            // same cores, and a loop split statically across every core notices one busy core.
            if (i > 0)
                Thread.Sleep(600);
            results.Add(BenchmarkRunner.Measure(variant, s_budget, CancellationToken.None));
            Measurement[] snapshot = [.. results];
            Post(() => Measurements = snapshot);
        }

        Post(() => Status = L.Pick("Counting allocations…", "Menghitung alokasi…"));
        Thread.Sleep(800);
        for (int i = 0; i < variants.Count; i++)
            results[i] = results[i] with { BytesPerRun = BenchmarkRunner.MeasureAllocations(variants[i], Math.Clamp(results[i].Runs / 2, 3, 20)) };
        Measurement[] final = [.. results];
        Post(() => Measurements = final);

        Post(() => Status = L.Pick("Checking the output…", "Memeriksa hasil…"));
        bool ok = Case.Verify();
        Post(() => Verified = ok);

        IReadOnlyList<LaneRun> laneRuns = Case.CreateLaneRuns();
        if (laneRuns.Count > 0)
        {
            Post(() => Status = L.Pick("Tracing threads…", "Merekam thread…"));
            var recorder = new LaneRecorder();
            var traces = new List<LaneTrace>();
            foreach (LaneRun run in laneRuns)
            {
                run.Run(recorder);         // warm, and let the pool settle
                GC.Collect();
                recorder.Restart();
                run.Run(recorder);
                traces.Add(recorder.ToTrace(run.Name, run.Kind));
            }

            LaneTrace[] snapshot = [.. traces];
            Post(() => Lanes = snapshot);
        }

        Post(() => Status = string.Empty);
    }

    private void UpdateHeadline()
    {
        TplResult = Measurements.FirstOrDefault(m => m.Kind == VariantKind.Tpl);
        AplResult = Case.HeadlineVariant is { } name
            ? Measurements.FirstOrDefault(m => m.Name == name)
            : Measurements.Where(m => m.Kind == VariantKind.Apl).MinBy(m => m.MedianMs);

        if (TplResult is null || AplResult is null)
            return;

        double ratio = TplResult.MedianMs / AplResult.MedianMs;
        Speedup = ratio;
        HeadlineIsWin = ratio >= 1;
        HeadlineValue = ratio >= 1 ? $"{ratio:0.0}×" : $"{1 / ratio:0.0}×";
        HeadlineLabel = ratio >= 1 ? L["Faster"] : L["Slower"];
        HeadlineDetail = $"{AplResult.Name} · {Format(AplResult.MedianMs)}  vs  {TplResult.Name} · {Format(TplResult.MedianMs)}";
    }

    public static string Format(double ms) =>
        ms >= 100 ? $"{ms:0} ms" : ms >= 1 ? $"{ms:0.00} ms" : $"{ms * 1000:0} µs";

    private static void Post(Action action) => Dispatcher.UIThread.Post(action);
}
