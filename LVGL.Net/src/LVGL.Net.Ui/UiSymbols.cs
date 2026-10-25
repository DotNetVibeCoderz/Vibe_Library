using System.Collections.Frozen;
using System.Reflection;

namespace Lvgl.Ui;

/// <summary>
/// The <see cref="LvSymbols"/> glyphs by name, so a layout document can say <c>"Symbol": "Wifi"</c>.
/// </summary>
/// <remarks>
/// Built from <see cref="LvSymbols"/>' own properties rather than a second hand-kept list, so a
/// symbol added there is available to layouts without another edit. Reflecting over a type named
/// with <c>typeof</c> is something the trimmer can see, so this stays AOT-safe.
/// </remarks>
public static class UiSymbols
{
    private static readonly FrozenDictionary<string, string> Glyphs = typeof(LvSymbols)
        .GetProperties(BindingFlags.Public | BindingFlags.Static)
        .Where(property => property.PropertyType == typeof(string))
        .ToFrozenDictionary(property => property.Name, property => (string)property.GetValue(null)!, StringComparer.Ordinal);

    /// <summary>Every symbol name, sorted.</summary>
    public static IReadOnlyList<string> Names { get; } = [.. Glyphs.Keys.Order(StringComparer.Ordinal)];

    /// <summary>True when <paramref name="name"/> is an <see cref="LvSymbols"/> property.</summary>
    public static bool IsKnown(string name) => Glyphs.ContainsKey(name);

    /// <summary>The glyph string for a symbol name.</summary>
    /// <exception cref="KeyNotFoundException">The name is not a symbol.</exception>
    public static string Glyph(string name) =>
        Glyphs.TryGetValue(name, out var glyph) ? glyph : throw new KeyNotFoundException($"'{name}' is not one of LvSymbols.");
}
