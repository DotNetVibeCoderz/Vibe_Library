using Lvgl.Ui;

namespace Lvgl.Tests;

public class CSharpUiGeneratorTests
{
    private static UiDocument Document() => new()
    {
        Name = "Dashboard",
        Width = 800,
        Height = 480,
        BackgroundColor = "#0F1720",
        Children =
        [
            new UiNode
            {
                Type = UiWidgetType.Panel,
                Name = "Card",
                Width = 400,
                Height = 200,
                Radius = 12,
                BackgroundColor = "#1A2430",
                Children =
                [
                    new UiNode
                    {
                        Type = UiWidgetType.Label,
                        Name = "TitleLabel",
                        Text = "Hello",
                        Align = LvAlign.TopMid,
                        FontSize = 20,
                    },
                    new UiNode
                    {
                        Type = UiWidgetType.Slider,
                        Name = "Level",
                        Minimum = 0,
                        Maximum = 100,
                        Value = 40,
                    },
                ],
            },
        ],
    };

    [Fact]
    public void Generates_a_partial_class_with_typed_properties()
    {
        var source = new CSharpUiGenerator { Namespace = "Demo.Ui" }.Generate(Document());

        Assert.Contains("namespace Demo.Ui;", source, StringComparison.Ordinal);
        Assert.Contains("public partial class Dashboard", source, StringComparison.Ordinal);
        Assert.Contains("public LvPanel Card { get; private set; } = null!;", source, StringComparison.Ordinal);
        Assert.Contains("public LvLabel TitleLabel { get; private set; } = null!;", source, StringComparison.Ordinal);
        Assert.Contains("public LvSlider Level { get; private set; } = null!;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Emits_the_hook_that_keeps_hand_written_code_safe()
    {
        var source = new CSharpUiGenerator().Generate(Document());

        // Regenerating must never clobber event wiring, which is why the generated half only
        // calls into a partial method the application implements.
        Assert.Contains("partial void OnBuilt();", source, StringComparison.Ordinal);
        Assert.Contains("OnBuilt();", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Parents_children_to_their_container()
    {
        var source = new CSharpUiGenerator().Generate(Document());

        Assert.Contains("var card = new LvPanel(Root);", source, StringComparison.Ordinal);
        Assert.Contains("new LvLabel(card, \"Hello\")", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Emits_ranges_and_values_for_range_widgets()
    {
        var source = new CSharpUiGenerator().Generate(Document());

        Assert.Contains(".SetRange(0, 100);", source, StringComparison.Ordinal);
        Assert.Contains(".SetValue(40, animate: false);", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Emits_alignment_only_when_the_node_uses_it()
    {
        var source = new CSharpUiGenerator().Generate(Document());

        Assert.Contains(".Align(LvAlign.TopMid, 0, 0);", source, StringComparison.Ordinal);
        Assert.Contains(".SetPosition(0, 0);", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Colours_become_packed_literals()
    {
        var source = new CSharpUiGenerator().Generate(Document());

        Assert.Contains("LvColor.FromRgb(0x1A2430u)", source, StringComparison.Ordinal);
        Assert.Contains("Root.SetBackgroundColor(LvColor.FromRgb(0x0F1720u));", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Local_variable_never_shadows_the_generated_property()
    {
        // A node named "card" would otherwise produce `var card = ...` alongside a property named
        // `card`, and the assignment back to the property would silently become a self-assignment.
        var document = new UiDocument
        {
            Children = [new UiNode { Type = UiWidgetType.Panel, Name = "card" }],
        };

        var source = new CSharpUiGenerator().Generate(document);

        Assert.Contains("var cardWidget = new LvPanel(Root);", source, StringComparison.Ordinal);
        Assert.Contains("card = cardWidget;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Text_is_escaped()
    {
        var document = new UiDocument
        {
            Children = [new UiNode { Type = UiWidgetType.Label, Text = "say \"hi\"\nagain" }],
        };

        var source = new CSharpUiGenerator().Generate(document);

        Assert.Contains("\"say \\\"hi\\\"\\nagain\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_to_generate_an_invalid_document()
    {
        var document = new UiDocument
        {
            Children =
            [
                new UiNode { Type = UiWidgetType.Label, Name = "Dup" },
                new UiNode { Type = UiWidgetType.Label, Name = "Dup" },
            ],
        };

        var error = Assert.Throws<InvalidOperationException>(() => new CSharpUiGenerator().Generate(document));
        Assert.Contains("Dup", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Unnamed_nodes_are_still_created_but_get_no_property()
    {
        var document = new UiDocument
        {
            Children = [new UiNode { Type = UiWidgetType.Label, Text = "anonymous" }],
        };

        var source = new CSharpUiGenerator().Generate(document);

        Assert.Contains("new LvLabel(Root, \"anonymous\")", source, StringComparison.Ordinal);

        // Root is the only generated property: the unnamed label gets a local variable and nothing else.
        var propertyCount = source.Split("{ get; private set; }", StringSplitOptions.None).Length - 1;
        Assert.Equal(1, propertyCount);
    }
}
