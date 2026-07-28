using Lvgl;
using Lvgl.Widgets;

namespace Lvgl.Samples.Desktop;

/// <summary>One page of the demo browser.</summary>
/// <remarks>
/// Pages are built lazily into a shared container and torn down when another page is selected, so
/// the sample also demonstrates that widget trees can be created and destroyed repeatedly without
/// leaking - the thing most easily got wrong in a P/Invoke wrapper.
/// </remarks>
internal abstract class DemoPage
{
    /// <summary>Title shown in the navigation list.</summary>
    public abstract string Title { get; }

    /// <summary>One-line description shown at the top of the page.</summary>
    public abstract string Description { get; }

    /// <summary>Creates the page's widgets inside <paramref name="container"/>.</summary>
    public abstract void Build(LvObject container, LvglApplication application);

    /// <summary>
    /// Called once per run-loop iteration while the page is displayed. Pages that animate override
    /// this; the default does nothing.
    /// </summary>
    public virtual void Update(LvglApplication application) { }

    /// <summary>Called before the page's widgets are destroyed.</summary>
    public virtual void Teardown() { }
}
