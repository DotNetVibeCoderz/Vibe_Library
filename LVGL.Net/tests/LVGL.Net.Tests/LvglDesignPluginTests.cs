using Lvgl.Assistant.Plugins;
using Lvgl.Ui;

namespace Lvgl.Tests;

/// <summary>
/// Covers the plugin that turns the model's design instructions into real layouts and code.
/// </summary>
/// <remarks>
/// The value of this plugin is that it validates against the actual <see cref="UiDocument"/> model
/// before answering, so a malformed layout comes back as a fixable list of problems instead of
/// reaching the user as plausible-looking JSON that will not open.
/// </remarks>
public class LvglDesignPluginTests
{
    private readonly LvglDesignPlugin _plugin = new();

    private const string ValidLayout = """
        {
          "Name": "Dashboard",
          "Width": 800,
          "Height": 480,
          "Children": [
            { "Type": "Label", "Name": "TitleLabel", "Text": "Hello", "FontSize": 20 },
            { "Type": "Button", "Name": "StartButton", "Text": "Start", "Width": 140, "Height": 44 }
          ]
        }
        """;

    [Fact]
    public void A_valid_layout_is_accepted_and_summarised()
    {
        var result = _plugin.CreateLayout(ValidLayout);

        Assert.Contains("is valid", result, StringComparison.Ordinal);
        Assert.Contains("TitleLabel", result, StringComparison.Ordinal);
        Assert.Contains("StartButton", result, StringComparison.Ordinal);
        Assert.NotNull(_plugin.LastDocument);
        Assert.Equal("Dashboard", _plugin.LastDocument!.Name);
    }

    [Fact]
    public void Producing_a_layout_raises_the_event_the_designer_listens_to()
    {
        UiDocument? produced = null;
        _plugin.DocumentProduced += (_, document) => produced = document;

        _plugin.CreateLayout(ValidLayout);

        Assert.NotNull(produced);
        Assert.Equal("Dashboard", produced!.Name);
    }

    [Fact]
    public void Malformed_json_returns_a_readable_error_not_an_exception()
    {
        var result = _plugin.CreateLayout("{ this is not json");

        Assert.Contains("not valid JSON", result, StringComparison.Ordinal);
        Assert.Null(_plugin.LastDocument);
    }

    [Fact]
    public void A_layout_with_duplicate_names_is_rejected_with_the_reason()
    {
        var result = _plugin.CreateLayout("""
            {
              "Name": "Broken",
              "Children": [
                { "Type": "Label", "Name": "Same" },
                { "Type": "Label", "Name": "Same" }
              ]
            }
            """);

        Assert.Contains("Problems", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("more than once", result, StringComparison.Ordinal);

        // A rejected layout must not become the one the designer offers to open.
        Assert.Null(_plugin.LastDocument);
    }

    [Fact]
    public void Validate_checks_without_keeping_the_document()
    {
        var result = _plugin.ValidateLayout(ValidLayout);

        Assert.Contains("valid", result, StringComparison.OrdinalIgnoreCase);
        Assert.Null(_plugin.LastDocument);
    }

    [Fact]
    public void Oversized_widgets_are_flagged_as_a_warning()
    {
        var result = _plugin.CreateLayout("""
            {
              "Name": "TooBig",
              "Width": 320,
              "Height": 240,
              "Children": [ { "Type": "Panel", "Name": "Huge", "Width": 900, "Height": 700 } ]
            }
            """);

        Assert.Contains("Warnings", result, StringComparison.Ordinal);
        Assert.Contains("Huge", result, StringComparison.Ordinal);
        Assert.Contains("900 wide", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Generated_csharp_matches_what_the_designer_would_export()
    {
        var source = _plugin.GenerateCSharp(ValidLayout, "Demo.Ui", "Dashboard");

        Assert.Contains("namespace Demo.Ui;", source, StringComparison.Ordinal);
        Assert.Contains("public partial class Dashboard", source, StringComparison.Ordinal);
        Assert.Contains("public LvLabel TitleLabel", source, StringComparison.Ordinal);
        Assert.Contains("partial void OnBuilt();", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Generated_event_handlers_cover_the_interactive_widgets()
    {
        var source = _plugin.GenerateEventHandlers(ValidLayout, "Demo.Ui");

        Assert.Contains("partial void OnBuilt()", source, StringComparison.Ordinal);
        Assert.Contains("StartButton.Clicked += OnStartButtonClicked;", source, StringComparison.Ordinal);
        Assert.Contains("private void OnStartButtonClicked(", source, StringComparison.Ordinal);

        // A label raises nothing, so it should not get a handler.
        Assert.DoesNotContain("TitleLabel.Clicked", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("blank")]
    [InlineData("dashboard")]
    [InlineData("form")]
    [InlineData("chart")]
    public void Every_template_produces_a_document_that_passes_validation(string kind)
    {
        // Templates are the shape the model starts from, so an invalid one would poison every
        // layout built on it.
        var json = _plugin.LayoutTemplate(kind, 800, 480);
        var document = UiJson.Parse(json);

        Assert.Empty(document.Validate());
    }

    [Fact]
    public void Template_dimensions_are_clamped_to_something_sane()
    {
        var json = _plugin.LayoutTemplate("dashboard", -5, 99999);
        var document = UiJson.Parse(json);

        Assert.InRange(document.Width, 64, 4096);
        Assert.InRange(document.Height, 64, 4096);
    }

    [Fact]
    public void DescribeWidgets_lists_every_supported_type()
    {
        var description = _plugin.DescribeWidgets();

        foreach (var type in Enum.GetValues<UiWidgetType>())
        {
            Assert.Contains(type.ToString(), description, StringComparison.Ordinal);
        }
    }
}
