using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Lvgl;
using Lvgl.Backends;
using Lvgl.Interop;
using Lvgl.Ui;
using Lvgl.Widgets;

namespace Lvgl.Designer;

/// <summary>
/// Runs a real LVGL instance off-screen and publishes its frames as a <see cref="WriteableBitmap"/>.
/// </summary>
/// <remarks>
/// <para>
/// The preview is not a WPF re-implementation of LVGL's look. It is LVGL itself, rendering through
/// <see cref="HeadlessBackend"/> into a pixel buffer that is copied into a bitmap. Anything the
/// designer shows is therefore exactly what the device will draw, including font metrics, radii
/// and gradients - the details a hand-written approximation always gets subtly wrong.
/// </para>
/// <para>
/// LVGL is single-threaded and owned by whichever thread initialises it. Everything here runs on
/// the WPF dispatcher thread, driven by a <see cref="DispatcherTimer"/>, which satisfies that rule
/// without any cross-thread marshalling.
/// </para>
/// </remarks>
internal sealed class LvglPreview : IDisposable
{
    private readonly DispatcherTimer _timer;
    private HeadlessBackend? _backend;
    private LvglApplication? _application;
    private UiBuilder _builder = new();
    private UiDocument? _document;
    private bool _disposed;

    public LvglPreview()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(30) };
        _timer.Tick += (_, _) => Pump();
    }

    /// <summary>The latest rendered frame, or null while the preview is unavailable.</summary>
    public WriteableBitmap? Bitmap { get; private set; }

    /// <summary>
    /// Why the preview could not start - typically that the native library has not been built yet.
    /// Null when the preview is running.
    /// </summary>
    public string? UnavailableReason { get; private set; }

    /// <summary>True when LVGL is loaded and rendering.</summary>
    public bool IsAvailable => _application is not null;

    /// <summary>Raised after a frame has been copied into <see cref="Bitmap"/>.</summary>
    public event EventHandler? FrameReady;

    /// <summary>Raised when <see cref="Bitmap"/> is replaced, e.g. after a size change.</summary>
    public event EventHandler? SurfaceChanged;

    /// <summary>
    /// Builds <paramref name="document"/> into the preview, recreating the surface if the target
    /// screen size changed.
    /// </summary>
    public void Show(UiDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        _document = document;

        if (!EnsureSurface(document.Width, document.Height)) return;

        try
        {
            // Rebuild from scratch: the document is small, and diffing a widget tree would be a
            // source of preview-versus-device drift for no real gain at designer scale.
            _application!.Screen.Clear();
            _builder = new UiBuilder();
            _builder.Build(document, _application.Screen);
        }
        catch (LvglException ex)
        {
            UnavailableReason = $"The layout could not be rendered: {ex.Message}";
        }
    }

    /// <summary>Starts the render timer.</summary>
    public void Start() => _timer.Start();

    /// <summary>Stops the render timer.</summary>
    public void Stop() => _timer.Stop();

    private bool EnsureSurface(int width, int height)
    {
        if (UnavailableReason is not null && _application is null && _attemptedStart) return false;

        if (_backend is not null && _backend.Width == width && _backend.Height == height) return true;

        _attemptedStart = true;

        try
        {
            // A resize means a new lv_display, and LVGL keeps the display list in globals, so the
            // whole runtime is torn down and brought back up rather than leaving a stale display
            // behind.
            _application?.Dispose();
            _application = null;
            _backend?.Dispose();

            if (LvglRuntime.IsInitialized) LvglRuntime.Shutdown();

            _backend = new HeadlessBackend(Math.Max(1, width), Math.Max(1, height));
            _application = LvglApplication.Create(_backend, new LvglOptions
            {
                RenderMode = LvRenderMode.Partial,
                EnablePointer = false,   // the designer drives selection itself
            });

            // Bgr32 rather than Bgra32: LVGL's 32-bit buffer is XRGB8888, whose fourth byte is
            // padding, not alpha. Treating it as alpha would render the preview transparent.
            Bitmap = new WriteableBitmap(_backend.Width, _backend.Height, 96, 96, PixelFormats.Bgr32, null);
            UnavailableReason = null;

            SurfaceChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (LvglNativeNotFoundException ex)
        {
            UnavailableReason = ex.Message;
        }
        catch (LvglException ex)
        {
            UnavailableReason = ex.Message;
        }

        _application = null;
        _backend = null;
        Bitmap = null;
        SurfaceChanged?.Invoke(this, EventArgs.Empty);
        return false;
    }

    private bool _attemptedStart;

    private void Pump()
    {
        if (_application is null || _backend is null || Bitmap is null) return;

        try
        {
            _application.RunFrame();
        }
        catch (LvglException ex)
        {
            UnavailableReason = ex.Message;
            _timer.Stop();
            return;
        }

        if (!_backend.IsDirty) return;

        // A full-surface copy. At designer sizes this is around a megabyte at 30 Hz, far cheaper
        // than the bookkeeping a partial upload would need.
        Bitmap.WritePixels(
            new Int32Rect(0, 0, _backend.Width, _backend.Height),
            _backend.Buffer,
            _backend.Stride,
            0);

        _backend.ClearDirty();
        FrameReady?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Absolute bounds of the widget built from <paramref name="node"/>, in preview pixels, for
    /// drawing the selection outline. Null when the node has no live widget.
    /// </summary>
    public Rect? GetBounds(UiNode? node)
    {
        if (node is null || _application is null) return null;
        if (!_builder.WidgetsByNode.TryGetValue(node, out var widget) || !widget.IsAlive) return null;

        try
        {
            // LvObject coordinates are relative to the parent's content area, so walk up to the
            // screen accumulating offsets.
            double x = widget.X;
            double y = widget.Y;

            for (var parent = widget.Parent; parent is not null and not LvScreen; parent = parent.Parent)
            {
                x += parent.X;
                y += parent.Y;
            }

            return new Rect(x, y, widget.Width, widget.Height);
        }
        catch (ObjectDisposedException)
        {
            return null;
        }
    }

    /// <summary>Re-applies the current document, e.g. after a property edit.</summary>
    public void Refresh()
    {
        if (_document is not null) Show(_document);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _timer.Stop();
        _application?.Dispose();
        _backend?.Dispose();

        if (LvglRuntime.IsInitialized) LvglRuntime.Shutdown();
    }
}
