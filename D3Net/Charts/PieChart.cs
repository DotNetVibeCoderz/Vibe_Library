using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using AvaloniaPath = Avalonia.Controls.Shapes.Path;

namespace D3Net.Charts
{
    /// <summary>
    /// Pie Chart - Grafik lingkaran dengan animasi smooth
    /// Menampilkan proporsi data dalam bentuk irisan lingkaran
    /// </summary>
    public class PieChart : BaseChart
    {
        public override void Render(Canvas canvas)
        {
            // Judul
            AddTextLabel(canvas, "🥧 Pie Chart - Distribusi Kategori", 
                canvas.Bounds.Width / 2 - 150, 20, 20, Colors.Black, FontWeight.Bold);

            // Data
            var categories = new List<string> { "Mobile", "Desktop", "Tablet", "TV", "Others" };
            var values = new List<double> { 45, 30, 15, 7, 3 };
            
            double centerX = canvas.Bounds.Width / 2;
            double centerY = canvas.Bounds.Height / 2 + 20;
            double radius = Math.Min(canvas.Bounds.Width, canvas.Bounds.Height) / 3;

            double total = 0;
            foreach (var val in values) total += val;

            double startAngle = -90; // Start dari atas

            // Render pie slices
            for (int i = 0; i < values.Count; i++)
            {
                double angle = (values[i] / total) * 360;
                
                // Create path for pie slice
                var slice = CreatePieSlice(centerX, centerY, radius, startAngle, angle, GetColor(i));
                canvas.Children.Add(slice);
                AnimationHelper.FadeIn(slice, i * 100, 600);

                // Label
                double labelAngle = startAngle + (angle / 2);
                double labelRadius = radius * 0.7;
                double labelX = centerX + labelRadius * Math.Cos(labelAngle * Math.PI / 180);
                double labelY = centerY + labelRadius * Math.Sin(labelAngle * Math.PI / 180);

                var percentLabel = new TextBlock
                {
                    Text = $"{values[i]:F0}%",
                    FontSize = 14,
                    FontWeight = FontWeight.Bold,
                    Foreground = new SolidColorBrush(Colors.White)
                };
                Canvas.SetLeft(percentLabel, labelX - 15);
                Canvas.SetTop(percentLabel, labelY - 10);
                canvas.Children.Add(percentLabel);
                AnimationHelper.FadeIn(percentLabel, i * 100 + 400, 400);

                startAngle += angle;
            }

            // Legend
            DrawLegend(canvas, categories, values, centerX + radius + 50, centerY - 80);
        }

        private AvaloniaPath CreatePieSlice(double centerX, double centerY, double radius, 
            double startAngle, double angle, Color color)
        {
            double endAngle = startAngle + angle;
            
            // Convert to radians
            double startRad = startAngle * Math.PI / 180;
            double endRad = endAngle * Math.PI / 180;

            // Calculate points
            Point startPoint = new Point(
                centerX + radius * Math.Cos(startRad),
                centerY + radius * Math.Sin(startRad)
            );

            Point endPoint = new Point(
                centerX + radius * Math.Cos(endRad),
                centerY + radius * Math.Sin(endRad)
            );

            bool largeArc = angle > 180;

            var pathFigure = new PathFigure
            {
                StartPoint = new Point(centerX, centerY),
                IsClosed = true
            };

            pathFigure.Segments?.Add(new LineSegment { Point = startPoint });
            pathFigure.Segments?.Add(new ArcSegment
            {
                Point = endPoint,
                Size = new Size(radius, radius),
                SweepDirection = SweepDirection.Clockwise,
                IsLargeArc = largeArc
            });

            var pathGeometry = new PathGeometry();
            pathGeometry.Figures?.Add(pathFigure);

            var path = new AvaloniaPath
            {
                Data = pathGeometry,
                Fill = new SolidColorBrush(color),
                Stroke = new SolidColorBrush(Colors.White),
                StrokeThickness = 2,
                Opacity = 0.9
            };

            return path;
        }

        private void DrawLegend(Canvas canvas, List<string> categories, List<double> values, 
            double x, double y)
        {
            for (int i = 0; i < categories.Count; i++)
            {
                // Color box
                var box = new Rectangle
                {
                    Width = 20,
                    Height = 20,
                    Fill = new SolidColorBrush(GetColor(i))
                };
                Canvas.SetLeft(box, x);
                Canvas.SetTop(box, y + (i * 30));
                canvas.Children.Add(box);

                // Label
                AddTextLabel(canvas, $"{categories[i]}: {values[i]:F0}%", 
                    x + 30, y + (i * 30) + 3, 12, Colors.Black);
            }
        }
    }
}
