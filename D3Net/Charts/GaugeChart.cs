using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using System;
using AvaloniaPath = Avalonia.Controls.Shapes.Path;

namespace D3Net.Charts
{
    /// <summary>
    /// Gauge Chart - Speedometer style gauge
    /// Menampilkan nilai tunggal dalam bentuk meter/jarum
    /// </summary>
    public class GaugeChart : BaseChart
    {
        public override void Render(Canvas canvas)
        {
            // Judul
            AddTextLabel(canvas, "🎯 Gauge Chart - Performance Monitor", 
                canvas.Bounds.Width / 2 - 180, 20, 20, Colors.Black, FontWeight.Bold);

            double centerX = canvas.Bounds.Width / 2;
            double centerY = canvas.Bounds.Height / 2 + 50;

            // Draw 3 gauges
            DrawGauge(canvas, centerX - 250, centerY, "CPU Usage", 75, Color.FromRgb(52, 152, 219), 0);
            DrawGauge(canvas, centerX, centerY, "Memory", 45, Color.FromRgb(46, 204, 113), 200);
            DrawGauge(canvas, centerX + 250, centerY, "Disk I/O", 88, Color.FromRgb(231, 76, 60), 400);

            // Info
            AddTextLabel(canvas, "💡 Real-time monitoring dashboard simulation", 
                50, canvas.Bounds.Height - 30, 11, Color.FromRgb(120, 120, 120));
        }

        private void DrawGauge(Canvas canvas, double centerX, double centerY, 
            string label, double value, Color color, int delay)
        {
            double radius = 100;
            double startAngle = 135; // Start dari kiri bawah
            double endAngle = 45;    // End di kanan bawah
            double totalAngle = 360 - startAngle + endAngle;

            // Background arc
            var bgArc = CreateArc(centerX, centerY, radius, startAngle, totalAngle, 
                Color.FromRgb(230, 230, 230), 15);
            canvas.Children.Add(bgArc);

            // Foreground arc (value)
            double valueAngle = (value / 100.0) * totalAngle;
            var fgArc = CreateArc(centerX, centerY, radius, startAngle, valueAngle, 
                color, 15);
            canvas.Children.Add(fgArc);
            AnimationHelper.FadeIn(fgArc, delay, 1000);

            // Center circle
            var centerCircle = new Ellipse
            {
                Width = radius * 1.5,
                Height = radius * 1.5,
                Fill = new SolidColorBrush(Colors.White),
                Stroke = new SolidColorBrush(Color.FromRgb(240, 240, 240)),
                StrokeThickness = 3
            };
            Canvas.SetLeft(centerCircle, centerX - radius * 0.75);
            Canvas.SetTop(centerCircle, centerY - radius * 0.75);
            canvas.Children.Add(centerCircle);

            // Draw ticks
            for (int i = 0; i <= 10; i++)
            {
                double angle = startAngle + (totalAngle * i / 10.0);
                double angleRad = angle * Math.PI / 180;

                double innerR = radius - 10;
                double outerR = radius - 3;

                double x1 = centerX + innerR * Math.Cos(angleRad);
                double y1 = centerY + innerR * Math.Sin(angleRad);
                double x2 = centerX + outerR * Math.Cos(angleRad);
                double y2 = centerY + outerR * Math.Sin(angleRad);

                var tick = new Line
                {
                    StartPoint = new Point(x1, y1),
                    EndPoint = new Point(x2, y2),
                    Stroke = new SolidColorBrush(Color.FromRgb(180, 180, 180)),
                    StrokeThickness = 2
                };
                canvas.Children.Add(tick);

                // Tick labels
                if (i % 2 == 0)
                {
                    double labelR = radius - 25;
                    double labelX = centerX + labelR * Math.Cos(angleRad);
                    double labelY = centerY + labelR * Math.Sin(angleRad);

                    AddTextLabel(canvas, $"{i * 10}", labelX - 8, labelY - 8, 10, 
                        Color.FromRgb(120, 120, 120));
                }
            }

            // Needle
            double needleAngle = startAngle + valueAngle;
            var needle = CreateNeedle(centerX, centerY, radius - 20, needleAngle, color);
            canvas.Children.Add(needle);
            AnimationHelper.FadeIn(needle, delay + 300, 800);

            // Needle center
            var needleCenter = new Ellipse
            {
                Width = 12,
                Height = 12,
                Fill = new SolidColorBrush(color)
            };
            Canvas.SetLeft(needleCenter, centerX - 6);
            Canvas.SetTop(needleCenter, centerY - 6);
            canvas.Children.Add(needleCenter);

            // Value text
            var valueText = new TextBlock
            {
                Text = $"{(int)value}%",
                FontSize = 28,
                FontWeight = FontWeight.Bold,
                Foreground = new SolidColorBrush(color)
            };
            Canvas.SetLeft(valueText, centerX - 25);
            Canvas.SetTop(valueText, centerY - 10);
            canvas.Children.Add(valueText);
            AnimationHelper.FadeIn(valueText, delay + 600, 400);

            // Label
            var labelText = new TextBlock
            {
                Text = label,
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 100, 100))
            };
            Canvas.SetLeft(labelText, centerX - (label.Length * 3.5));
            Canvas.SetTop(labelText, centerY + 25);
            canvas.Children.Add(labelText);
            AnimationHelper.FadeIn(labelText, delay + 700, 400);

