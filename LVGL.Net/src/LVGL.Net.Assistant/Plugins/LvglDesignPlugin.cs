using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Lvgl.Ui;
using Microsoft.SemanticKernel;

namespace Lvgl.Assistant.Plugins;

/// <summary>
/// Turns design instructions into real LVGL layouts and .NET code.
/// </summary>
/// <remarks>
/// <para>
/// This is the plugin that makes the assistant more than a chat window. Rather than asking the
/// model to hand-write a layout into a code block - where a typo or a wrong enum name is only
/// discovered when the user tries to open it - the model emits a document and this plugin
/// <b>validates it against the real <see cref="UiDocument"/> model</b> before answering. An invalid
/// layout comes back as a list of problems the model can fix on the next turn.
/// </para>
/// <para>
/// Code generation goes through the same <see cref="CSharpUiGenerator"/> the designer's Export C#
/// button uses, so what the assistant produces and what the designer produces cannot diverge.
/// </para>
/// </remarks>
public sealed class LvglDesignPlugin
{
    /// <summary>The last layout the model produced, for the designer to open.</summary>
    public UiDocument? LastDocument { get; private set; }

    /// <summary>Raised when a layout is produced or changed, so the UI can offer to open it.</summary>
    public event EventHandler<UiDocument>? DocumentProduced;

    [KernelFunction("describe_widgets")]
    [Description(
        "Lists the LVGL widget types available in a layout document, with the properties each one " +
        "understands. Call this before designing a layout if you are unsure what is supported.")]
    public string DescribeWidgets()
    {
        var builder = new StringBuilder();
        builder.AppendLine("Widget types for the 'Type' field, and the properties each uses:");
        builder.AppendLine();

        foreach (var type in Enum.GetValues<UiWidgetType>())
        {
            builder.Append("- ").Append(type).Append(": ").AppendLine(DescribeType(type));
        }

        builder.AppendLine();
        builder.AppendLine(
            """
            Every widget also accepts: Name (a valid C# identifier, unique in the document),
            X, Y, Width, Height, Align, BackgroundColor, TextColor, BorderColor, BorderWidth,
            Radius, Padding, FontSize, Hidden, and Children (Panel only).

            POSITIONING - the one thing to get right:
              * WITHOUT Align, X and Y are absolute coordinates from the top left.
              * WITH Align, X and Y are OFFSETS FROM THE ANCHOR, not absolute coordinates.
                Align="Center" with X=0, Y=0 is dead centre. Align="TopMid" with X=240 on a
                480-wide screen puts the widget 240px RIGHT OF CENTRE, i.e. off the screen.
              Pick one scheme per widget. If you are centring something, use Align and leave
              X and Y at 0 or a small nudge. If you are laying things out on a grid, drop Align
              and use real coordinates.

            Colours are "#RRGGBB". Font sizes are 12, 14, 16, 20, 24, 28 or 36 - other values fall
            back to the default. Align is an LvAlign name such as Center, TopLeft, BottomMid.
            Sizes are pixels: work out percentages yourself against the screen size rather than
            trying to express them here.
            """);

        return builder.ToString();
    }

    [KernelFunction("create_layout")]
    [Description(
        "Validates a layout document and makes it available for the user to open in the designer. " +
        "Pass the complete document as JSON. Returns the problems found, or a confirmation and a " +
        "summary of what was created. Always call this instead of only printing JSON in your reply.")]
    public string CreateLayout(
        [Description("The complete layout document as JSON, in .lvgl.json format.")] string documentJson)
    {
        UiDocument document;

        try
        {
            document = UiJson.Parse(documentJson);
        }
        catch (JsonException ex)
        {
            return $"That is not valid JSON: {ex.Message}\n\nCall describe_widgets if you need the schema.";
        }
        catch (InvalidDataException ex)
        {
            return $"The document could not be read: {ex.Message}";
        }

        var problems = document.Validate();
        if (problems.Count > 0)
        {
            return "The layout has problems that must be fixed before it can be used:\n" +
                   string.Join("\n", problems.Select(p => "- " + p));
        }

        LastDocument = document;
        DocumentProduced?.Invoke(this, document);

        return Summarize(document);
    }

