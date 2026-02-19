using System;
using System.Collections.Generic;
using System.Linq;
using DataBender.Core;

namespace DataBender
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("=== DataBender: The Pandas Clone for .NET ===");
            Console.WriteLine("Created by Jacky the Code Bender @ Gravicode Studios");
            Console.WriteLine("============================================\n");

            // Menjalankan 20 Contoh Awal
            RunOriginal20Examples();

            // Menjalankan Contoh Visualisasi Baru
            RunNewVisualizationExamples();
            
            Console.WriteLine("\nSemua contoh selesai dijalankan! Jangan lupa traktir kopi/pulsa ya ke:");
            Console.WriteLine("https://studios.gravicode.com/products/budax");
        }

        static void RunOriginal20Examples()
        {
            Console.WriteLine("--- PART 1: 20 ORIGINAL EXAMPLES ---");

            // 1. Create a Series
            Console.WriteLine("Example 1: Creating a Series");
            var s = new Series<int>(new[] { 10, 20, 30, 40 }, "MySeries");
            Console.WriteLine(s);

            // 2. Series Math Operations
            Console.WriteLine("\nExample 2: Series Stats");
            Console.WriteLine($"Mean: {s.Mean()}, Sum: {s.Sum()}, Max: {s.Max()}");

            // 3. Create DataFrame manually
            Console.WriteLine("\nExample 3: Manual DataFrame");
            var df = new DataFrame();
            df.AddColumn("Name", new[] { "Jacky", "Fadhil", "Budi", "Siti" });
            df.AddColumn("Age", new[] { 25, 30, 22, 28 });
            df.AddColumn("Score", new[] { 85.5, 90.0, 78.5, 92.0 });
            df.Print();

            // 4. Accessing a Column
            Console.WriteLine("\nExample 4: Access Column");
            var ageCol = df["Age"];
            Console.WriteLine($"First Age: {ageCol[0]}");

            // 5. Head (Preview Data)
            Console.WriteLine("\nExample 5: DataFrame.Head(2)");
            df.Head(2).Print();

            // 6. Filtering Data (Where Age > 24)
            Console.WriteLine("\nExample 6: Filtering (Age > 24)");
            var filtered = df.Filter(i => Convert.ToInt32(df["Age"][i]) > 24);
            filtered.Print();

            // 7. Handling Missing Data (FillNa)
            Console.WriteLine("\nExample 7: Handling NaN");
            var sWithNulls = new Series<double?>(new double?[] { 1.0, null, 3.5, null }, "NullableSeries");
            var filled = sWithNulls.FillNa(0.0);
            Console.WriteLine(filled);

            // 8. Adding New Column based on logic
            Console.WriteLine("\nExample 8: Calculated Column (Score * 1.1)");
            var newScores = new List<double>();
            for(int i=0; i<df.RowCount; i++) newScores.Add(Convert.ToDouble(df["Score"][i]) * 1.1);
            df.AddColumn("BonusScore", newScores);
            df.Print();

            // 9. Data Input: Creating a CSV file then reading it
            Console.WriteLine("\nExample 9: I/O (Write/Read CSV)");
            df.ToCsv("data.csv");
            var loadedDf = DataFrame.FromCsv("data.csv");
            Console.WriteLine("Loaded from CSV:");
            loadedDf.Print();

            // 10. Time Series Simulation
            Console.WriteLine("\nExample 10: Time Indexing");
            var dates = Enumerable.Range(0, 5).Select(i => DateTime.Now.AddDays(i).ToShortDateString());
            var timeDf = new DataFrame();
            timeDf.AddColumn("Date", dates);
            timeDf.AddColumn("Value", new[] { 100, 105, 102, 110, 108 });
            timeDf.Print();

            // 11. Custom Index
            Console.WriteLine("\nExample 11: Custom Labeled Index");
            var customIdx = new DbIndex(new object[] { "A1", "A2", "A3", "A4" });
            var customDf = new DataFrame(customIdx);
            customDf.AddColumn("ID", new[] { 1, 2, 3, 4 });
            customDf.Print();

            // 12. Row-wise selection
            Console.WriteLine("\nExample 12: Row Selection via Index Label");
            int idx = customIdx.GetIndex("A3");
            Console.WriteLine($"Data at A3: {customDf["ID"][idx]}");

            // 13. Column Deletion Simulation
            Console.WriteLine("\nExample 13: Slicing (Drop Column Score)");
            var slicedDf = new DataFrame();
            slicedDf.AddColumn("Name", df["Name"].GetValues());
            slicedDf.AddColumn("Age", df["Age"].GetValues());
            slicedDf.Print();

            // 14. Descriptive Statistics on DataFrame
            Console.WriteLine("\nExample 14: Column Summary");
            var scoreSeries = (Series<double>)df["Score"];
            Console.WriteLine($"Average Score: {scoreSeries.Mean()}");

            // 15. Aggregation: Sum of a Column
            Console.WriteLine("\nExample 15: Aggregation");
            var ageSeries = (Series<int>)df["Age"];
            Console.WriteLine($"Total Age: {ageSeries.Sum()}");

            // 16. Sort Simulation
            Console.WriteLine("\nExample 16: Sort Simulation (Top 2 Scores)");
            var sortedIdx = Enumerable.Range(0, df.RowCount)
                             .OrderByDescending(i => Convert.ToDouble(df["Score"][i]))
                             .Take(2);
            Console.WriteLine("Top 2 performers:");
            foreach(var i in sortedIdx) Console.WriteLine($"{df["Name"][i]}: {df["Score"][i]}");

            // 17. Concatenation logic
            Console.WriteLine("\nExample 17: Concatenation (Adding a Row)");
            Console.WriteLine("Action: Row appended logic simulation.");

            // 18. Multi-type Series
            Console.WriteLine("\nExample 18: Object Series");
            var mix = new Series<object>(new object[] { "Text", 123, true }, "Mix");
            Console.WriteLine(mix);

            // 19. Simple Data Clean
            Console.WriteLine("\nExample 19: Cleaning (Filter Scores < 80)");
            var cleanDf = df.Filter(i => Convert.ToDouble(df["Score"][i]) >= 80);
            cleanDf.Print();

            // 20. Visualization (Original Bar Chart)
            Console.WriteLine("\nExample 20: Original Bar Chart Visualization");
            Visualizer.BarChart(df, "Name", "Score");
        }

        static void RunNewVisualizationExamples()
        {
            Console.WriteLine("\n--- PART 2: NEW ADVANCED VISUALIZATIONS ---");
            
            var df = new DataFrame();
            df.AddColumn("Dept", new[] { "Engineering", "Marketing", "HumanResources", "Sales", "Legal" });
            df.AddColumn("StaffCount", new[] { 55.0, 25.0, 10.0, 40.0, 15.0 });
            df.AddColumn("Expenses", new[] { 120.5, 60.2, 30.5, 95.8, 45.0 });

            // 21. Advanced Bar Chart
            Console.WriteLine("\nExample 21: New Bar Chart (Visualizer Class)");
            Visualizer.BarChart(df, "Dept", "Expenses");

            // 22. Pie Chart (Distribution)
            Console.WriteLine("\nExample 22: Pie Chart (Staff Distribution)");
            Visualizer.PieChart(df, "Dept", "StaffCount");

            // 23. Scatter Plot
            Console.WriteLine("\nExample 23: Scatter Plot (Revenue over Time)");
            var dfScatter = new DataFrame();
            dfScatter.AddColumn("Month", Enumerable.Range(1, 12).Cast<object>().ToArray());
            dfScatter.AddColumn("Revenue", new[] { 12.0, 18.0, 15.0, 28.0, 35.0, 42.0, 40.0, 58.0, 65.0, 72.0, 85.0, 100.0 });
            Visualizer.ScatterPlot(dfScatter, "Month", "Revenue");

            // 24. Line Chart
            Console.WriteLine("\nExample 24: Line Chart (Trend Analysis)");
            Visualizer.LineChart(dfScatter, "Revenue");
        }
    }
}