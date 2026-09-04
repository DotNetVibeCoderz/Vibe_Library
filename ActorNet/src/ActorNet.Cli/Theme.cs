// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using Spectre.Console;

namespace ActorNet.Cli;

/// <summary>
/// One place for the console's visual language, so every command looks like part of the same tool.
/// </summary>
/// <remarks>
/// The palette is cool cyan and slate with a single warm accent for anything that needs attention.
/// Alarms and failures are the only red in the tool, which is what makes them readable at a glance
/// in a scrolling log.
/// </remarks>
internal static class Theme
{
    public const string Accent = "#38bdf8";      // Sky - headings, the product's own voice.
    public const string AccentDim = "#0ea5e9";
    public const string Good = "#4ade80";        // Green - a thing that worked.
    public const string Warn = "#fbbf24";        // Amber - a thing that needs a look.
    public const string Bad = "#f87171";         // Red - a failure. Used sparingly on purpose.
    public const string Muted = "#64748b";       // Slate - labels, units, chrome.
    public const string Text = "#e2e8f0";

    /// <summary>The product banner, shown once per interactive session.</summary>
    public static void Banner()
    {
        AnsiConsole.Write(new FigletText("ActorNet").Color(Color.FromHex(Accent)));
        AnsiConsole.Write(new Markup(
            $"[{Muted}]Hybrid actor framework for .NET - virtual actors, supervision, clustering.[/]\n" +
            $"[{Muted}]Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.[/]\n\n"));
    }

    /// <summary>A section heading.</summary>
    public static void Rule(string title) =>
        AnsiConsole.Write(new Rule($"[{Accent}]{title}[/]").RuleStyle(Style.Parse(Muted)).LeftJustified());

    public static void Success(string message) => AnsiConsole.MarkupLine($"[{Good}]OK[/]  {message}");

    public static void Info(string message) => AnsiConsole.MarkupLine($"[{Accent}]->[/]  {message}");

    public static void Caution(string message) => AnsiConsole.MarkupLine($"[{Warn}]!![/]  {message}");

    public static void Fail(string message) => AnsiConsole.MarkupLine($"[{Bad}]xx[/]  {message}");

    /// <summary>A key/value table with no borders, for reporting settings and results.</summary>
    public static Table Facts(string? title = null)
    {
        var table = new Table().Border(TableBorder.None).HideHeaders();
        table.AddColumn(new TableColumn(string.Empty).PadRight(3));
        table.AddColumn(new TableColumn(string.Empty));
        if (title is not null) table.Title = new TableTitle(title, Style.Parse(Accent));
        return table;
    }

    /// <summary>Adds a labelled row to a <see cref="Facts"/> table.</summary>
    public static Table Fact(this Table table, string label, string value)
    {
        table.AddRow($"[{Muted}]{label}[/]", $"[{Text}]{value}[/]");
        return table;
    }

    /// <summary>A bordered table for lists of records.</summary>
    public static Table Grid(params string[] columns)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.FromHex(Muted));

        foreach (var column in columns) table.AddColumn(new TableColumn($"[{Accent}]{column}[/]"));
        return table;
    }

    /// <summary>Escapes a value that may contain Spectre markup characters.</summary>
    public static string Safe(this string value) => Markup.Escape(value);
}
