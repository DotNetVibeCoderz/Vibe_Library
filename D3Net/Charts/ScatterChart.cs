using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using System;
using System.Collections.Generic;

namespace D3Net.Charts
{
    /// <summary>
    /// Scatter Chart - Grafik scatter plot dengan animasi
    /// Menampilkan korelasi antara dua variabel
    /// </summary>
    public class ScatterChart : BaseChart
    {
        public override void Render(Canvas canvas)
        {
            // Judul
            AddTextLabel(canvas, "⚫ Scatter Plot - Korelasi Data", 
                canvas.Bounds.Width / 2 - 150, 20, 20, Colors.Black, FontWeight.Bold);

            double chartWidth = canvas.Bounds.Width - 150;
            double chartHeight = canvas.Bounds.Height - 150;

            double startX = 80;
            double startY = canvas.Bounds.Height - 80;

            // Generate scatter data (3 clusters)
            var cluster1 = GenerateClusterData(30, 50, 100, 50, 100);
            var cluster2 = GenerateClusterData(30, 100, 150, 100, 150);
            var cluster3 = GenerateClusterData(30, 150, 200, 50, 100);

            double maxX = 200;
            double maxY = 200;

            // Grid
            for (int i = 0; i <= 5; i++)
            {
                // Horizontal grid
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

                // Vertical grid
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
                    x - 15, startY + 10, 10, Color.FromRgb(100, 100, 100));
            }

            // Axis labels
            AddTextLabel(canvas, "X Axis →", startX + chartWidth / 2 - 30, 
                startY + 40, 12, Color.FromRgb(80, 80, 80), FontWeight.Bold);
            
            AddTextLabel(canvas, "← Y Axis", startX - 70, 
                startY - chartHeight / 2, 12, Color.FromRgb(80, 80, 80), FontWeight.Bold);

            // Draw scatter points
            DrawScatterCluster(canvas, cluster1, startX, startY, chartWidth, chartHeight, 
                maxX, maxY, GetColor(0), 0);
            DrawScatterCluster(canvas, cluster2, startX, startY, chartWidth, chartHeight, 
                maxX, maxY, GetColor(1), 200);
            DrawScatterCluster(canvas, cluster3, startX, startY, chartWidth, chartHeight, 
                maxX, maxY, GetColor(2), 400);

            // Legend
            DrawLegend(canvas, startX + chartWidth - 120, 60);
        }

        private List<(double x, double y)> GenerateClusterData(int count, double centerX, 
            double rangeX, double centerY, double rangeY)
        {
            var data = new List<(double, double)>();
            for (int i = 0; i < count; i++)
            {
                double x = centerX + (random.NextDouble() - 0.5) * rangeX;
                double y = centerY + (random.NextDouble() - 0.5) * rangeY;
                data.Add((x, y));
            }
            return data;
        }

        private void DrawScatterCluster(Canvas canvas, List<(double x, double y)> data, 
            double startX, double startY, double chartWidth, double chartHeight, 
            double maxX, double maxY, Color color, int baseDelay)
        {
            for (int i = 0; i < data.Count; i++)
            {
                double plotX = startX + (data[i].x / maxX) * chartWidth;
                double plotY = startY - (data[i].y / maxY) * chartHeight;

                var circle = new Ellipse
                {
                    Width = 8,
                    Height = 8,
                    Fill = new SolidColorBrush(Color.FromArgb(180, color.R, color.G, color.B)),
                    Stroke = new SolidColorBrush(color),
                    StrokeThickness = 2
                };

                Canvas.SetLeft(circle, plotX - 4);
                Canvas.SetTop(circle, plotY - 4);
                canvas.Children.Add(circle);
                AnimationHelper.FadeIn(circle, baseDelay + (i * 10), 300);
            }
        }

        private void DrawLegend(Canvas canvas, double x, double y)
        {
            var clusters = new[] { "Cluster A", "Cluster B", "Cluster C" };
            for (int i = 0; i < clusters.Length; i++)
            {
                var circle = new Ellipse
                {
                    Width = 10,
                    Height = 10,
                    Fill = new SolidColorBrush(GetColor(i))
                };
                Canvas.SetLeft(circle, x);
                Canvas.SetTop(circle, y + (i * 25));
                canvas.Children.Add(circle);

                AddTextLabel(canvas, clusters[i], x + 20, y + (i * 25) - 2, 11, Colors.Black);
            }
        }
    }
}
