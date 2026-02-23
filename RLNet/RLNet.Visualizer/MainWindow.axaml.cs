using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using RLNet.Core;
using System;
using System.Collections.Generic;

namespace RLNet.Visualizer
{
    public partial class MainWindow : Window
    {
        private IEnvironment _env;
        private QTableAgent _agent;
        private DispatcherTimer _timer;
        private bool _isTraining = false;
        private int _episode = 0;
        private int _step = 0;
        private double _totalReward = 0;
        private string _currentEnvType = "GridWorld";
        
        // State tracking
        private StepResult _lastResult;
        
        // UI Controls
        private Canvas? _gameCanvas;
        private TextBlock? _episodeText;
        private TextBlock? _stepText;
        private TextBlock? _epsilonText;
        private TextBlock? _rewardText;
        private Button? _toggleTrainButton;
        private Button? _resetButton;
        private Slider? _speedSlider;
        private ComboBox? _envSelector;

        public MainWindow()
        {
            InitializeComponent();
            
            _gameCanvas = this.FindControl<Canvas>("GameCanvas");
            _episodeText = this.FindControl<TextBlock>("EpisodeText");
            _stepText = this.FindControl<TextBlock>("StepText");
            _epsilonText = this.FindControl<TextBlock>("EpsilonText");
            _rewardText = this.FindControl<TextBlock>("RewardText");
            _toggleTrainButton = this.FindControl<Button>("ToggleTrainButton");
            _resetButton = this.FindControl<Button>("ResetButton");
            _speedSlider = this.FindControl<Slider>("SpeedSlider");
            _envSelector = this.FindControl<ComboBox>("EnvSelector");

            if (_toggleTrainButton != null) _toggleTrainButton.Click += ToggleTrainButton_Click;
            if (_resetButton != null) _resetButton.Click += ResetButton_Click;
            if (_speedSlider != null) _speedSlider.PropertyChanged += SpeedSlider_PropertyChanged;
            if (_envSelector != null) _envSelector.SelectionChanged += EnvSelector_SelectionChanged;
            
            if (_gameCanvas != null) _gameCanvas.SizeChanged += (s, e) => Draw();

            // Default
            SetupEnvironment("GridWorld");
            
            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(50);
            _timer.Tick += Timer_Tick;
            UpdateStats();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void EnvSelector_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_envSelector != null && _envSelector.SelectedItem is ComboBoxItem item)
            {
                string envName = item.Content?.ToString() ?? "GridWorld";
                SetupEnvironment(envName);
            }
        }

        private void SetupEnvironment(string envName)
        {
            _currentEnvType = envName;
            _episode = 0;
            _step = 0;
            _totalReward = 0;
            _isTraining = false;
            
            if (_toggleTrainButton != null) _toggleTrainButton.Content = "Start Training";
            if (_timer != null) _timer.Stop();

            switch (envName)
            {
                case "CartPole":
                    _env = new CartPoleEnvironment();
                    _agent = new QTableAgent(_env.GetActionSpaceSize(), StateDiscretizer.CartPoleEncoder, epsilon: 1.0);
                    break;
                case "LunarLander":
                    _env = new LunarLanderEnvironment();
                    _agent = new QTableAgent(_env.GetActionSpaceSize(), StateDiscretizer.LunarLanderEncoder, epsilon: 1.0);
                    break;
                case "Trading":
                    _env = new TradingEnvironment();
                    _agent = new QTableAgent(_env.GetActionSpaceSize(), StateDiscretizer.TradingEncoder, epsilon: 1.0);
                    break;
                case "GridWorld":
                default:
                    _env = new GridWorldEnvironment(5, 5);
                    _agent = new QTableAgent(_env.GetActionSpaceSize(), null, epsilon: 1.0);
                    break;
            }

            // Initialize State
            _lastResult = _env.Reset();
            
            UpdateStats();
            Draw();
        }

