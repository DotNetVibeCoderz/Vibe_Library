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
    /// Area Chart - Grafik area dengan gradient dan animasi smooth
    /// Menampilkan volume data sepanjang waktu
    /// </summary>
    public class AreaChart : BaseChart
    {
        public override void Render(Canvas canvas)
        {
            // Judul
            AddTextLabel(canvas, "📉 Area Chart - Volume Transaksi", 
                canvas.Bounds.Width / 2 - 150, 20, 20, Colors.Black, FontWeight.Bold);

            // Data
            var months = new List<string> { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct" };
            var data = GenerateSampleData(10, 30, 180);

            double chartWidth = canvas.Bounds.Width - 150;
            double chartHeight = canvas.Bounds.Height - 150;
            double maxValue = GetMaxValue(data);

            double startX = 80;
            double startY = canvas.Bounds.Height - 80;

            // Grid
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

                AddTextLabel(canvas, $"{(int)(maxValue * i / 5)}", 
                    startX - 50, y - 8, 10, Color.FromRgb(100, 100, 100));
            }

            // X-axis labels
            double segmentWidth = chartWidth / (months.Count - 1);
            for (int i = 0; i < months.Count; i++)
            {
                double x = startX + (i * segmentWidth);
                AddTextLabel(canvas, months[i], x - 15, startY + 20, 10, Color.FromRgb(80, 80, 80));
            }

            // Calculate points untuk area
            var points = new List<Point>();
            for (int i = 0; i < data.Count; i++)
            {
                double x = startX + (i * segmentWidth);
                double y = startY - ((data[i] / maxValue) * chartHeight);
                points.Add(new Point(x, y));
            }

            // Create area path dengan gradient
            var areaPath = CreateAreaPath(points, startX, startY, chartWidth);
            canvas.Children.Add(areaPath);
            AnimationHelper.FadeIn(areaPath, 100, 1000);

            // Draw line on top
            for (int i = 0; i < points.Count - 1; i++)
            {
                var line = new Line
                {
                    StartPoint = points[i],
                    EndPoint = points[i + 1],
                    Stroke = new SolidColorBrush(GetColor(0)),
                    StrokeThickness = 3,
                    StrokeLineCap = PenLineCap.Round
                };
                canvas.Children.Add(line);
                AnimationHelper.FadeIn(line, i * 50, 400);
            }

            // Draw data points
            for (int i = 0; i < points.Count; i++)
            {
                var circle = new Ellipse
                {
                    Width = 10,
                    Height = 10,
                    Fill = new SolidColorBrush(GetColor(0)),
                    Stroke = new SolidColorBrush(Colors.White),
                    StrokeThickness = 2
                };

                Canvas.SetLeft(circle, points[i].X - 5);
                Canvas.SetTop(circle, points[i].Y - 5);
                canvas.Children.Add(circle);
                AnimationHelper.FadeIn(circle, i * 50 + 200, 300);
            }

            // Info
            AddTextLabel(canvas, "💡 Area chart menunjukkan volume dan trend data", 
                50, canvas.Bounds.Height - 30, 11, Color.FromRgb(120, 120, 120));
        }

        private AvaloniaPath CreateAreaPath(List<Point> points, double startX, double baseY, double width)
        {
            var pathFigure = new PathFigure
            {
                StartPoint = new Point(startX, baseY),
                IsClosed = true
            };

            // Line to first point
            pathFigure.Segments?.Add(new LineSegment { Point = points[0] });

            // All data points
            for (int i = 1; i < points.Count; i++)
            {
                pathFigure.Segments?.Add(new LineSegment { Point = points[i] });
            }

            // Close the path
            pathFigure.Segments?.Add(new LineSegment { Point = new Point(startX + width, baseY) });

            var pathGeometry = new PathGeometry();
            pathGeometry.Figures?.Add(pathFigure);

            // Create gradient brush
            var gradientBrush = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative)
            };
            gradientBrush.GradientStops.Add(new GradientStop 
            { 
                Color = Color.FromArgb(200, GetColor(0).R, GetColor(0).G, GetColor(0).B), 
                Offset = 0 
            });
            gradientBrush.GradientStops.Add(new GradientStop 
            { 
                Color = Color.FromArgb(50, GetColor(0).R, GetColor(0).G, GetColor(0).B), 
                Offset = 1 
            });

            var path = new AvaloniaPath
            {
                Data = pathGeometry,
                Fill = gradientBrush
            };

            return path;
        }
    }
}
