using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace DataBender.Core
{
    /// <summary>
    /// DbIndex class to handle labels for Series and DataFrames.
    /// Changed name to DbIndex to avoid conflict with System.Index.
    /// </summary>
    public class DbIndex : IEnumerable<object>
    {
        private readonly List<object> _labels;

        public DbIndex(IEnumerable<object> labels)
        {
            _labels = labels.ToList();
        }

        public DbIndex(int length)
        {
            _labels = Enumerable.Range(0, length).Cast<object>().ToList();
        }

        public object this[int i] => _labels[i];
        public int Length => _labels.Count;

        public int GetIndex(object label) => _labels.IndexOf(label);

        public IEnumerator<object> GetEnumerator() => _labels.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}