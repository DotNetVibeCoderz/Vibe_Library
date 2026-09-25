// APL.Net - Another Parallel Library for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using AplNet.Gallery.Cases;
using AplNet.Gallery.Infrastructure;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AplNet.Gallery.ViewModels;

public sealed record CategoryGroup(CaseCategory Category, IReadOnlyList<CaseViewModel> Cases)
{
    public string Glyph => Category.Glyph;

    public string Name => Loc.Instance.Pick(Category.NameEn, Category.NameId);

    public string Meaning => Loc.Instance.Pick(Category.GlyphMeaningEn, Category.GlyphMeaningId);
}

public sealed partial class MainViewModel : ObservableObject
{
    public MainViewModel()
    {
        Cases = CaseCatalog.All.Select(c => new CaseViewModel(c) { Navigate = Select }).ToList();
        Overview = new OverviewViewModel(this);
        BuildGroups();
        _current = Overview;
    }

    public IReadOnlyList<CaseViewModel> Cases { get; }

    public OverviewViewModel Overview { get; }

    [ObservableProperty]
    private IReadOnlyList<CategoryGroup> _groups = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOverviewSelected))]
    private ObservableObject _current;

    public bool IsOverviewSelected => Current is OverviewViewModel;

    [RelayCommand]
    public void ShowOverview()
    {
        Overview.RebuildSummary();
        Navigate(Overview);
    }

    [RelayCommand]
    public void Select(CaseViewModel target) => Navigate(target);

    [RelayCommand]
    public void ToggleLanguage()
    {
        Loc.Instance.Indonesian = !Loc.Instance.Indonesian;
        foreach (CaseViewModel c in Cases)
            c.RefreshLanguage();
        Overview.RefreshLanguage();
        BuildGroups();
    }

    public CaseViewModel? Find(string id) => Cases.FirstOrDefault(c => c.Case.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    private void Navigate(ObservableObject target)
    {
        // Leaving a case frees its buffers (some are tens of megabytes); results and previews stay.
        if (Current is CaseViewModel previous && previous != target && !previous.IsRunning && !Overview.IsRunning)
            previous.Case.Release();
        Current = target;
        foreach (CaseViewModel c in Cases)
            c.IsSelected = c == target;
    }

    private void BuildGroups() =>
        Groups = CaseCatalog.Categories
            .Select(category => new CategoryGroup(category, Cases.Where(c => c.Case.Category == category).ToList()))
            .ToList();
}

public sealed partial class OverviewViewModel(MainViewModel main) : ObservableObject
{
    private static Loc L => Loc.Instance;

    public string Cpu => MachineInfo.Cpu;

    public string ThreadsText => $"{MachineInfo.Threads} {L["Threads"]}";

    public string Simd => MachineInfo.Simd;

    public string Runtime => MachineInfo.Runtime;

    public string Os => MachineInfo.Os;

    public int Threads => MachineInfo.Threads;

    public IReadOnlyList<int> ThreadSlots { get; } = Enumerable.Range(0, MachineInfo.Threads).ToList();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunAllCommand))]
    private bool _isRunning;

    [ObservableProperty]
    private string _status = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<SummaryRow> _summary = [];

    public bool HasSummary => Summary.Count > 0;

    partial void OnSummaryChanged(IReadOnlyList<SummaryRow> value) => OnPropertyChanged(nameof(HasSummary));

    public void RefreshLanguage()
    {
        OnPropertyChanged(string.Empty);
        RebuildSummary();
    }

    [RelayCommand(CanExecute = nameof(CanRunAll))]
    public async Task RunAllAsync()
    {
        IsRunning = true;
        try
        {
            int index = 0;
            foreach (CaseViewModel c in main.Cases)
            {
                Status = $"{++index}/{main.Cases.Count} · {c.Title}";
                await c.RunAsync();
                if (main.Current != c)
                    c.Case.Release();
                RebuildSummary();
            }
        }
        finally
        {
            Status = string.Empty;
            IsRunning = false;
        }
    }

    private bool CanRunAll() => !IsRunning;

    public void RebuildSummary() =>
        Summary = main.Cases
            .Where(c => c.Speedup is not null)
            .Select(c => new SummaryRow(c.Glyph, c.Title, c.TplResult!, c.AplResult!, c.Speedup!.Value))
            .ToList();
}

public sealed record SummaryRow(string Glyph, string Title, Measurement Tpl, Measurement Apl, double Speedup)
{
    public string TplText => CaseViewModel.Format(Tpl.MedianMs);

    public string AplText => CaseViewModel.Format(Apl.MedianMs);

    public string AplName => Apl.Name;

    public string SpeedupText => Speedup >= 1 ? $"{Speedup:0.0}× ▲" : $"{1 / Speedup:0.0}× ▼";

    public bool IsWin => Speedup >= 1;

    public bool IsLoss => Speedup < 1;
}
