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
    /// Donut Chart - Grafik donat dengan lubang di tengah
    /// Mirip pie chart tapi dengan area center untuk info tambahan
    /// </summary>
    public class DonutChart : BaseChart
    {
        public override void Render(Canvas canvas)
        {
            // Judul
            AddTextLabel(canvas, "🍩 Donut Chart - Komposisi Data", 
                canvas.Bounds.Width / 2 - 150, 20, 20, Colors.Black, FontWeight.Bold);

            // Data
            var categories = new List<string> { "Product A", "Product B", "Product C", "Product D", "Others" };
            var values = new List<double> { 35, 28, 20, 12, 5 };

            double centerX = canvas.Bounds.Width / 2;
            double centerY = canvas.Bounds.Height / 2 + 20;
            double outerRadius = Math.Min(canvas.Bounds.Width, canvas.Bounds.Height) / 3.5;
            double innerRadius = outerRadius * 0.6; // Donut hole

            double total = 0;
            foreach (var val in values) total += val;

            double startAngle = -90;

            // Render donut slices
            for (int i = 0; i < values.Count; i++)
            {
                double angle = (values[i] / total) * 360;

                // Create donut slice
                var slice = CreateDonutSlice(centerX, centerY, outerRadius, innerRadius, 
                    startAngle, angle, GetColor(i));
                canvas.Children.Add(slice);
                AnimationHelper.FadeIn(slice, i * 100, 700);

                // Percentage label on slice
                double labelAngle = startAngle + (angle / 2);
                double labelRadius = innerRadius + (outerRadius - innerRadius) / 2;
                double labelX = centerX + labelRadius * Math.Cos(labelAngle * Math.PI / 180);
                double labelY = centerY + labelRadius * Math.Sin(labelAngle * Math.PI / 180);

                var percentLabel = new TextBlock
                {
                    Text = $"{values[i]:F0}%",
                    FontSize = 13,
                    FontWeight = FontWeight.Bold,
                    Foreground = new SolidColorBrush(Colors.White)
                };
                Canvas.SetLeft(percentLabel, labelX - 15);
                Canvas.SetTop(percentLabel, labelY - 10);
                canvas.Children.Add(percentLabel);
                AnimationHelper.FadeIn(percentLabel, i * 100 + 500, 400);

                startAngle += angle;
            }

            // Center info circle
            var centerCircle = new Ellipse
            {
                Width = innerRadius * 2 - 10,
                Height = innerRadius * 2 - 10,
                Fill = new SolidColorBrush(Color.FromRgb(248, 249, 250)),
                Stroke = new SolidColorBrush(Color.FromRgb(220, 220, 220)),
                StrokeThickness = 2
            };
            Canvas.SetLeft(centerCircle, centerX - innerRadius + 5);
            Canvas.SetTop(centerCircle, centerY - innerRadius + 5);
            canvas.Children.Add(centerCircle);
            AnimationHelper.FadeIn(centerCircle, 600, 500);

            // Center text
            var centerText1 = new TextBlock
            {
                Text = "Total",
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromRgb(120, 120, 120))
            };
            Canvas.SetLeft(centerText1, centerX - 20);
            Canvas.SetTop(centerText1, centerY - 25);
            canvas.Children.Add(centerText1);
            AnimationHelper.FadeIn(centerText1, 800, 400);

            var centerText2 = new TextBlock
            {
                Text = "100%",
                FontSize = 24,
                FontWeight = FontWeight.Bold,
                Foreground = new SolidColorBrush(Colors.Black)
            };
            Canvas.SetLeft(centerText2, centerX - 30);
            Canvas.SetTop(centerText2, centerY - 5);
            canvas.Children.Add(centerText2);
            AnimationHelper.FadeIn(centerText2, 900, 400);

            // Legend
            DrawLegend(canvas, categories, values, centerX + outerRadius + 50, centerY - 80);
        }

        private AvaloniaPath CreateDonutSlice(double centerX, double centerY, double outerRadius, 
            double innerRadius, double startAngle, double angle, Color color)
        {
            double endAngle = startAngle + angle;

            // Convert to radians
            double startRad = startAngle * Math.PI / 180;
            double endRad = endAngle * Math.PI / 180;

            // Outer arc points
            Point outerStart = new Point(
                centerX + outerRadius * Math.Cos(startRad),
                centerY + outerRadius * Math.Sin(startRad)
            );
            Point outerEnd = new Point(
                centerX + outerRadius * Math.Cos(endRad),
                centerY + outerRadius * Math.Sin(endRad)
            );

            // Inner arc points
            Point innerStart = new Point(
                centerX + innerRadius * Math.Cos(startRad),
                centerY + innerRadius * Math.Sin(startRad)
            );
            Point innerEnd = new Point(
                centerX + innerRadius * Math.Cos(endRad),
                centerY + innerRadius * Math.Sin(endRad)
            );

            bool largeArc = angle > 180;

            var pathFigure = new PathFigure
            {
                StartPoint = outerStart,
                IsClosed = true
            };

            // Outer arc
            pathFigure.Segments?.Add(new ArcSegment
            {
                Point = outerEnd,
                Size = new Size(outerRadius, outerRadius),
                SweepDirection = SweepDirection.Clockwise,
                IsLargeArc = largeArc
            });

            // Line to inner arc
            pathFigure.Segments?.Add(new LineSegment { Point = innerEnd });

            // Inner arc (reverse direction)
            pathFigure.Segments?.Add(new ArcSegment
            {
                Point = innerStart,
                Size = new Size(innerRadius, innerRadius),
                SweepDirection = SweepDirection.CounterClockwise,
                IsLargeArc = largeArc
            });

            var pathGeometry = new PathGeometry();
            pathGeometry.Figures?.Add(pathFigure);

            var path = new AvaloniaPath
            {
                Data = pathGeometry,
                Fill = new SolidColorBrush(color),
                Stroke = new SolidColorBrush(Colors.White),
                StrokeThickness = 3,
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
                    Fill = new SolidColorBrush(GetColor(i)),
                    RadiusX = 3,
                    RadiusY = 3
                };
                Canvas.SetLeft(box, x);
                Canvas.SetTop(box, y + (i * 35));
                canvas.Children.Add(box);

                // Label
                AddTextLabel(canvas, categories[i], x + 30, y + (i * 35) + 3, 11, 
                    Colors.Black, FontWeight.Bold);
                AddTextLabel(canvas, $"{values[i]:F0}%", x + 30, y + (i * 35) + 17, 9, 
                    Color.FromRgb(120, 120, 120));
            }
        }
    }
}
