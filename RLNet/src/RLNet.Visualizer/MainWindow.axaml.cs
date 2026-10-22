// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using RLNet.Agents;
using RLNet.Environments;
using RLNet.Environments.MultiAgent;
using RLNet.Spaces;
using RLNet.Training;
using RLNet.Visualizer.Controls;

namespace RLNet.Visualizer;

/// <summary>
/// The console: environment viewport, recorder stack, and the controls that drive them.
/// </summary>
/// <remarks>
/// Code-behind with no view model. The window has one piece of state — the current
/// <see cref="TrainingSession"/> — and everything on screen is read from it every frame, so a
/// binding layer would add indirection without removing any work. The place that would benefit
/// from MVVM is a form; this is an instrument.
/// </remarks>
public partial class MainWindow : Window
{
    /// <summary>
    /// Speed positions, in steps attempted per frame.
    /// </summary>
    /// <remarks>
    /// Geometric rather than linear. The interesting range spans four orders of magnitude — one
    /// step per frame to watch a single decision, tens of thousands to get through early training
    /// — and a linear slider spends nearly all its travel in territory that looks identical.
    /// </remarks>
    private static readonly int[] SpeedSteps =
        [1, 2, 4, 8, 16, 32, 64, 128, 256, 1_024, 4_096, 16_384, 65_536];

    private readonly StartupOptions _startup;
    private TrainingSession? _session;
    private readonly DispatcherTimer _timer;
    private readonly List<Border> _lamps = [];

    private WorldView _world = null!;
    private RecorderStrip _returnStrip = null!;
    private RecorderStrip _lossStrip = null!;
    private RecorderStrip _explorationStrip = null!;
    private ComboBox _environmentPicker = null!;
    private ComboBox _algorithmPicker = null!;
    private Button _runButton = null!;
    private Button _resetButton = null!;
    private Slider _speedSlider = null!;
    private ItemsControl _actionLamps = null!;
    private Button _saveButton = null!;
    private Button _loadButton = null!;
    private ToggleButton _watchButton = null!;
    private ToggleButton _logButton = null!;
    private TextBlock _statusLine = null!;
    private TextBlock _averageLegend = null!;
    private TextBlock _currentLegend = null!;

    private TextBlock _environmentLabel = null!;
    private TextBlock _algorithmLabel = null!;
    private TextBlock _episodeReadout = null!;
    private TextBlock _stepsReadout = null!;
    private TextBlock _rateReadout = null!;
    private TextBlock _bestReadout = null!;
    private TextBlock _averageReadout = null!;
    private TextBlock _currentReadout = null!;
    private TextBlock _speedReadout = null!;

    private static readonly IBrush LampOff = new SolidColorBrush(Color.Parse("#1D2A39"));
    private static readonly IBrush LampOn = new SolidColorBrush(Color.Parse("#F0A93B"));
    private static readonly IBrush LampRule = new SolidColorBrush(Color.Parse("#253243"));
    private static readonly IBrush LampInk = new SolidColorBrush(Color.Parse("#7E92A8"));
    private static readonly IBrush LampInkLit = new SolidColorBrush(Color.Parse("#14202C"));
    private static readonly IBrush StatusInk = new SolidColorBrush(Color.Parse("#7E92A8"));
    private static readonly IBrush StatusError = new SolidColorBrush(Color.Parse("#E0674F"));

    private static readonly FilePickerFileType CheckpointFiles = new("RLNet checkpoint") { Patterns = ["*.rlnet"] };

