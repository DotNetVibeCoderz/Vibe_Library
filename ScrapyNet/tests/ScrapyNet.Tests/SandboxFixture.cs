using ScrapyNet.Sandbox;
using Xunit;

[assembly: AssemblyFixture(typeof(ScrapyNet.Tests.SandboxFixture))]

namespace ScrapyNet.Tests;

/// <summary>One sandbox site shared by every test in the assembly.</summary>
public sealed class SandboxFixture : IAsyncLifetime
{
    public SandboxSite Site { get; private set; } = null!;

    public string Url(string path) => Site.Url(path);

    public async ValueTask InitializeAsync() => Site = await SandboxSite.StartAsync();

    public async ValueTask DisposeAsync() => await Site.DisposeAsync();
}

/// <summary>Quiet settings for tests: warnings only, no stats dump.</summary>
public static class TestSettings
{
    public static void Quiet(Settings s)
    {
        s.Set(SettingKeys.LogLevel, "WARNING");
        s.Set(SettingKeys.StatsDump, false);
        s.Set(SettingKeys.LogStatsInterval, 0);
    }

    public static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "scrapynet-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
