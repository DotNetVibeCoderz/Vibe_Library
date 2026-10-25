using Lvgl.ApiDocs;

namespace Lvgl.Tests;

/// <summary>Keeps docs/api in step with the code it documents.</summary>
public sealed class ApiReferenceTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("lvglnet-api-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); }
        catch (IOException) { }
    }

    [Fact]
    public void The_committed_reference_matches_the_public_api()
    {
        var committed = FindCommittedReference();
        if (committed is null) return;   // running from a package or an unusual layout

        ApiDocGenerator.Generate(
        [
            typeof(LvglApplication).Assembly,
            typeof(Ui.UiDocument).Assembly,
            typeof(Sdl.SdlBackend).Assembly,
            typeof(Linux.FramebufferBackend).Assembly,
        ], _folder);

        var stale = new List<string>();
        foreach (var generated in Directory.GetFiles(_folder, "*.md"))
        {
            var name = Path.GetFileName(generated);
            var existing = Path.Combine(committed, name);

            if (!File.Exists(existing) || Normalize(File.ReadAllText(existing)) != Normalize(File.ReadAllText(generated))) stale.Add(name);
        }

        Assert.True(stale.Count == 0,
            $"docs/api is out of date ({string.Join(", ", stale)}). Regenerate it: dotnet run --project tools/LVGL.ApiDocs");
    }

    [Fact]
    public void Doc_comments_render_as_markdown()
    {
        var xml = System.Xml.Linq.XElement.Parse(
            """<summary>Uses <c>LV_PCT</c>, returns <see langword="null"/> for <paramref name="text"/>.<para>Second.</para></summary>""");

        Assert.Equal("Uses `LV_PCT`, returns `null` for `text`.\n\nSecond.", ApiDocGenerator.Text(xml));
        Assert.Equal("lvtabviewsetactivetab", ApiDocGenerator.Anchor("LvTabView.SetActiveTab"));
    }

    private static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd();

    private static string? FindCommittedReference()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "docs", "api");
            if (File.Exists(Path.Combine(directory.FullName, "LVGL.Net.slnx")) && Directory.Exists(candidate)) return candidate;
        }

        return null;
    }
}
