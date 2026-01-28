using Avalonia.Controls;
using Avalonia.Media;
using System;
using System.Collections.Generic;

namespace D3Net.Charts
{
    /// <summary>
    /// Base class untuk semua jenis chart
    /// Menyediakan fungsi-fungsi helper untuk rendering
    /// </summary>
    public abstract class BaseChart
    {
        protected static Random random = new Random();
        
        /// <summary>
        /// Palet warna yang indah untuk visualisasi
        /// </summary>
        protected static readonly List<Color> ColorPalette = new List<Color>
        {
            Color.FromRgb(52, 152, 219),   // Blue
            Color.FromRgb(46, 204, 113),   // Green
            Color.FromRgb(231, 76, 60),    // Red
            Color.FromRgb(241, 196, 15),   // Yellow
            Color.FromRgb(155, 89, 182),   // Purple
            Color.FromRgb(52, 73, 94),     // Dark Blue
            Color.FromRgb(26, 188, 156),   // Turquoise
            Color.FromRgb(230, 126, 34),   // Orange
            Color.FromRgb(149, 165, 166),  // Gray
            Color.FromRgb(192, 57, 43)     // Dark Red
        };

        /// <summary>
        /// Method abstract untuk render chart ke canvas
        /// </summary>
        public abstract void Render(Canvas canvas);

        /// <summary>
        /// Generate data sample untuk demo
        /// </summary>
        protected List<double> GenerateSampleData(int count, double min, double max)
        {
            var data = new List<double>();
            for (int i = 0; i < count; i++)
            {
                data.Add(min + random.NextDouble() * (max - min));
            }
            return data;
        }

        /// <summary>
        /// Dapatkan warna dari palette berdasarkan index
        /// </summary>
        protected Color GetColor(int index)
        {
            return ColorPalette[index % ColorPalette.Count];
        }

        /// <summary>
        /// Tambahkan text label ke canvas
        /// </summary>
        protected void AddTextLabel(Canvas canvas, string text, double x, double y, 
            int fontSize = 12, Color? color = null, FontWeight? weight = null)
        {
            var textBlock = new TextBlock
            {
                Text = text,
                FontSize = fontSize,
                Foreground = new SolidColorBrush(color ?? Colors.Black),
                FontWeight = weight ?? FontWeight.Normal
            };
            Canvas.SetLeft(textBlock, x);
            Canvas.SetTop(textBlock, y);
            canvas.Children.Add(textBlock);
        }

        /// <summary>
        /// Dapatkan nilai maksimum dari list
        /// </summary>
        protected double GetMaxValue(List<double> values)
        {
            double max = double.MinValue;
            foreach (var val in values)
            {
                if (val > max) max = val;
            }
            return max;
        }
    }
}