    [KernelFunction("validate_layout")]
    [Description(
        "Checks a layout document without keeping it. Use to review a document the user pasted in.")]
    public string ValidateLayout(
        [Description("The layout document as JSON.")] string documentJson)
    {
        try
        {
            var document = UiJson.Parse(documentJson);
            var problems = document.Validate();

            return problems.Count == 0
                ? "The layout is valid.\n\n" + Summarize(document)
                : "Problems found:\n" + string.Join("\n", problems.Select(p => "- " + p));
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        {
            return $"The document could not be read: {ex.Message}";
        }
    }

    [KernelFunction("generate_csharp")]
    [Description(
        "Generates the C# partial class that builds a layout, using the same generator as the " +
        "designer's Export C# button. Returns compilable source.")]
    public string GenerateCSharp(
        [Description("The layout document as JSON.")] string documentJson,
        [Description("Namespace for the generated class. Empty for none.")] string @namespace = "",
        [Description("Class name. Empty uses the document name.")] string className = "")
    {
        try
        {
            var document = UiJson.Parse(documentJson);

            var generator = new CSharpUiGenerator
            {
                Namespace = string.IsNullOrWhiteSpace(@namespace) ? null : @namespace.Trim(),
                ClassName = string.IsNullOrWhiteSpace(className) ? null : className.Trim(),
            };

            return generator.Generate(document);
        }
        catch (InvalidOperationException ex)
        {
            // Thrown by the generator when the document would not produce valid code.
            return ex.Message;
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        {
            return $"The document could not be read: {ex.Message}";
        }
    }

    [KernelFunction("generate_event_handlers")]
    [Description(
        "Generates the hand-written half of the partial class - the OnBuilt hook with an event " +
        "handler stub for every named widget that can raise one. Pass the SAME className you " +
        "passed to generate_csharp, or the two halves will not match.")]
    public string GenerateEventHandlers(
        [Description("The layout document as JSON.")] string documentJson,
        [Description("Namespace for the class. Empty for none.")] string @namespace = "",
        [Description("Class name. Must match the one used for generate_csharp. Empty uses the document name.")]
        string className = "")
    {
        UiDocument document;

        try
        {
            document = UiJson.Parse(documentJson);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        {
            return $"The document could not be read: {ex.Message}";
        }

        // Defaulting to the document name is what made the two halves disagree when the model
        // named the class on one call and not the other.
        var resolvedClassName = Sanitize(string.IsNullOrWhiteSpace(className) ? document.Name : className);
        var builder = new StringBuilder();

        builder.AppendLine("using Lvgl;");
        builder.AppendLine("using Lvgl.Widgets;");
        builder.AppendLine();

        if (!string.IsNullOrWhiteSpace(@namespace))
        {
            builder.AppendLine($"namespace {@namespace.Trim()};").AppendLine();
        }

        builder.AppendLine($"// The hand-written half of {resolvedClassName}. Regenerating the layout never touches this file.");
        builder.AppendLine($"public partial class {resolvedClassName}");
        builder.AppendLine("{");
        builder.AppendLine("    partial void OnBuilt()");
        builder.AppendLine("    {");

        var wired = 0;
        foreach (var node in document.Descendants().Where(n => !string.IsNullOrEmpty(n.Name)))
        {
            var line = EventWiringFor(node);
            if (line is null) continue;

            builder.AppendLine("        " + line);
            wired++;
        }

        if (wired == 0)
        {
            builder.AppendLine("        // No named interactive widgets in this layout yet.");
        }

        builder.AppendLine("    }");

        foreach (var node in document.Descendants().Where(n => !string.IsNullOrEmpty(n.Name)))
        {
            var handler = HandlerFor(node);
            if (handler is null) continue;

            builder.AppendLine();
            builder.Append(handler);
        }

        builder.AppendLine("}");
        return builder.ToString();
    }

    private static string? EventWiringFor(UiNode node) => node.Type switch
    {
        UiWidgetType.Button => $"{node.Name}.Clicked += On{node.Name}Clicked;",
        UiWidgetType.Slider or UiWidgetType.Arc or UiWidgetType.Switch or UiWidgetType.Checkbox
            or UiWidgetType.Dropdown or UiWidgetType.Roller =>
            $"{node.Name}.ValueChanged += On{node.Name}Changed;",
        _ => null,
    };

    private static string? HandlerFor(UiNode node)
    {
        var body = node.Type switch
        {
            UiWidgetType.Button =>
                $"    private void On{node.Name}Clicked(object? sender, LvEventArgs e)\n" +
                "    {\n        // TODO: handle the click.\n    }\n",

            UiWidgetType.Slider =>
                $"    private void On{node.Name}Changed(object? sender, LvEventArgs e)\n" +
                $"    {{\n        var value = {node.Name}.Value;\n        // TODO: use the new value.\n    }}\n",

            UiWidgetType.Arc =>
                $"    private void On{node.Name}Changed(object? sender, LvEventArgs e)\n" +
                $"    {{\n        var value = {node.Name}.Value;\n        // TODO: use the new value.\n    }}\n",

            UiWidgetType.Switch =>
                $"    private void On{node.Name}Changed(object? sender, LvEventArgs e)\n" +
                $"    {{\n        var isOn = {node.Name}.IsOn;\n        // TODO: react to the toggle.\n    }}\n",

            UiWidgetType.Checkbox =>
                $"    private void On{node.Name}Changed(object? sender, LvEventArgs e)\n" +
                $"    {{\n        var isChecked = {node.Name}.IsChecked;\n        // TODO: react to the toggle.\n    }}\n",

            UiWidgetType.Dropdown =>
                $"    private void On{node.Name}Changed(object? sender, LvEventArgs e)\n" +
                $"    {{\n        var selected = {node.Name}.SelectedOption;\n        // TODO: react to the selection.\n    }}\n",

            UiWidgetType.Roller =>
                $"    private void On{node.Name}Changed(object? sender, LvEventArgs e)\n" +
                $"    {{\n        var selected = {node.Name}.SelectedOption;\n        // TODO: react to the selection.\n    }}\n",

            _ => null,
        };

        return body;
    }

    [KernelFunction("layout_template")]
    [Description(
        "Returns a small, valid layout document to start from. Kinds: 'blank', 'dashboard', " +
        "'form', 'chart'. Use this as the shape to modify rather than inventing one.")]
    public string LayoutTemplate(
        [Description("Which template: blank, dashboard, form or chart.")] string kind = "blank",
        [Description("Screen width in pixels.")] int width = 800,
        [Description("Screen height in pixels.")] int height = 480)
    {
        width = Math.Clamp(width, 64, 4096);
        height = Math.Clamp(height, 64, 4096);

        var document = kind.Trim().ToLowerInvariant() switch
        {
            "dashboard" => DashboardTemplate(width, height),
            "form" => FormTemplate(width, height),
            "chart" => ChartTemplate(width, height),
            _ => new UiDocument { Name = "MainScreen", Width = width, Height = height, BackgroundColor = "#0F1720" },
        };

        return UiJson.ToJson(document);
    }

    private static UiDocument DashboardTemplate(int width, int height)
    {
        var cardWidth = (width - 48) / 3;

        return new UiDocument
        {
            Name = "Dashboard",
            Width = width,
            Height = height,
            BackgroundColor = "#0F1720",
            Children =
            [
                new UiNode
                {
                    Type = UiWidgetType.Label, Name = "TitleLabel", Text = "Dashboard",
                    Align = LvAlign.TopLeft, X = 16, Y = 14, TextColor = "#E6EDF3", FontSize = 20,
                },
                new UiNode
                {
                    Type = UiWidgetType.Panel, Name = "CardOne",
                    X = 16, Y = 56, Width = cardWidth, Height = 120,
                    BackgroundColor = "#1A2430", Radius = 12, BorderWidth = 0, Padding = 12,
                    Children =
                    [
                        new UiNode { Type = UiWidgetType.Label, Name = "CardOneValue", Text = "0", FontSize = 28, TextColor = "#E6EDF3" },
                    ],
                },
                new UiNode
                {
                    Type = UiWidgetType.Chart, Name = "Trend",
                    X = 16, Y = 190, Width = width - 32, Height = height - 210,
                    ChartType = LvChartType.Line, PointCount = 60, Minimum = 0, Maximum = 100,
                    SeriesColors = ["#38BDF8"], BackgroundColor = "#111A26", Radius = 10, BorderWidth = 0,
                },
            ],
        };
    }

    private static UiDocument FormTemplate(int width, int height) => new()
    {
        Name = "SettingsForm",
        Width = width,
        Height = height,
        BackgroundColor = "#0F1720",
        Children =
        [
            new UiNode { Type = UiWidgetType.Label, Name = "FormTitle", Text = "Settings", Align = LvAlign.TopMid, Y = 16, FontSize = 20, TextColor = "#E6EDF3" },
            new UiNode { Type = UiWidgetType.TextArea, Name = "NameField", X = 24, Y = 64, Width = width - 48, Height = 44 },
            new UiNode { Type = UiWidgetType.Dropdown, Name = "ModeField", X = 24, Y = 124, Width = 220, Options = ["DHCP", "Static"] },
            new UiNode { Type = UiWidgetType.Button, Name = "SaveButton", Text = "Save", Align = LvAlign.BottomRight, X = -24, Y = -20, Width = 120, Height = 44, Radius = 8, BackgroundColor = "#38BDF8", TextColor = "#0F1720" },
            new UiNode { Type = UiWidgetType.Button, Name = "CancelButton", Text = "Cancel", Align = LvAlign.BottomRight, X = -156, Y = -20, Width = 120, Height = 44, Radius = 8 },
        ],
    };

    private static UiDocument ChartTemplate(int width, int height) => new()
    {
        Name = "TrendScreen",
        Width = width,
        Height = height,
        BackgroundColor = "#0F1720",
        Children =
        [
            new UiNode { Type = UiWidgetType.Label, Name = "ChartTitle", Text = "Live trend", Align = LvAlign.TopLeft, X = 16, Y = 14, FontSize = 20, TextColor = "#E6EDF3" },
            new UiNode
            {
                Type = UiWidgetType.Chart, Name = "MainChart",
                X = 16, Y = 56, Width = width - 32, Height = height - 80,
                ChartType = LvChartType.Line, PointCount = 90, Minimum = 0, Maximum = 100,
                SeriesColors = ["#38BDF8", "#FB7185"], BackgroundColor = "#111A26", Radius = 10, BorderWidth = 0,
            },
        ],
    };

    private static string Summarize(UiDocument document)
    {
        var nodes = document.Descendants().ToList();
        var named = nodes.Where(n => !string.IsNullOrEmpty(n.Name)).ToList();

        var builder = new StringBuilder();
        builder.AppendLine($"Layout '{document.Name}' is valid: {document.Width}x{document.Height}, {nodes.Count} widgets.");

        var byType = nodes.GroupBy(n => n.Type).OrderByDescending(g => g.Count());
        builder.AppendLine("Composition: " + string.Join(", ", byType.Select(g => $"{g.Count()} {g.Key}")));

        if (named.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Named widgets, which become properties on the generated class:");
            foreach (var node in named)
            {
                builder.AppendLine($"- {node.Name} ({node.Type})");
            }
        }

        // Advisories are fed back so the model can correct itself on the next turn rather than
        // handing the user a layout that opens but looks wrong.
        var warnings = document.Warnings();
        if (warnings.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Warnings - fix these and call create_layout again:");
            foreach (var warning in warnings) builder.AppendLine("- " + warning);
        }

        return builder.ToString();
    }

    private static string DescribeType(UiWidgetType type) => type switch
    {
        UiWidgetType.Panel => "container; the only type that accepts Children",
        UiWidgetType.Label => "text; uses Text and FontSize",
        UiWidgetType.Button => "button; uses Text, and Value=1 for a latching toggle",
        UiWidgetType.Slider => "draggable slider; uses Minimum, Maximum, Value",
        UiWidgetType.Bar => "read-only progress bar; uses Minimum, Maximum, Value",
        UiWidgetType.Arc => "circular gauge or dial; uses Minimum, Maximum, Value",
        UiWidgetType.Switch => "on/off switch; Value=1 starts it on",
        UiWidgetType.Checkbox => "checkbox with a caption; uses Text, Value=1 starts it checked",
        UiWidgetType.Dropdown => "drop-down list; uses Options",
        UiWidgetType.Roller => "scrolling picker, good for touch; uses Options",
        UiWidgetType.TextArea => "editable text field; uses Text",
        UiWidgetType.Chart => "line/bar/scatter chart; uses ChartType, PointCount, Minimum, Maximum, SeriesColors",
        _ => "widget",
    };

    private static string Sanitize(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            if (char.IsLetterOrDigit(c) || c == '_') builder.Append(c);
        }

        if (builder.Length == 0) return "Screen";
        if (char.IsDigit(builder[0])) builder.Insert(0, '_');

        return builder.ToString();
    }
}
