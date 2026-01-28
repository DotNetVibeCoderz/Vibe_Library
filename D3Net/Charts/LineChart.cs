using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using System;
using System.Collections.Generic;

namespace D3Net.Charts
{
    /// <summary>
    /// Line Chart - Grafik garis dengan animasi smooth
    /// Menampilkan trend data sepanjang waktu
    /// </summary>
    public class LineChart : BaseChart
    {
        public override void Render(Canvas canvas)
        {
            // Judul
            AddTextLabel(canvas, "📈 Line Chart - Trend Pertumbuhan", 
                canvas.Bounds.Width / 2 - 150, 20, 20, Colors.Black, FontWeight.Bold);

            // Data untuk 2 line series
            var months = new List<string> { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct" };
            var series1 = GenerateSampleData(10, 50, 180);
            var series2 = GenerateSampleData(10, 30, 150);

            double chartWidth = canvas.Bounds.Width - 150;
            double chartHeight = canvas.Bounds.Height - 150;
            double maxValue = Math.Max(GetMaxValue(series1), GetMaxValue(series2));

            double startX = 80;
            double startY = canvas.Bounds.Height - 80;

            // Grid lines
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

            // Render line series 1
            RenderLineSeries(canvas, series1, startX, startY, chartHeight, chartWidth, 
                maxValue, GetColor(0), "Series 1");

            // Render line series 2
            RenderLineSeries(canvas, series2, startX, startY, chartHeight, chartWidth, 
                maxValue, GetColor(2), "Series 2");

            // Legend
            DrawLegend(canvas, startX + chartWidth - 150, 60);
        }

        private void RenderLineSeries(Canvas canvas, List<double> data, double startX, 
            double startY, double chartHeight, double chartWidth, double maxValue, 
            Color color, string name)
        {
            double segmentWidth = chartWidth / (data.Count - 1);
            var points = new List<Point>();

            // Calculate points
            for (int i = 0; i < data.Count; i++)
            {
                double x = startX + (i * segmentWidth);
                double y = startY - ((data[i] / maxValue) * chartHeight);
                points.Add(new Point(x, y));
            }

            // Draw line segments
            for (int i = 0; i < points.Count - 1; i++)
            {
                var line = new Line
                {
                    StartPoint = points[i],
                    EndPoint = points[i + 1],
                    Stroke = new SolidColorBrush(color),
                    StrokeThickness = 3,
                    StrokeLineCap = PenLineCap.Round
                };
                canvas.Children.Add(line);
                AnimationHelper.FadeIn(line, i * 50, 300);
            }

            // Draw data points
            for (int i = 0; i < points.Count; i++)
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
                AnimationHelper.FadeIn(circle, i * 50 + 200, 300);
            }
        }

        private void DrawLegend(Canvas canvas, double x, double y)
        {
            // Series 1
            var rect1 = new Rectangle
            {
                Width = 30,
                Height = 3,
                Fill = new SolidColorBrush(GetColor(0))
            };
            Canvas.SetLeft(rect1, x);
            Canvas.SetTop(rect1, y);
            canvas.Children.Add(rect1);
            AddTextLabel(canvas, "Series 1", x + 40, y - 6, 11, Colors.Black);

            // Series 2
            var rect2 = new Rectangle
            {
                Width = 30,
                Height = 3,
                Fill = new SolidColorBrush(GetColor(2))
            };
            Canvas.SetLeft(rect2, x);
            Canvas.SetTop(rect2, y + 25);
            canvas.Children.Add(rect2);
            AddTextLabel(canvas, "Series 2", x + 40, y + 19, 11, Colors.Black);
        }
    }
}
