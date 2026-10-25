using Lvgl.Assistant.Plugins;
using Lvgl.Ui;
using Lvgl.Widgets;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Lvgl.Tests;

/// <summary>
/// Compiles what the code generator emits against the real LVGL.Net assembly.
/// </summary>
/// <remarks>
/// String assertions on generated code prove a line is present, not that the file builds. A wrong
/// member name, a missing cast or a type the generator assumes but the library renamed would pass
/// every <c>Assert.Contains</c> and fail in the user's project. Compiling closes that gap - and
/// compiling the generated half together with the assistant's handler half proves the two partial
/// classes still fit each other.
/// </remarks>
public class GeneratedCodeCompilesTests
{
    [Fact]
    public void The_kitchen_sink_layout_compiles()
    {
        var document = TestLayouts.KitchenSink(fontAsset: "assets/brand.ttf", imageAsset: "assets/logo.png");
        var source = new CSharpUiGenerator { Namespace = "Demo.Ui" }.Generate(document);

        AssertCompiles(source);
    }

    [Fact]
    public void The_generated_half_and_the_handler_half_compile_together()
    {
        var document = TestLayouts.KitchenSink();
        var json = UiJson.ToJson(document);

        var generated = new CSharpUiGenerator { Namespace = "Demo.Ui" }.Generate(document);
        var handlers = new LvglDesignPlugin().GenerateEventHandlers(json, "Demo.Ui", document.Name);

        AssertCompiles(generated, handlers);
    }

    [Fact]
    public void A_layout_without_styles_or_assets_compiles_without_their_helpers()
    {
        var document = new UiDocument
        {
            Name = "Plain",
            Children = [new UiNode { Type = UiWidgetType.Label, Name = "Hello", Text = "hi" }],
        };

        var source = new CSharpUiGenerator().Generate(document);

        Assert.DoesNotContain("AssetDirectory", source, StringComparison.Ordinal);
        Assert.DoesNotContain("using Lvgl.Styling;", source, StringComparison.Ordinal);
        AssertCompiles(source);
    }

    [Theory]
    [MemberData(nameof(EveryWidgetType))]
    public void Each_widget_type_compiles_on_its_own(UiWidgetType type)
    {
        // Tabs and tiles need their host; every other kind is placed straight on the screen.
        var node = new UiNode { Type = type, Name = "Subject", Text = "x", Options = ["a", "b"] };
        var root = type switch
        {
            UiWidgetType.Tab => new UiNode { Type = UiWidgetType.TabView, Name = "Host", Children = [node] },
            UiWidgetType.Tile => new UiNode { Type = UiWidgetType.TileView, Name = "Host", Children = [node] },
            _ => node,
        };

        var source = new CSharpUiGenerator().Generate(new UiDocument { Name = "Single", Children = [root] });

        AssertCompiles(source);
        Assert.Contains($"public {CSharpUiGenerator.TypeName(type)} Subject", source, StringComparison.Ordinal);
    }

    public static TheoryData<UiWidgetType> EveryWidgetType()
    {
        var data = new TheoryData<UiWidgetType>();
        foreach (var type in Enum.GetValues<UiWidgetType>()) data.Add(type);
        return data;
    }

    private static void AssertCompiles(params string[] sources)
    {
        var trees = sources.Select(source => CSharpSyntaxTree.ParseText(
            source, new CSharpParseOptions(LanguageVersion.Latest))).ToArray();

        var compilation = CSharpCompilation.Create(
            "GeneratedLayout",
            trees,
            References(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var errors = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"{d.Id} at {d.Location.GetLineSpan().StartLinePosition}: {d.GetMessage()}")
            .ToList();

        Assert.True(errors.Count == 0,
            "The generated code does not compile:" + Environment.NewLine + string.Join(Environment.NewLine, errors) +
            Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine + "-----" + Environment.NewLine, sources));
    }

    private static IEnumerable<MetadataReference> References()
    {
        var paths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        paths.Add(typeof(LvObject).Assembly.Location);

        return paths.Select(path => MetadataReference.CreateFromFile(path));
    }
}
