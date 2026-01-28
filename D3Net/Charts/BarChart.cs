using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using System;
using System.Collections.Generic;

namespace D3Net.Charts
{
    /// <summary>
    /// Bar Chart - Grafik batang dengan animasi smooth
    /// Menampilkan perbandingan data dalam bentuk batang vertikal
    /// </summary>
    public class BarChart : BaseChart
    {
        public override void Render(Canvas canvas)
        {
            // Judul chart
            AddTextLabel(canvas, "📊 Bar Chart - Penjualan Bulanan", 
                canvas.Bounds.Width / 2 - 150, 20, 20, Colors.Black, FontWeight.Bold);

            // Data sample
            var months = new List<string> { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct" };
            var values = GenerateSampleData(10, 50, 200);

            double chartWidth = canvas.Bounds.Width - 100;
            double chartHeight = canvas.Bounds.Height - 150;
            double barWidth = chartWidth / values.Count - 10;
            double maxValue = GetMaxValue(values);

            // Area chart
            double startX = 50;
            double startY = canvas.Bounds.Height - 80;

            // Render grid lines
            for (int i = 0; i <= 5; i++)
            {
                double y = startY - (chartHeight * i / 5);
                var line = new Line
                {
                    StartPoint = new Point(startX, y),
                    EndPoint = new Point(startX + chartWidth, y),
                    Stroke = new SolidColorBrush(Color.FromRgb(230, 230, 230)),
                    StrokeThickness = 1
                };
                canvas.Children.Add(line);

                // Label nilai
                AddTextLabel(canvas, $"{(int)(maxValue * i / 5)}", 
                    startX - 40, y - 8, 10, Color.FromRgb(100, 100, 100));
            }

            // Render bars dengan animasi
            for (int i = 0; i < values.Count; i++)
            {
                double barHeight = (values[i] / maxValue) * chartHeight;
                double x = startX + (i * (barWidth + 10));

                // Bar rectangle
                var bar = new Rectangle
                {
                    Width = barWidth,
                    Height = barHeight,
                    Fill = new SolidColorBrush(GetColor(i)),
                    Opacity = 0.8
                };

                Canvas.SetLeft(bar, x);
                Canvas.SetTop(bar, startY - barHeight);
                canvas.Children.Add(bar);

                // Animasi smooth untuk bar
                AnimationHelper.FadeIn(bar, i * 50, 400);

                // Label bulan
                AddTextLabel(canvas, months[i], x + barWidth / 2 - 15, 
                    startY + 20, 10, Color.FromRgb(80, 80, 80));

                // Value label di atas bar
                var valueLabel = new TextBlock
                {
                    Text = $"{(int)values[i]}",
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Colors.Black),
                    Opacity = 0
                };
                Canvas.SetLeft(valueLabel, x + barWidth / 2 - 10);
                Canvas.SetTop(valueLabel, startY - barHeight - 20);
                canvas.Children.Add(valueLabel);

                // Animasi untuk value label
                AnimationHelper.FadeIn(valueLabel, i * 50 + 300, 400);
            }

            // Legend
            AddTextLabel(canvas, "💡 Tip: Chart dengan animasi smooth untuk visualisasi yang menarik", 
                50, canvas.Bounds.Height - 30, 11, Color.FromRgb(120, 120, 120));
        }
    }
}
