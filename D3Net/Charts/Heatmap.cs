using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using System;
using System.Collections.Generic;

namespace D3Net.Charts
{
    /// <summary>
    /// Heatmap - Peta panas untuk visualisasi data matriks
    /// Intensitas warna menunjukkan nilai data
    /// </summary>
    public class Heatmap : BaseChart
    {
        public override void Render(Canvas canvas)
        {
            // Judul
            AddTextLabel(canvas, "🔥 Heatmap - Aktivitas Jam vs Hari", 
                canvas.Bounds.Width / 2 - 150, 20, 20, Colors.Black, FontWeight.Bold);

            var days = new List<string> { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };
            var hours = new List<string> { "00", "04", "08", "12", "16", "20" };

            int rows = days.Count;
            int cols = hours.Count;

            double cellWidth = 80;
            double cellHeight = 60;
            double startX = 100;
            double startY = 100;

            // Generate random data matrix
            var data = new double[rows, cols];
            for (int i = 0; i < rows; i++)
            {
                for (int j = 0; j < cols; j++)
                {
                    data[i, j] = random.NextDouble() * 100;
                }
            }

            // Find max value for normalization
            double maxValue = 0;
            for (int i = 0; i < rows; i++)
            {
                for (int j = 0; j < cols; j++)
                {
                    if (data[i, j] > maxValue) maxValue = data[i, j];
                }
            }

            // Draw column headers (hours)
            for (int j = 0; j < cols; j++)
            {
                AddTextLabel(canvas, hours[j] + ":00", 
                    startX + (j * cellWidth) + cellWidth / 2 - 20, 
                    startY - 30, 11, Color.FromRgb(80, 80, 80), FontWeight.Bold);
            }

            // Draw row headers (days)
            for (int i = 0; i < rows; i++)
            {
                AddTextLabel(canvas, days[i], 
                    startX - 50, 
                    startY + (i * cellHeight) + cellHeight / 2 - 8, 
                    11, Color.FromRgb(80, 80, 80), FontWeight.Bold);
            }

            // Draw heatmap cells
            for (int i = 0; i < rows; i++)
            {
                for (int j = 0; j < cols; j++)
                {
                    double x = startX + (j * cellWidth);
                    double y = startY + (i * cellHeight);

                    // Calculate color intensity
                    double intensity = data[i, j] / maxValue;
                    Color cellColor = GetHeatColor(intensity);

                    var rect = new Rectangle
                    {
                        Width = cellWidth - 4,
                        Height = cellHeight - 4,
                        Fill = new SolidColorBrush(cellColor),
                        Stroke = new SolidColorBrush(Colors.White),
                        StrokeThickness = 2,
                        RadiusX = 4,
                        RadiusY = 4
                    };

                    Canvas.SetLeft(rect, x);
                    Canvas.SetTop(rect, y);
                    canvas.Children.Add(rect);
                    AnimationHelper.FadeIn(rect, (i * cols + j) * 20, 400);

                    // Value label
                    var valueLabel = new TextBlock
                    {
                        Text = $"{(int)data[i, j]}",
                        FontSize = 12,
                        FontWeight = FontWeight.Bold,
                        Foreground = new SolidColorBrush(
                            intensity > 0.5 ? Colors.White : Colors.Black)
                    };
                    Canvas.SetLeft(valueLabel, x + cellWidth / 2 - 10);
                    Canvas.SetTop(valueLabel, y + cellHeight / 2 - 8);
                    canvas.Children.Add(valueLabel);
                    AnimationHelper.FadeIn(valueLabel, (i * cols + j) * 20 + 200, 400);
                }
            }

            // Draw color legend
            DrawColorLegend(canvas, startX, startY + (rows * cellHeight) + 30);

            // Info
            AddTextLabel(canvas, "💡 Warna lebih terang = aktivitas lebih tinggi", 
                startX, canvas.Bounds.Height - 30, 11, Color.FromRgb(120, 120, 120));
        }

        /// <summary>
        /// Mendapatkan warna berdasarkan intensitas (0-1)
        /// Gradient dari biru (dingin) ke merah (panas)
        /// </summary>
        private Color GetHeatColor(double intensity)
        {
            if (intensity < 0.25)
            {
                // Blue to Cyan
                byte g = (byte)(intensity * 4 * 255);
                return Color.FromRgb(0, g, 255);
            }
            else if (intensity < 0.5)
            {
                // Cyan to Green
                byte b = (byte)((0.5 - intensity) * 4 * 255);
                return Color.FromRgb(0, 255, b);
            }
            else if (intensity < 0.75)
            {
                // Green to Yellow
                byte r = (byte)((intensity - 0.5) * 4 * 255);
                return Color.FromRgb(r, 255, 0);
            }
            else
            {
                // Yellow to Red
                byte g = (byte)((1.0 - intensity) * 4 * 255);
                return Color.FromRgb(255, g, 0);
            }
        }

        private void DrawColorLegend(Canvas canvas, double x, double y)
        {
            AddTextLabel(canvas, "Intensity Scale:", x, y, 11, Colors.Black, FontWeight.Bold);

            int segments = 10;
            double segmentWidth = 40;

            for (int i = 0; i < segments; i++)
            {
                double intensity = (double)i / segments;
                Color color = GetHeatColor(intensity);

                var rect = new Rectangle
                {
                    Width = segmentWidth,
                    Height = 20,
                    Fill = new SolidColorBrush(color)
                };
                Canvas.SetLeft(rect, x + 120 + (i * segmentWidth));
                Canvas.SetTop(rect, y - 5);
                canvas.Children.Add(rect);
            }

            AddTextLabel(canvas, "Low", x + 120, y + 20, 9, Color.FromRgb(100, 100, 100));
            AddTextLabel(canvas, "High", x + 120 + (segments * segmentWidth) - 20, y + 20, 
                9, Color.FromRgb(100, 100, 100));
        }
    }
}
