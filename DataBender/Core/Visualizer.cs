using System;
using System.Collections.Generic;
using System.Linq;

namespace DataBender.Core
{
    /// <summary>
    /// Visualizer class for ASCII-based data visualization.
    /// Provides Bar Charts, Pie Charts, and Scatter Plots.
    /// </summary>
    public static class Visualizer
    {
        public static void BarChart(DataFrame df, string labelCol, string valCol)
        {
            Console.WriteLine($"\n--- Bar Chart: {valCol} by {labelCol} ---");
            for (int i = 0; i < df.RowCount; i++)
            {
                var label = df[labelCol][i]?.ToString() ?? "N/A";
                var val = Convert.ToDouble(df[valCol][i]);
                var barCount = (int)(val / 5);
                var bar = new string('█', Math.Max(0, barCount));
                Console.WriteLine($"{label.PadRight(12)} | {bar} ({val:F2})");
            }
        }

        public static void PieChart(DataFrame df, string labelCol, string valCol)
        {
            Console.WriteLine($"\n--- Pie Chart (Distribution): {valCol} by {labelCol} ---");
            double total = 0;
            for (int i = 0; i < df.RowCount; i++) total += Convert.ToDouble(df[valCol][i]);

            for (int i = 0; i < df.RowCount; i++)
            {
                var label = df[labelCol][i]?.ToString() ?? "N/A";
                var val = Convert.ToDouble(df[valCol][i]);
                double percent = (val / total) * 100;
                var bar = new string('░', (int)(percent / 2));
                Console.WriteLine($"{label.PadRight(12)} | {bar} {percent:F1}% ({val})");
            }
        }

        public static void ScatterPlot(DataFrame df, string xCol, string yCol, int width = 40, int height = 15)
        {
            Console.WriteLine($"\n--- Scatter Plot: {yCol} (Y) vs {xCol} (X) ---");
            
            var xValues = new List<double>();
            var yValues = new List<double>();
            for (int i = 0; i < df.RowCount; i++)
            {
                xValues.Add(Convert.ToDouble(df[xCol][i]));
                yValues.Add(Convert.ToDouble(df[yCol][i]));
            }

            double xMin = xValues.Min();
            double xMax = xValues.Max();
            double yMin = yValues.Min();
            double yMax = yValues.Max();

            char[,] grid = new char[height, width];
            for (int i = 0; i < height; i++)
                for (int j = 0; j < width; j++) grid[i, j] = ' ';

            for (int i = 0; i < xValues.Count; i++)
            {
                int xPos = (int)((xValues[i] - xMin) / (xMax - xMin + 0.000001) * (width - 1));
                int yPos = (int)((yValues[i] - yMin) / (yMax - yMin + 0.000001) * (height - 1));
                // Flip y for console coordinate (top-down)
                grid[height - 1 - yPos, xPos] = '●';
            }

            // Print Grid
            string border = "+" + new string('-', width) + "+";
            Console.WriteLine($"{yMax:F1} ^");
            for (int i = 0; i < height; i++)
            {
                Console.Write("     | ");
                for (int j = 0; j < width; j++) Console.Write(grid[i, j]);
                Console.WriteLine("|");
            }
            Console.WriteLine("     " + border);
            Console.WriteLine($"     {xMin:F1} " + new string(' ', width - 10) + $" {xMax:F1} > {xCol}");
        }

        public static void LineChart(DataFrame df, string valCol, int width = 50)
        {
            Console.WriteLine($"\n--- Simple Trend Line: {valCol} ---");
            var values = new List<double>();
            for (int i = 0; i < df.RowCount; i++) values.Add(Convert.ToDouble(df[valCol][i]));
            
            double max = values.Max();
            foreach (var v in values)
            {
                int len = (int)((v / max) * width);
                Console.WriteLine(new string('-', len) + "o " + v);
            }
        }
    }
}