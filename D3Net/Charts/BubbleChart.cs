using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using System;
using System.Collections.Generic;

namespace D3Net.Charts
{
    /// <summary>
    /// Bubble Chart - Grafik gelembung dengan 3 dimensi data
    /// Ukuran bubble merepresentasikan nilai ketiga
    /// </summary>
    public class BubbleChart : BaseChart
    {
        public override void Render(Canvas canvas)
        {
            // Judul
            AddTextLabel(canvas, "🔵 Bubble Chart - Multi-Dimensional Data", 
                canvas.Bounds.Width / 2 - 180, 20, 20, Colors.Black, FontWeight.Bold);

            double chartWidth = canvas.Bounds.Width - 200;
            double chartHeight = canvas.Bounds.Height - 150;

            double startX = 100;
            double startY = canvas.Bounds.Height - 80;

            // Generate bubble data (x, y, size)
            var bubbles = GenerateBubbleData(20);

            double maxX = 100;
            double maxY = 100;
            double maxSize = 50;

            // Grid
            for (int i = 0; i <= 5; i++)
            {
                // Horizontal
                double y = startY - (chartHeight * i / 5);
                var hLine = new Line
                {
                    StartPoint = new Point(startX, y),
                    EndPoint = new Point(startX + chartWidth, y),
                    Stroke = new SolidColorBrush(Color.FromRgb(230, 230, 230)),
                    StrokeThickness = 1
                };
                canvas.Children.Add(hLine);

                AddTextLabel(canvas, $"{(int)(maxY * i / 5)}", 
                    startX - 40, y - 8, 10, Color.FromRgb(100, 100, 100));

                // Vertical
                double x = startX + (chartWidth * i / 5);
                var vLine = new Line
                {
                    StartPoint = new Point(x, startY),
                    EndPoint = new Point(x, startY - chartHeight),
                    Stroke = new SolidColorBrush(Color.FromRgb(230, 230, 230)),
                    StrokeThickness = 1
                };
                canvas.Children.Add(vLine);

                AddTextLabel(canvas, $"{(int)(maxX * i / 5)}", 
                    x - 10, startY + 10, 10, Color.FromRgb(100, 100, 100));
            }

            // Draw bubbles
            for (int i = 0; i < bubbles.Count; i++)
            {
                double plotX = startX + (bubbles[i].x / maxX) * chartWidth;
                double plotY = startY - (bubbles[i].y / maxY) * chartHeight;
                double radius = (bubbles[i].size / maxSize) * 40;

                var bubble = new Ellipse
                {
                    Width = radius * 2,
                    Height = radius * 2,
                    Fill = new SolidColorBrush(Color.FromArgb(120, 
                        GetColor(i % 5).R, GetColor(i % 5).G, GetColor(i % 5).B)),
                    Stroke = new SolidColorBrush(GetColor(i % 5)),
                    StrokeThickness = 2
                };

                Canvas.SetLeft(bubble, plotX - radius);
                Canvas.SetTop(bubble, plotY - radius);
                canvas.Children.Add(bubble);
                AnimationHelper.FadeIn(bubble, i * 50, 600);

                // Value label
                var label = new TextBlock
                {
                    Text = $"{(int)bubbles[i].size}",
                    FontSize = 10,
                    FontWeight = FontWeight.Bold,
                    Foreground = new SolidColorBrush(Colors.Black)
                };
                Canvas.SetLeft(label, plotX - 8);
                Canvas.SetTop(label, plotY - 6);
                canvas.Children.Add(label);
                AnimationHelper.FadeIn(label, i * 50 + 300, 400);
            }

            // Info
            AddTextLabel(canvas, "💡 Ukuran bubble menunjukkan nilai data ketiga", 
                50, canvas.Bounds.Height - 30, 11, Color.FromRgb(120, 120, 120));
        }

        private List<(double x, double y, double size)> GenerateBubbleData(int count)
        {
            var data = new List<(double, double, double)>();
            for (int i = 0; i < count; i++)
            {
                double x = random.NextDouble() * 100;
                double y = random.NextDouble() * 100;
                double size = 10 + random.NextDouble() * 40;
                data.Add((x, y, size));
            }
            return data;
        }
    }
}
