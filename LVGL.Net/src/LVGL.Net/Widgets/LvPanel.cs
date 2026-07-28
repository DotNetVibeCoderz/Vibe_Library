namespace Lvgl.Widgets;

/// <summary>
/// A plain container. Identical to a bare <see cref="LvObject"/>, named for readability when it is
/// used purely as a grouping or layout element.
/// </summary>
public sealed class LvPanel : LvObject
{
    /// <summary>Creates a container on <paramref name="parent"/>.</summary>
    public LvPanel(LvObject? parent) : base(parent) { }

    /// <summary>Creates a container with a size.</summary>
    public LvPanel(LvObject? parent, int width, int height) : base(parent) => SetSize(width, height);
}