            // Status indicator
            string status = value < 50 ? "Normal" : value < 80 ? "Warning" : "Critical";
            Color statusColor = value < 50 ? Color.FromRgb(46, 204, 113) : 
                              value < 80 ? Color.FromRgb(241, 196, 15) : 
                              Color.FromRgb(231, 76, 60);

            var statusRect = new Rectangle
            {
                Width = 80,
                Height = 25,
                Fill = new SolidColorBrush(statusColor),
                RadiusX = 12,
                RadiusY = 12
            };
            Canvas.SetLeft(statusRect, centerX - 40);
            Canvas.SetTop(statusRect, centerY + 50);
            canvas.Children.Add(statusRect);
            AnimationHelper.FadeIn(statusRect, delay + 800, 400);

            var statusText = new TextBlock
            {
                Text = status,
                FontSize = 12,
                FontWeight = FontWeight.Bold,
                Foreground = new SolidColorBrush(Colors.White)
            };
            Canvas.SetLeft(statusText, centerX - (status.Length * 3));
            Canvas.SetTop(statusText, centerY + 56);
            canvas.Children.Add(statusText);
            AnimationHelper.FadeIn(statusText, delay + 850, 400);
        }

        private AvaloniaPath CreateArc(double centerX, double centerY, double radius, 
            double startAngle, double sweepAngle, Color color, double thickness)
        {
            double startRad = startAngle * Math.PI / 180;
            double endRad = (startAngle + sweepAngle) * Math.PI / 180;

            Point start = new Point(
                centerX + radius * Math.Cos(startRad),
                centerY + radius * Math.Sin(startRad)
            );

            Point end = new Point(
                centerX + radius * Math.Cos(endRad),
                centerY + radius * Math.Sin(endRad)
            );

            var pathFigure = new PathFigure
            {
                StartPoint = start,
                IsClosed = false
            };

            pathFigure.Segments?.Add(new ArcSegment
            {
                Point = end,
                Size = new Size(radius, radius),
                SweepDirection = SweepDirection.Clockwise,
                IsLargeArc = sweepAngle > 180
            });

            var pathGeometry = new PathGeometry();
            pathGeometry.Figures?.Add(pathFigure);

            var path = new AvaloniaPath
            {
                Data = pathGeometry,
                Stroke = new SolidColorBrush(color),
                StrokeThickness = thickness,
                StrokeLineCap = PenLineCap.Round
            };

            return path;
        }

        private AvaloniaPath CreateNeedle(double centerX, double centerY, double length, 
            double angle, Color color)
        {
            double angleRad = angle * Math.PI / 180;
            Point tip = new Point(
                centerX + length * Math.Cos(angleRad),
                centerY + length * Math.Sin(angleRad)
            );

            var pathFigure = new PathFigure
            {
                StartPoint = new Point(centerX, centerY),
                IsClosed = false
            };

            pathFigure.Segments?.Add(new LineSegment { Point = tip });

            var pathGeometry = new PathGeometry();
            pathGeometry.Figures?.Add(pathFigure);

            var needle = new AvaloniaPath
            {
                Data = pathGeometry,
                Stroke = new SolidColorBrush(color),
                StrokeThickness = 4,
                StrokeLineCap = PenLineCap.Round
            };

            return needle;
        }
    }
}
