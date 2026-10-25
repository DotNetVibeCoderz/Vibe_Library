using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Lvgl.Backends;
using Lvgl.Interop;
using Lvgl.Ui;
using Lvgl.Widgets;

namespace Lvgl.Designer.Core;

/// <summary>
/// Runs a real LVGL instance off-screen and publishes its frames as a <see cref="WriteableBitmap"/>.
/// </summary>
/// <remarks>
/// <para>
/// The preview is not a re-implementation of LVGL's look. It is LVGL itself, rendering through
/// <see cref="HeadlessBackend"/> into a pixel buffer that is copied into a bitmap. Anything the
/// designer shows is therefore exactly what the device will draw, including font metrics, radii
/// and gradients - the details a hand-written approximation always gets subtly wrong.
/// </para>
/// <para>
/// LVGL is single-threaded and owned by whichever thread initialises it. Everything here runs on
/// the Avalonia UI thread, driven by a <see cref="DispatcherTimer"/>, which satisfies that rule
/// without any cross-thread marshalling.
/// </para>
/// </remarks>
public sealed class LvglPreview : IDisposable
{
    private readonly DispatcherTimer _timer;
    private HeadlessBackend? _backend;
    private LvglApplication? _application;
    private UiBuilder? _builder;
    private UiDocument? _document;
    private bool _attemptedStart;
    private bool _disposed;

    public LvglPreview()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(30) };
        _timer.Tick += (_, _) => Pump();
    }

    /// <summary>The latest rendered frame, or null while the preview is unavailable.</summary>
    public WriteableBitmap? Bitmap { get; private set; }

    /// <summary>Why the preview could not start or render - typically a missing native library.</summary>
    public string? UnavailableReason { get; private set; }

    /// <summary>Problems met while building the last document, such as a missing asset file.</summary>
    public IReadOnlyList<string> Diagnostics => _builder?.Diagnostics ?? [];

    /// <summary>True when LVGL is loaded and rendering.</summary>
    public bool IsAvailable => _application is not null;

    /// <summary>The LVGL version actually loaded, for the status bar.</summary>
    public Version? LvglVersion => IsAvailable ? LvglRuntime.LvglVersion : null;

    /// <summary>Folder relative asset paths resolve against.</summary>
    public string AssetDirectory { get; set; } = Environment.CurrentDirectory;

    /// <summary>Raised after a frame has been copied into <see cref="Bitmap"/>.</summary>
    public event EventHandler? FrameReady;

    /// <summary>Raised when <see cref="Bitmap"/> is replaced, e.g. after a size change.</summary>
    public event EventHandler? SurfaceChanged;

    /// <summary>Raised after a document has been (re)built into the preview.</summary>
    public event EventHandler? Rebuilt;

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
            // source of preview-versus-device drift for no real gain at designer scale. Widgets go
            // first, then the old builder's styles and fonts they pointed at.
            _application!.Screen.Clear();
            _builder?.Dispose();

            _builder = new UiBuilder { AssetDirectory = AssetDirectory };
            _builder.Build(document, _application.Screen);
            _application.Screen.UpdateLayout();

            // Render now rather than on the next tick, so bounds and pixels agree immediately.
            Pump();
            Rebuilt?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is LvglException or InvalidOperationException or ArgumentException)
        {
            UnavailableReason = $"The layout could not be rendered: {ex.Message}";
            SurfaceChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Re-applies the current document.</summary>
    public void Refresh()
    {
        if (_document is not null) Show(_document);
    }

    public void Start() => _timer.Start();

    public void Stop() => _timer.Stop();

    private bool EnsureSurface(int width, int height)
    {
        if (_application is null && _attemptedStart && UnavailableReason is not null && _backend is null)
        {
            // A missing native library does not appear by itself; do not retry on every edit.
            if (UnavailableReason.Contains("native", StringComparison.OrdinalIgnoreCase)) return false;
        }

        if (_backend is not null && _backend.Width == width && _backend.Height == height)
        {
            UnavailableReason = null;
            return true;
        }

        _attemptedStart = true;

        try
        {
            // A resize means a new lv_display, and LVGL keeps the display list in globals, so the
            // whole runtime is torn down and brought back up rather than leaving a stale display
            // behind.
            _application?.Screen.Clear();
            _builder?.Dispose();
            _builder = null;
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

            // Opaque BGRA: LVGL's buffer is XRGB8888, whose fourth byte is padding, not alpha.
            Bitmap = new WriteableBitmap(
                new PixelSize(_backend.Width, _backend.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
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

    private unsafe void Pump()
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
            SurfaceChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (!_backend.IsDirty) return;

        // A full-surface copy. At designer sizes this is around a megabyte at 30 Hz, far cheaper
        // than the bookkeeping a partial upload would need.
        using (var target = Bitmap.Lock())
        {
            var source = _backend.Buffer;
            var rowBytes = _backend.Width * HeadlessBackend.BytesPerPixel;

            fixed (byte* from = source)
            {
                for (var y = 0; y < _backend.Height; y++)
                {
                    Buffer.MemoryCopy(
                        from + y * _backend.Stride,
                        (byte*)target.Address + y * target.RowBytes,
                        target.RowBytes,
                        rowBytes);
                }
            }
        }

        _backend.ClearDirty();
        FrameReady?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Absolute bounds of the widget built from <paramref name="node"/>, in preview pixels. Null
    /// when the node has no live widget.
    /// </summary>
    public Rect? GetBounds(UiNode? node)
    {
        if (node is null || _builder is null || _application is null) return null;
        if (!_builder.WidgetsByNode.TryGetValue(node, out var widget) || !widget.IsAlive) return null;

        try
        {
            var area = widget.GetBounds();
            return new Rect(area.X1, area.Y1, area.Width, area.Height);
        }
        catch (ObjectDisposedException)
        {
            return null;
        }
    }

    /// <summary>Every node's bounds, deepest last - the order hit-testing walks in reverse.</summary>
    public IReadOnlyList<(UiNode Node, Rect Bounds)> AllBounds()
    {
        var result = new List<(UiNode, Rect)>();
        if (_document is null) return result;

        foreach (var node in _document.Descendants())
        {
            if (GetBounds(node) is { } bounds) result.Add((node, bounds));
        }

        return result;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _timer.Stop();
        _application?.Screen.Clear();
        _builder?.Dispose();
        _application?.Dispose();
        _backend?.Dispose();

        if (LvglRuntime.IsInitialized) LvglRuntime.Shutdown();
    }
}
