using System.Text.Json;
using System.Text.RegularExpressions;
using Lvgl.Ui;

namespace Lvgl.Tests;

/// <summary>
/// Cheap checks on the dotnet new templates. The end-to-end proof - pack, create, build, render -
/// is templates/test-templates.sh, which CI runs; these catch the mistakes it would only find slowly.
/// </summary>
public sealed partial class TemplateTests
{
    [Fact]
    public void The_app_template_references_the_version_being_built()
    {
        var root = FindRoot();
        if (root is null) return;   // running from a package or an unusual layout

        var props = File.ReadAllText(Path.Combine(root, "Directory.Build.props"));
        var version = VersionElement().Match(props).Groups[1].Value;

        using var config = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(root, "templates", "content", "lvgl-app", ".template.config", "template.json")));
        var templateDefault = config.RootElement.GetProperty("symbols").GetProperty("LvglNetVersion").GetProperty("defaultValue").GetString();

        Assert.True(version == templateDefault,
            $"Directory.Build.props is {version} but the app template creates projects referencing {templateDefault}. " +
            "Update defaultValue of LvglNetVersion in templates/content/lvgl-app/.template.config/template.json.");
    }

    [Theory]
    [InlineData("lvgl-app/Screens/MainScreen.lvgl.json", new[] { "LevelSlider", "LevelBar", "LevelValue", "PowerSwitch", "PowerLed", "ClockLabel" })]
    [InlineData("lvgl-screen/Screens/ScreenName.lvgl.json", new[] { "TitleLabel", "BackButton" })]
    public void Template_layouts_are_valid_and_name_what_the_code_finds(string relative, string[] names)
    {
        var root = FindRoot();
        if (root is null) return;

        // The app layout carries the size as replacement tokens; substitute what dotnet new would.
        var json = File.ReadAllText(Path.Combine(root, "templates", "content", relative))
            .Replace("DESIGN_WIDTH", "800", StringComparison.Ordinal)
            .Replace("DESIGN_HEIGHT", "480", StringComparison.Ordinal);
        var document = UiJson.Parse(json);

        Assert.Empty(document.Validate());
        foreach (var name in names) Assert.NotNull(document.Find(name));
    }

    private static string? FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LVGL.Net.slnx")) && Directory.Exists(Path.Combine(directory.FullName, "templates")))
                return directory.FullName;
        }

        return null;
    }

    [GeneratedRegex("<Version>([^<]+)</Version>")]
    private static partial Regex VersionElement();
}