    /// <summary>Where checkpoints are offered and metrics logs go unless told otherwise.</summary>
    private static readonly string WorkFolder =
        Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments), "RLNet");

    /// <summary>Set while the session holds an agent built with Catalog's settings, after a load.</summary>
    private bool _catalogSettings;

    public MainWindow() : this(new StartupOptions()) { }

    public MainWindow(StartupOptions startup)
    {
        _startup = startup;

        InitializeComponent();
        ResolveControls();

        PopulateEnvironments();

        _runButton.Click += OnRunClicked;
        _resetButton.Click += OnResetClicked;
        _environmentPicker.SelectionChanged += OnEnvironmentChanged;
        _algorithmPicker.SelectionChanged += OnAlgorithmChanged;
        _speedSlider.PropertyChanged += OnSpeedChanged;
        _saveButton.Click += OnSaveClicked;
        _loadButton.Click += OnLoadClicked;
        _watchButton.IsCheckedChanged += OnWatchChanged;
        _logButton.IsCheckedChanged += OnLogChanged;
        Closed += (_, _) => _session?.Dispose();

        // 60 Hz. The session's own frame budget keeps a slice inside this, so the window stays
        // responsive whatever the step rate is set to.
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += OnTick;
        _timer.Start();

        ApplyStartup();
    }

    /// <summary>Opens on whatever the command line asked for, and starts if it said so.</summary>
    private void ApplyStartup()
    {
        int index = IndexOfEnvironment(_startup.Environment);
        if (index >= 0 && index != _environmentPicker.SelectedIndex)
        {
            // Assigning this raises SelectionChanged, which repopulates the algorithms and builds
            // the session, so nothing further is needed for the environment itself.
            _environmentPicker.SelectedIndex = index;
        }
        else
        {
            PopulateAlgorithms();
        }

        if (_startup.Algorithm is { } algorithm)
        {
            int algorithmIndex = Array.IndexOf(SelectedEntry.SupportedAlgorithms, algorithm);
            if (algorithmIndex >= 0) _algorithmPicker.SelectedIndex = algorithmIndex;
        }

        if (_startup.Speed is { } speed)
        {
            // The slider is a position into a geometric table, so the requested step count is
            // matched to the nearest position rather than used directly.
            int best = 0;
            for (int i = 1; i < SpeedSteps.Length; i++)
                if (Math.Abs(SpeedSteps[i] - speed) < Math.Abs(SpeedSteps[best] - speed)) best = i;

            _speedSlider.Value = best;
        }

        if (_session is null) BuildSession();

        _logButton.IsChecked = _startup.Log;
        if (_startup.CheckpointPath is { } path) LoadCheckpoint(path);
        _watchButton.IsChecked = _startup.Watch;

        if (_startup.AutoStart) StartSession();
    }

    private static int IndexOfEnvironment(string name)
    {
        for (int i = 0; i < Catalog.Environments.Count; i++)
            if (Catalog.Environments[i].Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void ResolveControls()
    {
        _world = this.GetControl<WorldView>("World");
        _returnStrip = this.GetControl<RecorderStrip>("ReturnStrip");
        _lossStrip = this.GetControl<RecorderStrip>("LossStrip");
        _explorationStrip = this.GetControl<RecorderStrip>("ExplorationStrip");
        _environmentPicker = this.GetControl<ComboBox>("EnvironmentPicker");
        _algorithmPicker = this.GetControl<ComboBox>("AlgorithmPicker");
        _runButton = this.GetControl<Button>("RunButton");
        _resetButton = this.GetControl<Button>("ResetButton");
        _speedSlider = this.GetControl<Slider>("SpeedSlider");
        _actionLamps = this.GetControl<ItemsControl>("ActionLamps");
        _saveButton = this.GetControl<Button>("SaveButton");
        _loadButton = this.GetControl<Button>("LoadButton");
        _watchButton = this.GetControl<ToggleButton>("WatchButton");
        _logButton = this.GetControl<ToggleButton>("LogButton");
        _statusLine = this.GetControl<TextBlock>("StatusLine");
        _averageLegend = this.GetControl<TextBlock>("AverageLegend");
        _currentLegend = this.GetControl<TextBlock>("CurrentLegend");

        _environmentLabel = this.GetControl<TextBlock>("EnvironmentLabel");
        _algorithmLabel = this.GetControl<TextBlock>("AlgorithmLabel");
        _episodeReadout = this.GetControl<TextBlock>("EpisodeReadout");
        _stepsReadout = this.GetControl<TextBlock>("StepsReadout");
        _rateReadout = this.GetControl<TextBlock>("RateReadout");
        _bestReadout = this.GetControl<TextBlock>("BestReadout");
        _averageReadout = this.GetControl<TextBlock>("AverageReadout");
        _currentReadout = this.GetControl<TextBlock>("CurrentReadout");
        _speedReadout = this.GetControl<TextBlock>("SpeedReadout");
    }

    private void PopulateEnvironments()
    {
        // Grouped by category, so the picker reads as "classic / control / robotics / finance"
        // rather than as nine unrelated names.
        _environmentPicker.ItemsSource = Catalog.Environments
            .Select(e => $"{e.Category}  ·  {e.Name}")
            .ToList();

        _environmentPicker.SelectedIndex = 1; // CartPole: the one whose learning curve reads clearest
    }

    private EnvironmentEntry SelectedEntry =>
        Catalog.Environments[Math.Max(0, _environmentPicker.SelectedIndex)];

    private void OnEnvironmentChanged(object? sender, SelectionChangedEventArgs e)
    {
        PopulateAlgorithms();
        BuildSession();
    }

    private void OnAlgorithmChanged(object? sender, SelectionChangedEventArgs e) => BuildSession();

    private void PopulateAlgorithms()
    {
        var supported = SelectedEntry.SupportedAlgorithms;
        int previous = _algorithmPicker.SelectedIndex;

        _algorithmPicker.SelectionChanged -= OnAlgorithmChanged;
        _algorithmPicker.ItemsSource = supported.Select(DisplayName).ToList();
        _algorithmPicker.SelectedIndex = previous >= 0 && previous < supported.Length ? previous : 0;
        _algorithmPicker.SelectionChanged += OnAlgorithmChanged;
    }

    private static string DisplayName(Algorithm algorithm) => algorithm switch
    {
        Algorithm.QLearning => "Q-Learning (tabular)",
        Algorithm.Dqn => "DQN (double + dueling)",
        Algorithm.A2C => "A2C",
        Algorithm.Ppo => "PPO",
        Algorithm.Sac => "SAC",
        Algorithm.Td3 => "TD3",
        _ => algorithm.ToString(),
    };

    private Algorithm SelectedAlgorithm
    {
        get
        {
            var supported = SelectedEntry.SupportedAlgorithms;
            return supported[Math.Clamp(_algorithmPicker.SelectedIndex, 0, supported.Length - 1)];
        }
    }

    private void BuildSession()
    {
        if (_algorithmPicker.ItemsSource is null) PopulateAlgorithms();

        // Every session is seeded identically, so switching algorithm and switching back gives
        // the same run again. Comparing two algorithms on different random worlds compares the
        // worlds as much as the algorithms.
        InstallSession(CreateSession(SelectedEntry, SelectedAlgorithm, catalogSettings: false), catalogSettings: false);
        ShowStatus(string.Empty);
    }

    /// <summary>Makes <paramref name="session"/> the current one, carrying the watch switch over.</summary>
    private void InstallSession(TrainingSession session, bool catalogSettings)
    {
        _session?.Dispose();
        _session = session;
        _catalogSettings = catalogSettings;

        _session.SetWatching(_watchButton.IsChecked == true);

        _world.World = _session.Environment;
        _returnStrip.Trace = _session.ReturnTrace;
        _lossStrip.Trace = _session.LossTrace;
        _explorationStrip.Trace = _session.ExplorationTrace;

        _environmentLabel.Text = _session.EnvironmentName;
        _algorithmLabel.Text = _session.AlgorithmName;
        UpdateRunLabel();

        ApplySpeed();
        BuildActionLamps();
        Refresh();
    }

    /// <summary>
    /// Builds a session for an environment and algorithm, with the console's light presets or —
    /// to load a checkpoint trained in code — the library's own <see cref="Catalog"/> settings.
    /// </summary>
    private static TrainingSession CreateSession(EnvironmentEntry entry, Algorithm algorithm, bool catalogSettings)
    {
        string name = DisplayName(algorithm) + (catalogSettings ? "  ·  Catalog" : string.Empty);
        Func<object> copy = entry.Create;

        switch (entry.Kind)
        {
            case EnvironmentKind.Discrete:
            {
                var environment = (IDiscreteEnvironment)entry.Create();
                var agent = CreateDiscreteAgent(algorithm, environment.ObservationSpace, environment.ActionSpace, catalogSettings);
                return new TrainingSession(environment, agent, name, copy);
            }
            case EnvironmentKind.Continuous:
            {
                var environment = (IContinuousEnvironment)entry.Create();
                var agent = catalogSettings
                    ? Catalog.CreateAgent(algorithm, environment.ObservationSpace, environment.ActionSpace, TrainingSession.Seed)
                    : DemoPresets.CreateContinuous(algorithm, environment.ObservationSpace, environment.ActionSpace, TrainingSession.Seed);
                return new TrainingSession(environment, agent, name, copy);
            }
            case EnvironmentKind.MultiAgent:
            {
                var environment = (IMultiAgentEnvironment)entry.Create();

                // Shared parameters: the predators are interchangeable, so pooling their experience
                // makes the console show progress in a minute rather than ten.
                var agent = CreateDiscreteAgent(algorithm, environment.ObservationSpace, environment.ActionSpace, catalogSettings);
                var learners = IndependentLearners.ShareParameters(
                    agent, environment.AgentCount, environment.ObservationSpace.FlatSize);
                return new TrainingSession(environment, learners, name, copy);
            }
            default:
                throw new InvalidOperationException($"Unhandled environment kind {entry.Kind}.");
        }
    }

    private static IDiscreteAgent CreateDiscreteAgent(
        Algorithm algorithm, Space observationSpace, DiscreteSpace actionSpace, bool catalogSettings) =>
        catalogSettings
            ? Catalog.CreateAgent(algorithm, observationSpace, actionSpace, TrainingSession.Seed)
            : DemoPresets.CreateDiscrete(algorithm, observationSpace, actionSpace, TrainingSession.Seed);

    private async void OnSaveClicked(object? sender, RoutedEventArgs e)
    {
        if (_session is null) return;

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save checkpoint",
            SuggestedFileName = $"{_session.EnvironmentName}-{SelectedAlgorithm}".ToLowerInvariant() + ".rlnet",
            DefaultExtension = "rlnet",
            FileTypeChoices = [CheckpointFiles],
            SuggestedStartLocation = await WorkFolderAsync(),
        });

        if (file?.TryGetLocalPath() is not { } path) return;

        try
        {
            _session.Save(path);
            ShowStatus($"Saved {_session.AlgorithmName} at {_session.TotalSteps:N0} steps to {path}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError($"Could not save: {ex.Message}");
        }
    }

    private async void OnLoadClicked(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = $"Load checkpoint into {SelectedEntry.Name} / {DisplayName(SelectedAlgorithm)}",
            AllowMultiple = false,
            FileTypeFilter = [CheckpointFiles, FilePickerFileTypes.All],
            SuggestedStartLocation = await WorkFolderAsync(),
        });

        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path) LoadCheckpoint(path);
    }

    private async Task<IStorageFolder?> WorkFolderAsync()
    {
        try
        {
            Directory.CreateDirectory(WorkFolder);
            return await StorageProvider.TryGetFolderFromPathAsync(WorkFolder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null; // the picker opens wherever the platform likes
        }
    }

    /// <summary>
    /// Loads a checkpoint into the current environment and algorithm, keeping the clock running
    /// if it was.
    /// </summary>
    /// <remarks>
    /// The console's agents are narrower than <see cref="Catalog"/>'s, so a checkpoint trained in
    /// code with the library defaults has a different layout and the console's agent refuses it.
    /// Rather than make anyone retrain at console sizes to watch a policy, a refused load is
    /// retried against a Catalog-configured agent, and the session switches to that agent if it
    /// fits. Only when both refuse is the first refusal — the one about the console's own agent —
    /// reported.
    /// </remarks>
    private void LoadCheckpoint(string path)
    {
        if (_session is null) return;
        bool running = _session.IsRunning;

        try
        {
            if (_catalogSettings) BuildSession(); // back to console sizes, which are tried first

            try
            {
                _session.Load(path);
            }
            catch (InvalidDataException consoleRefusal)
            {
                var catalogSession = CreateSession(SelectedEntry, SelectedAlgorithm, catalogSettings: true);
                try
                {
                    catalogSession.Load(path);
                }
                catch (InvalidDataException)
                {
                    catalogSession.Dispose();
                    ShowError($"Could not load {Path.GetFileName(path)}: {consoleRefusal.Message}");
                    return;
                }

                InstallSession(catalogSession, catalogSettings: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError($"Could not load {Path.GetFileName(path)}: {ex.Message}");
            return;
        }

        _world.World = _session.Environment;
        if (running) StartSession();
        Refresh();
        ShowStatus($"Loaded {Path.GetFileName(path)} at {_session.TotalSteps:N0} steps" +
                   (_catalogSettings ? " (Catalog settings)" : string.Empty));
    }

    private void OnWatchChanged(object? sender, RoutedEventArgs e)
    {
        if (_session is null) return;

        _session.SetWatching(_watchButton.IsChecked == true);
        _world.World = _session.Environment;
        UpdateRunLabel();
        Refresh();
    }

    private void OnLogChanged(object? sender, RoutedEventArgs e)
    {
        if (_logButton.IsChecked != true)
        {
            if (_session?.Log is not null)
            {
                _session.Log = null;
                ShowStatus("Metrics log closed.");
            }
            return;
        }

        // Opened now if the clock is running, otherwise when it starts — so flicking through
        // environments with the switch on does not leave a trail of empty files.
        if (_session?.IsRunning == true) EnsureLog();
        else ShowStatus("Metrics log on: a CSV is opened when training starts.");
    }

    /// <summary>Opens a fresh metrics log for the current run if logging is on and none is open.</summary>
    private void EnsureLog()
    {
        if (_session is null || _session.Log is not null || _logButton.IsChecked != true) return;

        string folder = _startup.LogDirectory ?? Path.Combine(WorkFolder, "runs");
        string file = Path.Combine(
            folder,
            $"{_session.EnvironmentName}-{SelectedAlgorithm}-{DateTime.Now:yyyyMMdd-HHmmss}.csv".ToLowerInvariant());

        try
        {
            Directory.CreateDirectory(folder);
            _session.Log = MetricsLog.Create(file);
            ShowStatus($"Logging training episodes to {file}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logButton.IsChecked = false;
            ShowError($"Could not open a metrics log: {ex.Message}");
        }
    }

    private void StartSession()
    {
        if (_session is null) return;

        _session.Start();
        EnsureLog();
        UpdateRunLabel();
    }

    private void UpdateRunLabel()
    {
        if (_session is null) return;

        string verb = _session.IsRunning ? "STOP" : "START";
        _runButton.Content = _session.IsWatching ? $"{verb} WATCHING" : $"{verb} TRAINING";
    }

    private void ShowStatus(string text)
    {
        _statusLine.Foreground = StatusInk;
        _statusLine.Text = text;
        ToolTip.SetTip(_statusLine, string.IsNullOrEmpty(text) ? null : text); // the line trims long paths
    }

    private void ShowError(string text)
    {
        _statusLine.Foreground = StatusError;
        _statusLine.Text = text;
        ToolTip.SetTip(_statusLine, text);
    }

    private void BuildActionLamps()
    {
        _lamps.Clear();

        if (_session is null || _session.ActionCount == 0)
        {
            // A continuous policy has no discrete action to light up. The viewport shows the
            // torque arc instead, so the lamp row says so rather than sitting empty.
            _actionLamps.ItemsSource = new List<Control>
            {
                new TextBlock
                {
                    Text = "continuous — see the torque arc in the viewport",
                    Foreground = LampInk,
                    FontSize = 11,
                },
            };
            return;
        }

        var lamps = new List<Control>();
        foreach (string label in _session.ActionLabels)
        {
            var text = new TextBlock
            {
                Text = label.ToUpperInvariant(),
                FontSize = 10,
                FontWeight = FontWeight.SemiBold,
                Foreground = LampInk,
            };

            var lamp = new Border
            {
                Background = LampOff,
                BorderBrush = LampRule,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2),
                Padding = new Thickness(9, 5),
                Child = text,
            };

            _lamps.Add(lamp);
            lamps.Add(lamp);
        }

        _actionLamps.ItemsSource = lamps;
    }

    private void OnRunClicked(object? sender, RoutedEventArgs e)
    {
        if (_session is null) return;

        if (_session.IsRunning)
        {
            _session.Stop();
            UpdateRunLabel();
        }
        else
        {
            StartSession();
        }
    }

    private void OnResetClicked(object? sender, RoutedEventArgs e) => BuildSession();

    private void OnSpeedChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == RangeBase.ValueProperty) ApplySpeed();
    }

    private void ApplySpeed()
    {
        int index = Math.Clamp((int)_speedSlider.Value, 0, SpeedSteps.Length - 1);
        int steps = SpeedSteps[index];

        if (_session is not null) _session.StepsPerFrame = steps;
        _speedReadout.Text = steps == 1 ? "1 step / frame" : $"{steps:N0} steps / frame";
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _session?.Advance();
        Refresh();
    }

    private void Refresh()
    {
        if (_session is null) return;

        _episodeReadout.Text = _session.Episode.ToString("N0");
        _stepsReadout.Text = _session.TotalSteps.ToString("N0");
        _rateReadout.Text = _session.StepsPerSecond.ToString("N0");

        _bestReadout.Text = float.IsNaN(_session.BestReturn) ? "—" : _session.BestReturn.ToString("F1");
        _currentReadout.Text = $"{_session.EpisodeReturn:F1} / {_session.EpisodeSteps}s";

        // While watching, the summary is about the deterministic policy, not the training curve;
        // the legends say so, because the two numbers are not comparable.
        if (_session.IsWatching)
        {
            _averageLegend.Text = $"WATCHED  ·  {_session.WatchEpisodes:N0} EP";
            _currentLegend.Text = "WATCHING";
            _averageReadout.Text = float.IsNaN(_session.WatchAverage) ? "—" : _session.WatchAverage.ToString("F1");
        }
        else
        {
            _averageLegend.Text = "LAST 20 EPISODES";
            _currentLegend.Text = "THIS EPISODE";
            _averageReadout.Text = float.IsNaN(_session.RecentAverage) ? "—" : _session.RecentAverage.ToString("F1");
        }

        _explorationStrip.Legend = _session.ExplorationLabel;

        UpdateLamps();

        // The environment mutates in place, so the viewport has to be told its content changed —
        // there is no property assignment for Avalonia to notice.
        _world.InvalidateVisual();
        _returnStrip.InvalidateVisual();
        _lossStrip.InvalidateVisual();
        _explorationStrip.InvalidateVisual();
    }

    private void UpdateLamps()
    {
        if (_session is null || _lamps.Count == 0) return;

        int active = _session.LastAction;
        for (int i = 0; i < _lamps.Count; i++)
        {
            bool lit = i == active;
            _lamps[i].Background = lit ? LampOn : LampOff;
            if (_lamps[i].Child is TextBlock text) text.Foreground = lit ? LampInkLit : LampInk;
        }
    }
}
