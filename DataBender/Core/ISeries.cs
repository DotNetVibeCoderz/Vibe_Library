using System;
using System.Collections;
using System.Collections.Generic;

namespace DataBender.Core
{
    /// <summary>
    /// Common interface for Series to allow non-generic access.
    /// </summary>
    public interface ISeries : IEnumerable
    {
        string Name { get; set; }
        DbIndex Index { get; }
        int Count { get; }
        object? GetValue(int index);
        IEnumerable<object?> GetValues();
        object? this[int index] { get; }
    }
}