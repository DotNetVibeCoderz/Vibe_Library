using Avalonia.Controls;
using Avalonia.Interactivity;
using D3Net.Charts;
using System;

namespace D3Net
{
    /// <summary>
    /// Main Window yang menampilkan berbagai jenis visualisasi data
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            
            // Wire up button click events
            btnBarChart.Click += ShowBarChart;
            btnLineChart.Click += ShowLineChart;
            btnPieChart.Click += ShowPieChart;
            btnAreaChart.Click += ShowAreaChart;
            btnScatterChart.Click += ShowScatterChart;
            btnBubbleChart.Click += ShowBubbleChart;
            btnRadarChart.Click += ShowRadarChart;
            btnHeatmap.Click += ShowHeatmap;
            btnDonutChart.Click += ShowDonutChart;
            btnGaugeChart.Click += ShowGaugeChart;
            
            // Tampilkan Bar Chart sebagai default
            Loaded += (s, e) => ShowBarChart(this, new RoutedEventArgs());
        }

        private void ShowBarChart(object? sender, RoutedEventArgs e)
        {
            ChartCanvas.Children.Clear();
            var chart = new BarChart();
            chart.Render(ChartCanvas);
        }

        private void ShowLineChart(object? sender, RoutedEventArgs e)
        {
            ChartCanvas.Children.Clear();
            var chart = new LineChart();
            chart.Render(ChartCanvas);
        }

        private void ShowPieChart(object? sender, RoutedEventArgs e)
        {
            ChartCanvas.Children.Clear();
            var chart = new PieChart();
            chart.Render(ChartCanvas);
        }

        private void ShowAreaChart(object? sender, RoutedEventArgs e)
        {
            ChartCanvas.Children.Clear();
            var chart = new AreaChart();
            chart.Render(ChartCanvas);
        }

        private void ShowScatterChart(object? sender, RoutedEventArgs e)
        {
            ChartCanvas.Children.Clear();
            var chart = new ScatterChart();
            chart.Render(ChartCanvas);
        }

        private void ShowBubbleChart(object? sender, RoutedEventArgs e)
        {
            ChartCanvas.Children.Clear();
            var chart = new BubbleChart();
            chart.Render(ChartCanvas);
        }

        private void ShowRadarChart(object? sender, RoutedEventArgs e)
        {
            ChartCanvas.Children.Clear();
            var chart = new RadarChart();
            chart.Render(ChartCanvas);
        }

        private void ShowHeatmap(object? sender, RoutedEventArgs e)
        {
            ChartCanvas.Children.Clear();
            var chart = new Heatmap();
            chart.Render(ChartCanvas);
        }

        private void ShowDonutChart(object? sender, RoutedEventArgs e)
        {
            ChartCanvas.Children.Clear();
            var chart = new DonutChart();
            chart.Render(ChartCanvas);
        }

        private void ShowGaugeChart(object? sender, RoutedEventArgs e)
        {
            ChartCanvas.Children.Clear();
            var chart = new GaugeChart();
            chart.Render(ChartCanvas);
        }
    }
}
