using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using System;
using System.Collections.Generic;

namespace D3Net.Charts
{
    /// <summary>
    /// Radar Chart - Grafik radar/spider untuk multi-kategori
    /// Cocok untuk membandingkan beberapa variabel sekaligus
    /// </summary>
    public class RadarChart : BaseChart
    {
        public override void Render(Canvas canvas)
        {
            // Judul
            AddTextLabel(canvas, "🕸️ Radar Chart - Analisis Multi-Dimensi", 
                canvas.Bounds.Width / 2 - 180, 20, 20, Colors.Black, FontWeight.Bold);

            double centerX = canvas.Bounds.Width / 2;
            double centerY = canvas.Bounds.Height / 2 + 20;
            double maxRadius = Math.Min(canvas.Bounds.Width, canvas.Bounds.Height) / 3;

            // Categories
            var categories = new List<string> { "Speed", "Quality", "Cost", "Support", "Features", "Security" };
            var data1 = new List<double> { 85, 70, 60, 90, 75, 80 };
            var data2 = new List<double> { 60, 85, 90, 70, 85, 75 };

            int numAxes = categories.Count;
            double angleStep = 360.0 / numAxes;

            // Draw background web
            DrawRadarWeb(canvas, centerX, centerY, maxRadius, numAxes, angleStep);

            // Draw axis lines and labels
            for (int i = 0; i < numAxes; i++)
            {
                double angle = -90 + (i * angleStep); // Start from top
                double angleRad = angle * Math.PI / 180;

                double x = centerX + maxRadius * Math.Cos(angleRad);
                double y = centerY + maxRadius * Math.Sin(angleRad);

                // Axis line
                var line = new Line
                {
                    StartPoint = new Point(centerX, centerY),
                    EndPoint = new Point(x, y),
                    Stroke = new SolidColorBrush(Color.FromRgb(180, 180, 180)),
                    StrokeThickness = 1
                };
                canvas.Children.Add(line);

                // Label
                double labelRadius = maxRadius + 30;
                double labelX = centerX + labelRadius * Math.Cos(angleRad);
                double labelY = centerY + labelRadius * Math.Sin(angleRad);

                AddTextLabel(canvas, categories[i], labelX - 25, labelY - 8, 11, Colors.Black, FontWeight.Bold);
            }

            // Draw data series
            DrawRadarSeries(canvas, data1, centerX, centerY, maxRadius, numAxes, 
                angleStep, GetColor(0), "Product A");
            DrawRadarSeries(canvas, data2, centerX, centerY, maxRadius, numAxes, 
                angleStep, GetColor(2), "Product B");

            // Legend
            DrawLegend(canvas, centerX - 80, centerY + maxRadius + 60);
        }

        private void DrawRadarWeb(Canvas canvas, double centerX, double centerY, 
            double maxRadius, int numAxes, double angleStep)
        {
            // Draw concentric polygons (5 levels)
            for (int level = 1; level <= 5; level++)
            {
                double radius = (maxRadius * level) / 5;
                var points = new List<Point>();

                for (int i = 0; i < numAxes; i++)
                {
                    double angle = -90 + (i * angleStep);
                    double angleRad = angle * Math.PI / 180;

                    double x = centerX + radius * Math.Cos(angleRad);
                    double y = centerY + radius * Math.Sin(angleRad);
                    points.Add(new Point(x, y));
                }

                // Close the polygon
                points.Add(points[0]);

                var polyline = new Polyline
                {
                    Points = points,
                    Stroke = new SolidColorBrush(Color.FromRgb(220, 220, 220)),
                    StrokeThickness = 1
                };
                canvas.Children.Add(polyline);

                // Level label
                if (level == 5)
                {
                    AddTextLabel(canvas, "100", centerX + radius + 5, centerY - 5, 9, 
                        Color.FromRgb(150, 150, 150));
                }
            }
        }

        private void DrawRadarSeries(Canvas canvas, List<double> data, double centerX, 
            double centerY, double maxRadius, int numAxes, double angleStep, 
            Color color, string name)
        {
            var points = new List<Point>();

            // Calculate points
            for (int i = 0; i < numAxes; i++)
            {
                double angle = -90 + (i * angleStep);
                double angleRad = angle * Math.PI / 180;

                double radius = (data[i] / 100.0) * maxRadius;
                double x = centerX + radius * Math.Cos(angleRad);
                double y = centerY + radius * Math.Sin(angleRad);
                points.Add(new Point(x, y));
            }

            // Close the polygon
            points.Add(points[0]);

            // Create filled polygon
            var polygon = new Polygon
            {
                Points = points,
                Fill = new SolidColorBrush(Color.FromArgb(80, color.R, color.G, color.B)),
                Stroke = new SolidColorBrush(color),
                StrokeThickness = 3
            };
            canvas.Children.Add(polygon);
            AnimationHelper.FadeIn(polygon, 200, 800);

            // Draw data points
            for (int i = 0; i < numAxes; i++)
            {
                var circle = new Ellipse
                {
                    Width = 8,
                    Height = 8,
                    Fill = new SolidColorBrush(color),
                    Stroke = new SolidColorBrush(Colors.White),
                    StrokeThickness = 2
                };

                Canvas.SetLeft(circle, points[i].X - 4);
                Canvas.SetTop(circle, points[i].Y - 4);
                canvas.Children.Add(circle);
                AnimationHelper.FadeIn(circle, 600 + (i * 50), 300);
            }
        }

        private void DrawLegend(Canvas canvas, double x, double y)
        {
            var items = new[] { ("Product A", GetColor(0)), ("Product B", GetColor(2)) };
            
            for (int i = 0; i < items.Length; i++)
            {
                var rect = new Rectangle
                {
                    Width = 20,
                    Height = 4,
                    Fill = new SolidColorBrush(items[i].Item2)
                };
                Canvas.SetLeft(rect, x + (i * 120));
                Canvas.SetTop(rect, y);
                canvas.Children.Add(rect);

                AddTextLabel(canvas, items[i].Item1, x + (i * 120) + 25, y - 6, 11, Colors.Black);
            }
        }
    }
}
