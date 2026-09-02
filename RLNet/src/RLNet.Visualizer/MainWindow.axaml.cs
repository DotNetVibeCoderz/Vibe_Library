// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using RLNet.Agents;
using RLNet.Environments;
using RLNet.Environments.MultiAgent;
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

        if (_startup.AutoStart)
        {
            _session!.Start();
            _runButton.Content = "STOP TRAINING";
        }
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

    private void BuildSession()
    {
        if (_algorithmPicker.ItemsSource is null) PopulateAlgorithms();

        var entry = SelectedEntry;
        var algorithm = entry.SupportedAlgorithms[
            Math.Clamp(_algorithmPicker.SelectedIndex, 0, entry.SupportedAlgorithms.Length - 1)];

        // Every session is seeded identically, so switching algorithm and switching back gives
        // the same run again. Comparing two algorithms on different random worlds compares the
        // worlds as much as the algorithms.
        _session = entry.Kind switch
        {
            EnvironmentKind.Discrete => BuildDiscrete(entry, algorithm),
            EnvironmentKind.Continuous => BuildContinuous(entry, algorithm),
            EnvironmentKind.MultiAgent => BuildMultiAgent(entry, algorithm),
            _ => throw new InvalidOperationException($"Unhandled environment kind {entry.Kind}."),
        };

        _world.World = _session.Environment;
        _returnStrip.Trace = _session.ReturnTrace;
        _lossStrip.Trace = _session.LossTrace;
        _explorationStrip.Trace = _session.ExplorationTrace;

        _environmentLabel.Text = _session.EnvironmentName;
        _algorithmLabel.Text = _session.AlgorithmName;
        _runButton.Content = "START TRAINING";

        ApplySpeed();
        BuildActionLamps();
        Refresh();
    }

    private static TrainingSession BuildDiscrete(EnvironmentEntry entry, Algorithm algorithm)
    {
        var environment = (IDiscreteEnvironment)entry.Create();
        var agent = DemoPresets.CreateDiscrete(
            algorithm, environment.ObservationSpace, environment.ActionSpace, TrainingSession.Seed);

        return new TrainingSession(environment, agent, DisplayName(algorithm));
    }

    private static TrainingSession BuildContinuous(EnvironmentEntry entry, Algorithm algorithm)
    {
        var environment = (IContinuousEnvironment)entry.Create();
        var agent = DemoPresets.CreateContinuous(
            algorithm, environment.ObservationSpace, environment.ActionSpace, TrainingSession.Seed);

        return new TrainingSession(environment, agent, DisplayName(algorithm));
    }

    private static TrainingSession BuildMultiAgent(EnvironmentEntry entry, Algorithm algorithm)
    {
        var environment = (IMultiAgentEnvironment)entry.Create();

        // Shared parameters: the predators are interchangeable, so pooling their experience makes
        // the console show progress in a minute rather than ten.
        var agent = DemoPresets.CreateDiscrete(
            algorithm, environment.ObservationSpace, environment.ActionSpace, TrainingSession.Seed);

        var learners = IndependentLearners.ShareParameters(
            agent, environment.AgentCount, environment.ObservationSpace.FlatSize);

        return new TrainingSession(environment, learners, DisplayName(algorithm));
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
            _runButton.Content = "START TRAINING";
        }
        else
        {
            _session.Start();
            _runButton.Content = "STOP TRAINING";
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
        _averageReadout.Text = float.IsNaN(_session.RecentAverage) ? "—" : _session.RecentAverage.ToString("F1");
        _currentReadout.Text = $"{_session.EpisodeReturn:F1} / {_session.EpisodeSteps}s";

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
