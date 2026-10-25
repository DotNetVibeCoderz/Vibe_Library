using Lvgl;
using Lvgl.Ui;
using Lvgl.Widgets;

namespace ScreenNamespace;

/// <summary>
/// The ScreenName screen. Its layout lives in <c>Screens/ScreenName.lvgl.json</c> - open it in the
/// LVGL.Net designer to change it - and this class wires behaviour to the named widgets.
/// </summary>
/// <remarks>
/// Needs the LVGL.Net.Ui package, and the layout copied to the output folder:
/// <c>&lt;None Update="Screens/*.lvgl.json" CopyToOutputDirectory="PreserveNewest" /&gt;</c>.
/// </remarks>
internal sealed class ScreenName
{
    private UiBuilder? _builder;

    /// <summary>Builds the screen into <paramref name="root"/> (a fresh <c>LvScreen.Create()</c>, usually).</summary>
    public void Build(LvObject root)
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "Screens");
        var document = UiJson.Load(Path.Combine(folder, "ScreenName.lvgl.json"));

        _builder = new UiBuilder { AssetDirectory = folder };
        _builder.Build(document, root);

        if (_builder.Find<LvButton>("BackButton") is { } back)
        {
            back.Clicked += (_, _) => BackRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Raised when the user presses Back.</summary>
    public event EventHandler? BackRequested;

    /// <summary>Frees the layout's shared styles and fonts. Call after the widgets are gone.</summary>
    public void Destroy() => _builder?.Dispose();
}
