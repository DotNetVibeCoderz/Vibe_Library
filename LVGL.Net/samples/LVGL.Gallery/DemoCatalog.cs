using Lvgl.Gallery.Shell;

namespace Lvgl.Gallery;

/// <summary>Every demo, in the order the gallery shows them.</summary>
internal static class DemoCatalog
{
    public static IReadOnlyList<Demo> Create() =>
    [
        new Scientific.FunctionPlotter(),
        new Scientific.Oscilloscope(),
        new Scientific.Mandelbrot(),
        new Work.Inventory(),
        new Work.TaskBoard(),
        new Work.SalesReport(),
        new Multimedia.MusicPlayer(),
        new Multimedia.Visualizer(),
        new Multimedia.PhotoLab(),
        new Games.Snake(),
        new Games.Game2048(),
        new Games.TicTacToe(),
        new Tools.Calculator(),
        new Tools.UnitConverter(),
        new Tools.Stopwatch(),
        new Home.SmartHome(),
        new Home.EnergyMonitor(),
    ];

    public static Demo? Find(IReadOnlyList<Demo> demos, string? name) =>
        name is null ? null : demos.FirstOrDefault(d => Slug(d.Title) == Slug(name) || d.GetType().Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public static string Slug(string text) =>
        new string(text.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Replace("--", "-").Trim('-');
}
