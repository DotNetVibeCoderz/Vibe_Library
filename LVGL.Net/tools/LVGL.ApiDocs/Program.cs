using System.Reflection;
using Lvgl.ApiDocs;

// The packages whose public API is documented. LVGL.Net.Assistant is left out: it is the
// designer's AI backend, and its surface is mostly Semantic Kernel plumbing.
Assembly[] assemblies =
[
    typeof(Lvgl.LvglApplication).Assembly,
    typeof(Lvgl.Ui.UiDocument).Assembly,
    typeof(Lvgl.Sdl.SdlBackend).Assembly,
    typeof(Lvgl.Linux.FramebufferBackend).Assembly,
];

var output = args.Length > 0 ? args[0] : FindRepositoryDocs();
var files = ApiDocGenerator.Generate(assemblies, output);

Console.WriteLine($"Wrote {files.Count} files to {output}");
return 0;

static string FindRepositoryDocs()
{
    for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
    {
        if (File.Exists(Path.Combine(directory.FullName, "LVGL.Net.slnx"))) return Path.Combine(directory.FullName, "docs", "api");
    }

    throw new DirectoryNotFoundException("Run from inside the repository, or pass the output folder as the first argument.");
}