        private void SpeedSlider_PropertyChanged(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property.Name == "Value" && _timer != null && _speedSlider != null)
            {
                 // Higher value = Slower? Or Higher Value = Faster (Less interval)?
                 // Let's assume Slider is "Interval in ms".
                 // So 10ms (Fast) to 500ms (Slow).
                 _timer.Interval = TimeSpan.FromMilliseconds(_speedSlider.Value);
            }
        }

        private void ResetButton_Click(object? sender, RoutedEventArgs e)
        {
            SetupEnvironment(_currentEnvType);
        }

        private void ToggleTrainButton_Click(object? sender, RoutedEventArgs e)
        {
            _isTraining = !_isTraining;
            if (_toggleTrainButton == null) return;

            if (_isTraining)
            {
                _toggleTrainButton.Content = "Stop Training";
                _timer.Start();
            }
            else
            {
                _toggleTrainButton.Content = "Start Training";
                _timer.Stop();
            }
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            Step();
            Draw();
        }

        private void Step()
        {
            // Safety check
            if (_lastResult.State == null) 
            {
                _lastResult = _env.Reset();
            }

            // 1. Get Action based on Current State
            double[] currentState = _lastResult.State;
            int action = _agent.GetAction(currentState);
            
            // 2. Execute Action
            StepResult nextResult = _env.Step(action);
            
            // 3. Train Agent (using S, A, R, S', Done)
            _agent.Train(currentState, action, nextResult.Reward, nextResult.State, nextResult.Done);
            
            // 4. Update internal state
            _lastResult = nextResult;
            _step++;
            _totalReward += nextResult.Reward;

            // 5. Check Done
            if (nextResult.Done)
            {
                _episode++;
                // Decay Epsilon
                _agent.DecayEpsilon(0.995, 0.05);
                
                // Reset Environment for next episode
                _lastResult = _env.Reset();
                
                // Reset step counters for display clarity (or keep cumulative?)
                _step = 0;
                _totalReward = 0; 
            }
            
            UpdateStats();
        }

        private void UpdateStats()
        {
            if (_episodeText != null) _episodeText.Text = _episode.ToString();
            if (_stepText != null) _stepText.Text = _step.ToString();
            if (_epsilonText != null) _epsilonText.Text = _agent.Epsilon.ToString("F4");
            if (_rewardText != null) _rewardText.Text = _totalReward.ToString("F1");
        }

        private void Draw()
        {
            if (_gameCanvas == null) return;
            _gameCanvas.Children.Clear();

            double w = _gameCanvas.Bounds.Width;
            double h = _gameCanvas.Bounds.Height;
            
            // Initial render might have 0 size
            if (w <= 0 || h <= 0) return;

            if (_currentEnvType == "GridWorld") DrawGridWorld(w, h);
            else if (_currentEnvType == "CartPole") DrawCartPole(w, h);
            else if (_currentEnvType == "LunarLander") DrawLunarLander(w, h);
            else if (_currentEnvType == "Trading") DrawTrading(w, h);
        }

        private void DrawGridWorld(double width, double height)
        {
            var env = _env as GridWorldEnvironment;
            if (env == null) return;

            int rows = env.Height;
            int cols = env.Width;
            double cellW = width / cols;
            double cellH = height / rows;

            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < cols; x++)
                {
                    var rect = new Avalonia.Controls.Shapes.Rectangle
                    {
                        Width = cellW,
                        Height = cellH,
                        Stroke = Brushes.LightGray,
                        StrokeThickness = 1
                    };
                    Canvas.SetLeft(rect, x * cellW);
                    Canvas.SetTop(rect, y * cellH);

                    int cellType = env.GetCellType(x, y);
                    if (cellType == 2) rect.Fill = Brushes.LightGreen;
                    else if (cellType == 3) rect.Fill = Brushes.LightSalmon;
                    else rect.Fill = Brushes.White;

                    _gameCanvas.Children.Add(rect);

                    if (x == env.AgentX && y == env.AgentY)
                    {
                        var agentRect = new Avalonia.Controls.Shapes.Ellipse
                        {
                            Width = cellW * 0.8,
                            Height = cellH * 0.8,
                            Fill = Brushes.Blue
                        };
                        Canvas.SetLeft(agentRect, x * cellW + (cellW * 0.1));
                        Canvas.SetTop(agentRect, y * cellH + (cellH * 0.1));
                        _gameCanvas.Children.Add(agentRect);
                    }
                }
            }
        }

        private void DrawCartPole(double width, double height)
        {
            var env = _env as CartPoleEnvironment;
            if (env == null) return;

            // Mapping
            // CartX range approx -2.4 to 2.4. Let's map -3 to 3 to Width
            double worldWidth = 6.0;
            double scale = width / worldWidth; 
            double centerX = width / 2.0;
            double groundY = height * 0.7; 

            // Track
            var line = new Avalonia.Controls.Shapes.Line
            {
                StartPoint = new Point(0, groundY),
                EndPoint = new Point(width, groundY),
                Stroke = Brushes.Black,
                StrokeThickness = 2
            };
            _gameCanvas.Children.Add(line);

            // Cart
            double cartW = 50;
            double cartH = 30;
            double cartXPixels = centerX + (env.CartX * scale);

            var cart = new Avalonia.Controls.Shapes.Rectangle
            {
                Width = cartW,
                Height = cartH,
                Fill = Brushes.Black
            };
            Canvas.SetLeft(cart, cartXPixels - cartW / 2);
            Canvas.SetTop(cart, groundY - cartH / 2);
            _gameCanvas.Children.Add(cart);

            // Pole
            double poleLenPixels = 100;
            // PoleAngle 0 is vertical up? Physics says 0 is vertical.
            // Screen Y is down.
            // Tip X = CartX + Sin(theta) * L
            // Tip Y = CartY - Cos(theta) * L (Minus because up is negative Y)
            var pole = new Avalonia.Controls.Shapes.Line
            {
                StartPoint = new Point(cartXPixels, groundY),
                EndPoint = new Point(
                    cartXPixels + Math.Sin(env.PoleAngle) * poleLenPixels,
                    groundY - Math.Cos(env.PoleAngle) * poleLenPixels
                ),
                Stroke = Brushes.Brown,
                StrokeThickness = 6
            };
            _gameCanvas.Children.Add(pole);
        }

        private void DrawLunarLander(double width, double height)
        {
            var env = _env as LunarLanderEnvironment;
            if (env == null) return;

            // Mapping: X [-1, 1] -> [0, width] (Approx)
            // Mapping: Y [0, 1.5] -> [height, 0] (Inverted Y)
            
            // Assume World Width is 3.0 (-1.5 to 1.5)
            double scaleX = width / 3.0;
            double centerX = width / 2.0;
            double scaleY = height / 1.5;
            
            double screenX = centerX + (env.X * scaleX); 
            double screenY = height - (env.Y * scaleY);

            // Draw Lander Body
            var landerGroup = new Canvas();
            landerGroup.Width = 30;
            landerGroup.Height = 30;
            
            // Body
            var body = new Avalonia.Controls.Shapes.Rectangle
            {
                Width = 20,
                Height = 20,
                Fill = Brushes.Purple
            };
            Canvas.SetLeft(body, 5);
            Canvas.SetTop(body, 5);
            landerGroup.Children.Add(body);

            // Rotation
            landerGroup.RenderTransform = new RotateTransform(env.Angle * 180 / Math.PI);
            
            // Positioning (Center of lander at screenX, screenY)
            Canvas.SetLeft(landerGroup, screenX - 15);
            Canvas.SetTop(landerGroup, screenY - 15);
            _gameCanvas.Children.Add(landerGroup);

            // Draw Flames (Visual only, relative to lander pos but simplified for now)
            if (env.MainEngineFiring)
            {
                 var flame = new Avalonia.Controls.Shapes.Ellipse { Width=8, Height=15, Fill=Brushes.Orange };
                 Canvas.SetLeft(flame, screenX - 4);
                 Canvas.SetTop(flame, screenY + 15);
                 _gameCanvas.Children.Add(flame);
            }
            if (env.LeftEngineFiring)
            {
                 var flame = new Avalonia.Controls.Shapes.Ellipse { Width=15, Height=5, Fill=Brushes.Orange };
                 Canvas.SetLeft(flame, screenX + 15);
                 Canvas.SetTop(flame, screenY - 2);
                 _gameCanvas.Children.Add(flame);
            }
            if (env.RightEngineFiring)
            {
                 var flame = new Avalonia.Controls.Shapes.Ellipse { Width=15, Height=5, Fill=Brushes.Orange };
                 Canvas.SetLeft(flame, screenX - 30);
                 Canvas.SetTop(flame, screenY - 2);
                 _gameCanvas.Children.Add(flame);
            }
            
            // Draw Ground
            var ground = new Avalonia.Controls.Shapes.Rectangle { Width=width, Height=20, Fill=Brushes.Gray };
            Canvas.SetLeft(ground, 0);
            Canvas.SetTop(ground, height - 20);
            _gameCanvas.Children.Add(ground);
            
            // Landing Pad
            var pad = new Avalonia.Controls.Shapes.Rectangle { Width=width * 0.2, Height=5, Fill=Brushes.Yellow };
            Canvas.SetLeft(pad, centerX - (width*0.1));
            Canvas.SetTop(pad, height - 25);
            _gameCanvas.Children.Add(pad);
        }

        private void DrawTrading(double width, double height)
        {
            var env = _env as TradingEnvironment;
            if (env == null) return;

            var prices = env.GetPriceHistory();
            if (prices.Count < 2) return;

            // Scaling
            double maxPrice = 200; 
            double minPrice = 0;
            foreach(var p in prices) 
            {
                if (p > maxPrice) maxPrice = p;
                // if (p < minPrice) minPrice = p; // Keep 0 baseline
            }
            maxPrice *= 1.1; // Padding

            double stepX = width / prices.Count;
            
            // Draw Price Line
            Avalonia.Controls.Shapes.Polyline line = new Avalonia.Controls.Shapes.Polyline();
            line.Stroke = Brushes.Blue;
            line.StrokeThickness = 2;
            
            var points = new List<Point>();
            for(int i=0; i<prices.Count; i++)
            {
                double x = i * stepX;
                double y = height - ((prices[i] / maxPrice) * height);
                points.Add(new Point(x, y));
            }
            line.Points = points;
            _gameCanvas.Children.Add(line);
            
            // Current Position Marker
            double curX = env.CurrentStep * stepX;
            double curY = height - ((prices[Math.Min(env.CurrentStep, prices.Count-1)] / maxPrice) * height);
            
            var markerLine = new Avalonia.Controls.Shapes.Line { StartPoint=new Point(curX, 0), EndPoint=new Point(curX, height), Stroke=Brushes.Red, StrokeThickness=1, StrokeDashArray=new Avalonia.Collections.AvaloniaList<double>{4, 2} };
            _gameCanvas.Children.Add(markerLine);
            
            var dot = new Avalonia.Controls.Shapes.Ellipse { Width=10, Height=10, Fill=Brushes.Red };
            Canvas.SetLeft(dot, curX - 5);
            Canvas.SetTop(dot, curY - 5);
            _gameCanvas.Children.Add(dot);
            
            // Info Text Overlay
            var infoPanel = new StackPanel { Margin = new Thickness(10), Background = new SolidColorBrush(Colors.White, 0.7) };
            infoPanel.Children.Add(new TextBlock { Text = $"Shares: {env.Shares}", FontWeight=FontWeight.Bold });
            infoPanel.Children.Add(new TextBlock { Text = $"Balance: ${env.Balance:F2}" });
            infoPanel.Children.Add(new TextBlock { Text = $"Net Worth: ${env.NetWorth:F2}" });
            
            Canvas.SetLeft(infoPanel, 10);
            Canvas.SetTop(infoPanel, 10);
            _gameCanvas.Children.Add(infoPanel);
        }
    }
}