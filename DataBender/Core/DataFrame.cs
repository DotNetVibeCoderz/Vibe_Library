using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DataBender.Core
{
    /// <summary>
    /// DataFrame: Two-dimensional labeled data structure (rows & columns).
    /// </summary>
    public class DataFrame
    {
        private readonly Dictionary<string, ISeries> _columns = new();
        public DbIndex Index { get; private set; }
        public List<string> ColumnNames => _columns.Keys.ToList();

        public DataFrame(DbIndex? index = null)
        {
            Index = index ?? new DbIndex(0);
        }

        public void AddColumn<T>(string name, IEnumerable<T?> data)
        {
            var listData = data.ToList();
            if (_columns.Count == 0)
                Index = new DbIndex(listData.Count);
            else if (listData.Count != Index.Length)
                throw new ArgumentException("Column length mismatch.");

            _columns[name] = new Series<T>(listData, name, Index);
        }

        public ISeries this[string columnName] => _columns[columnName];

        public int RowCount => Index.Length;
        public int ColumnCount => _columns.Count;

        public DataFrame Head(int n = 5)
        {
            int count = Math.Min(n, RowCount);
            var df = new DataFrame(new DbIndex(count));
            foreach (var col in _columns)
            {
                var slice = col.Value.GetValues().Take(count).ToList();
                df.AddColumn(col.Key, slice);
            }
            return df;
        }

        public DataFrame Filter(Func<int, bool> predicate)
        {
            var indices = Enumerable.Range(0, RowCount).Where(predicate).ToList();
            var newIdx = new DbIndex(indices.Select(i => Index[i]));
            var df = new DataFrame(newIdx);

            foreach (var col in _columns)
            {
                var filteredData = new List<object?>();
                foreach (var i in indices) filteredData.Add(col.Value.GetValue(i));
                df.AddColumn(col.Key, filteredData);
            }
            return df;
        }

        public void Print()
        {
            var sb = new StringBuilder();
            // Header
            sb.Append(string.Format("{0,-10}", "Index"));
            foreach (var col in ColumnNames) sb.Append(string.Format("| {0,-15}", col));
            sb.AppendLine("\n" + new string('-', 10 + ColumnNames.Count * 17));

            // Data
            for (int i = 0; i < Math.Min(RowCount, 20); i++)
            {
                sb.Append(string.Format("{0,-10}", Index[i]));
                foreach (var col in ColumnNames)
                {
                    var val = _columns[col].GetValue(i)?.ToString() ?? "NaN";
                    sb.Append(string.Format("| {0,-15}", val.Length > 15 ? val.Substring(0, 12) + "..." : val));
                }
                sb.AppendLine();
            }

            if (RowCount > 20) sb.AppendLine("...");
            Console.WriteLine(sb.ToString());
        }

        public static DataFrame FromCsv(string filePath)
        {
            var lines = System.IO.File.ReadAllLines(filePath);
            if (lines.Length == 0) return new DataFrame();

            var headers = lines[0].Split(',');
            var df = new DataFrame();
            var columnsData = headers.Select(_ => new List<string>()).ToList();

            for (int i = 1; i < lines.Length; i++)
            {
                var values = lines[i].Split(',');
                for (int j = 0; j < headers.Length; j++)
                    columnsData[j].Add(values[j]);
            }

            for (int i = 0; i < headers.Length; i++)
                df.AddColumn(headers[i], columnsData[i]);

            return df;
        }
        
        public void ToCsv(string filePath)
        {
            var lines = new List<string> { string.Join(",", ColumnNames) };
            for (int i = 0; i < RowCount; i++)
            {
                var row = ColumnNames.Select(col => _columns[col].GetValue(i)?.ToString() ?? "").ToArray();
                lines.Add(string.Join(",", row));
            }
            System.IO.File.WriteAllLines(filePath, lines);
        }
    }
}