using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace DataBender.Core
{
    /// <summary>
    /// Series: A one-dimensional labeled array.
    /// </summary>
    public class Series<T> : ISeries, IEnumerable<T>
    {
        public string Name { get; set; }
        public DbIndex Index { get; private set; }
        private readonly List<T?> _data;

        public Series(IEnumerable<T?> data, string name = "", DbIndex? index = null)
        {
            _data = data.ToList();
            Name = name;
            Index = index ?? new DbIndex(_data.Count);
            
            if (Index.Length != _data.Count)
                throw new ArgumentException("Index length must match data length.");
        }

        public T? this[int i]
        {
            get => _data[i];
            set => _data[i] = value;
        }

        object? ISeries.this[int i] => _data[i];

        public int Count => _data.Count;

        public object? GetValue(int index) => _data[index];
        public IEnumerable<object?> GetValues() => _data.Cast<object?>();

        // Statistics
        public double Mean() => _data.OfType<IConvertible>().Select(Convert.ToDouble).DefaultIfEmpty(0).Average();
        public double Sum() => _data.OfType<IConvertible>().Select(Convert.ToDouble).Sum();
        public double Max() => _data.OfType<IConvertible>().Select(Convert.ToDouble).DefaultIfEmpty(0).Max();
        public double Min() => _data.OfType<IConvertible>().Select(Convert.ToDouble).DefaultIfEmpty(0).Min();

        public Series<T> FillNa(T value)
        {
            var newData = _data.Select(x => x == null ? value : x).ToList();
            return new Series<T>(newData, Name, Index);
        }

        public IEnumerator<T> GetEnumerator() => _data.GetEnumerator()!;
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public override string ToString()
        {
            var res = $"Series: {Name}\n";
            for (int i = 0; i < Math.Min(Count, 10); i++)
                res += $"{Index[i]}: {(_data[i]?.ToString() ?? "NaN")}\n";
            if (Count > 10) res += "...";
            return res;
        }
    }
}