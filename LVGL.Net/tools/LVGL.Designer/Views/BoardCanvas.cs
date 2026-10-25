using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Lvgl.Designer.Core;
using Lvgl.Ui;
using CoreRect = Lvgl.Designer.Core.Rect;
using UiRect = Avalonia.Rect;

namespace Lvgl.Designer.Views;

/// <summary>
/// The design surface: LVGL's own pixels laid on a circuit board, with the selected widget drawn
/// as a component footprint that can be dragged and resized.
/// </summary>
/// <remarks>
/// <para>
/// The board is the designer's one piece of ornament, and it carries information: the via grid is
/// the snap grid, the rulers are in device pixels, and the footprint's designator tag names the
/// widget's kind, its field name and its size - the three things you check while placing it.
/// </para>
/// <para>
/// Gestures are translated into <see cref="DesignerSession"/> operations as they happen, so the
/// preview re-renders under the pointer and a whole drag undoes as one step (the edits share a
/// coalescing key per gesture).
/// </para>
/// </remarks>
public sealed class BoardCanvas : Control
{
    /// <summary>Space between the screen and the board edge, in device pixels at zoom 1.</summary>
    private const double BoardMargin = 36;

    /// <summary>Via pitch, and the default snap grid.</summary>
    public const int Grid = 8;

    private const double PadSize = 8;

    private DesignerSession? _session;
    private LvglPreview? _preview;
    private double _zoom = 1;
    private Vector _pan;
    private bool _fitPending = true;

    private UiNode? _hover;
    private Gesture? _gesture;
    private Point _panStart;
    private Vector _panOrigin;
    private bool _panning;
    private int _gestureId;

    // Pens and brushes are resolved once from the theme tokens; the canvas redraws at 30 Hz.
    private IBrush _mask = Brushes.Black;
    private IBrush _board = Brushes.Black;
    private IBrush _silk = Brushes.White;
    private IBrush _silkDim = Brushes.Gray;
    private IBrush _copper = Brushes.Orange;
    private IBrush _copperWash = Brushes.Transparent;
    private IPen _tracePen = new Pen(Brushes.Gray);
    private IPen _copperPen = new Pen(Brushes.Orange);
    private IPen _hoverPen = new Pen(Brushes.Gray);
    private IPen _edgePen = new Pen(Brushes.Gray);
    private IBrush _via = Brushes.Gray;
    private Typeface _mono = Typeface.Default;

    public BoardCanvas()
    {
        Focusable = true;
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.Arrow);
    }

    /// <summary>Raised when the user picks a widget on the canvas (or empty board: null).</summary>
    public event EventHandler<UiNode?>? NodePicked;

    /// <summary>Raised when the zoom changes, for the status bar.</summary>
    public event EventHandler? ZoomChanged;

    /// <summary>Current zoom; 1 is one device pixel per screen pixel.</summary>
    public double Zoom => _zoom;

    /// <summary>When true, drags snap to the via grid. Holding Alt inverts it for one gesture.</summary>
    public bool SnapToGrid { get; set; } = true;

    /// <summary>Connects the canvas to the model and the renderer.</summary>
    public void Attach(DesignerSession session, LvglPreview preview)
    {
        _session = session;
        _preview = preview;

        preview.FrameReady += (_, _) => InvalidateVisual();
        preview.SurfaceChanged += (_, _) =>
        {
            _fitPending = true;
            InvalidateVisual();
        };
        session.SelectionChanged += (_, _) => InvalidateVisual();
    }

    #region Zoom and pan

    /// <summary>Zooms so the whole board fits the canvas.</summary>
    public void FitToView()
    {
        if (_session is null || Bounds.Width <= 0 || Bounds.Height <= 0) return;

        var document = _session.Document;
        var fitX = Bounds.Width / (document.Width + BoardMargin * 2);
        var fitY = Bounds.Height / (document.Height + BoardMargin * 2);

        // Never blow a small screen up past 2x on fit: past that, pixels stop looking like pixels.
        SetZoom(Math.Clamp(Math.Min(fitX, fitY), 0.1, 2));
        _pan = default;
        _fitPending = false;
        InvalidateVisual();
    }

    /// <summary>Sets the zoom, keeping the board centred.</summary>
    public void SetZoom(double zoom)
    {
        _zoom = Math.Clamp(zoom, 0.1, 8);
        ZoomChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    /// <summary>Top-left of the device screen, in canvas coordinates.</summary>
    private Point ScreenOrigin
    {
        get
        {
            if (_session is null) return default;
            var document = _session.Document;
            var x = (Bounds.Width - document.Width * _zoom) / 2 + _pan.X;
            var y = (Bounds.Height - document.Height * _zoom) / 2 + _pan.Y;

            // Whole pixels, so the LVGL bitmap is never resampled across a half-pixel at 1x.
            return new Point(Math.Round(x), Math.Round(y));
        }
    }

    private Point ToDevice(Point canvas)
    {
        var origin = ScreenOrigin;
        return new Point((canvas.X - origin.X) / _zoom, (canvas.Y - origin.Y) / _zoom);
    }

    /// <summary>Where a device pixel is drawn on the canvas - for the UI tests to aim the pointer.</summary>
    internal Point DeviceToCanvas(double x, double y)
    {
        var origin = ScreenOrigin;
        return new Point(origin.X + x * _zoom, origin.Y + y * _zoom);
    }

    private UiRect ToCanvas(CoreRect device)
    {
        var origin = ScreenOrigin;
        return new UiRect(origin.X + device.X * _zoom, origin.Y + device.Y * _zoom, device.Width * _zoom, device.Height * _zoom);
    }

    #endregion

    #region Rendering

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ResolveTheme();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (_fitPending) FitToView();
    }

    private void ResolveTheme()
    {
        IBrush Brush(string key, IBrush fallback) =>
            this.TryFindResource(key, out var value) && value is IBrush brush ? brush : fallback;

        _mask = Brush("Mask", _mask);
        _board = Brush("Board", _board);
        _silk = Brush("Silk", _silk);
        _silkDim = Brush("SilkDim", _silkDim);
        _copper = Brush("Copper", _copper);
        _copperWash = Brush("CopperWash", _copperWash);

        var trace = Brush("Trace", Brushes.Gray);
        _tracePen = new Pen(trace, 1);
        _via = Brush("TraceBright", Brushes.Gray);
        _edgePen = new Pen(_via, 1.5);
        _copperPen = new Pen(_copper, 1.5);
        _hoverPen = new Pen(_silkDim, 1, new DashStyle([3, 3], 0));

        if (this.TryFindResource("MonoFont", out var font) && font is FontFamily family)
        {
            _mono = new Typeface(family);
        }
    }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(_mask, new UiRect(Bounds.Size));
        if (_session is null) return;

        var document = _session.Document;
        var screen = ToCanvas(new CoreRect(0, 0, document.Width, document.Height));
        var board = screen.Inflate(BoardMargin * _zoom);

        DrawBoard(context, board);
        DrawRulers(context, screen, document);

        if (_preview?.Bitmap is { } bitmap)
        {
            // Nearest neighbour: zoomed in, a designer wants to see LVGL's real pixels, not a blur.
            using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.None }))
            {
                context.DrawImage(bitmap, new UiRect(0, 0, bitmap.PixelSize.Width, bitmap.PixelSize.Height), screen);
            }
        }
        else
        {
            context.FillRectangle(_board, screen);
        }

        context.DrawRectangle(null, _tracePen, screen.Inflate(0.5));

        if (_hover is not null && !ReferenceEquals(_hover, _session.Selection) && _preview?.GetBounds(_hover) is { } hover)
        {
            context.DrawRectangle(null, _hoverPen, ToCanvas(hover).Deflate(0.5));
        }

        if (_session.Selection is { } selection && CurrentFootprint(selection) is { } footprint)
        {
            DrawFootprint(context, selection, footprint);
        }
    }

    private void DrawBoard(DrawingContext context, UiRect board)
    {
        context.FillRectangle(_board, board, (float)(10 * _zoom));
        context.DrawRectangle(null, _edgePen, board, 10 * _zoom, 10 * _zoom);

        // The via grid doubles as the snap grid; it thins out when zoomed so far out that it would
        // turn into a grey wash.
        var pitch = Grid * _zoom;
        while (pitch < 10) pitch *= 2;

        var radius = Math.Max(0.8, 0.9 * Math.Min(_zoom, 1.5));
        var startX = board.X + (pitch - (board.X - ScreenOrigin.X) % pitch) % pitch;
        var startY = board.Y + (pitch - (board.Y - ScreenOrigin.Y) % pitch) % pitch;

        for (var y = startY; y < board.Bottom; y += pitch)
        {
            for (var x = startX; x < board.Right; x += pitch)
            {
                context.DrawEllipse(_via, null, new Point(x, y), radius, radius);
            }
        }

        // Mounting holes: a copper ring with a drilled centre, inset from each corner.
        var inset = 16 * _zoom;
        var ring = Math.Max(3, 7 * _zoom);
        foreach (var corner in new[]
                 {
                     new Point(board.X + inset, board.Y + inset), new Point(board.Right - inset, board.Y + inset),
                     new Point(board.X + inset, board.Bottom - inset), new Point(board.Right - inset, board.Bottom - inset),
                 })
        {
            context.DrawEllipse(_copper, null, corner, ring, ring);
            context.DrawEllipse(_mask, null, corner, ring * 0.55, ring * 0.55);
        }
    }

    private void DrawRulers(DrawingContext context, UiRect screen, UiDocument document)
    {
        // Labels every 100 device pixels (50 when zoomed in), on the board margin, in silkscreen.
        var step = _zoom >= 1.5 ? 50 : 100;
        var tick = 5 * Math.Min(_zoom, 1.5);

        // The far ends are left unlabelled: the size readout says them, and they would sit on the
        // mounting holes.
        for (var x = 0; x < document.Width - step / 3; x += step)
        {
            var cx = screen.X + x * _zoom;
            context.DrawLine(_tracePen, new Point(cx, screen.Y - tick - 2), new Point(cx, screen.Y - 2));
            DrawText(context, x.ToString(CultureInfo.InvariantCulture), new Point(cx + 3, screen.Y - tick - 15), _silkDim, 9);
        }

        // Vertical labels sit right-aligned against the screen edge, and only when the board margin
        // is wide enough to hold them - zoomed far out, they would spill off the board.
        var margin = BoardMargin * _zoom;
        var showY = margin >= MeasureText(document.Height.ToString(CultureInfo.InvariantCulture), 9) + tick + 6;

        for (var y = 0; y < document.Height - step / 3; y += step)
        {
            var cy = screen.Y + y * _zoom;
            context.DrawLine(_tracePen, new Point(screen.X - tick - 2, cy), new Point(screen.X - 2, cy));
            if (y == 0 || !showY) continue;

            var label = y.ToString(CultureInfo.InvariantCulture);
            DrawText(context, label, new Point(screen.X - tick - 4 - MeasureText(label, 9), cy + 2), _silkDim, 9);
        }

        var size = $"{document.Width} × {document.Height}";
        DrawText(context, size, new Point(screen.Right - MeasureText(size, 9) , screen.Bottom + 8), _silkDim, 9);
    }

    private void DrawFootprint(DrawingContext context, UiNode node, CoreRect device)
    {
        var footprint = ToCanvas(device);

        // Outline and pads only: a tint over the widget would change the very colours the preview
        // exists to show exactly.
        context.DrawRectangle(null, _copperPen, footprint.Inflate(0.75));

        if (IsResizable(node))
        {
            foreach (var (_, pad) in Pads(footprint))
            {
                context.FillRectangle(_copper, pad);
                context.DrawRectangle(null, new Pen(_mask, 1), pad);
            }
        }

        // Designator tag: kind and number in copper, field name in silk, size in dim silk.
        var designator = Designators.For(_session!.Document, node);
        var name = node.Name ?? "unnamed";
        var size = $"{device.Width:0}×{device.Height:0}";

        const double textSize = 10;
        var widths = new[] { MeasureText(designator, textSize), MeasureText(name, textSize), MeasureText(size, textSize) };
        var tagWidth = widths.Sum() + 30;
        var tagHeight = 18.0;

        var tagY = footprint.Y - tagHeight - 6;
        if (tagY < 2) tagY = footprint.Bottom + 6;
        var tagX = Math.Clamp(footprint.X, 2, Math.Max(2, Bounds.Width - tagWidth - 2));
        var tag = new UiRect(tagX, tagY, tagWidth, tagHeight);

        context.FillRectangle(_mask, tag, 3);
        context.DrawRectangle(null, new Pen(_copper, 1), tag.Deflate(0.5), 3, 3);

        var x = tag.X + 8;
        DrawText(context, designator, new Point(x, tag.Y + 3), _copper, textSize);
        x += widths[0] + 7;
        DrawText(context, name, new Point(x, tag.Y + 3), _silk, textSize);
        x += widths[1] + 7;
        DrawText(context, size, new Point(x, tag.Y + 3), _silkDim, textSize);
    }

    private void DrawText(DrawingContext context, string text, Point origin, IBrush brush, double size) =>
        context.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _mono, size, brush), origin);

    private double MeasureText(string text, double size) =>
        new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _mono, size, _silk).Width;

    /// <summary>The selection's footprint: the live gesture's rectangle while dragging, else LVGL's.</summary>
    private CoreRect? CurrentFootprint(UiNode node) =>
        _gesture is { } gesture && ReferenceEquals(gesture.Node, node) ? gesture.Current : _preview?.GetBounds(node);

    private static IEnumerable<(ResizeEdge Edge, UiRect Pad)> Pads(UiRect footprint)
    {
        var half = PadSize / 2;
        double l = footprint.X, t = footprint.Y, r = footprint.Right, b = footprint.Bottom;
        double cx = (l + r) / 2, cy = (t + b) / 2;

        UiRect Pad(double x, double y) => new(x - half, y - half, PadSize, PadSize);

        yield return (ResizeEdge.TopLeft, Pad(l, t));
        yield return (ResizeEdge.Top, Pad(cx, t));
        yield return (ResizeEdge.TopRight, Pad(r, t));
        yield return (ResizeEdge.Right, Pad(r, cy));
        yield return (ResizeEdge.BottomRight, Pad(r, b));
        yield return (ResizeEdge.Bottom, Pad(cx, b));
        yield return (ResizeEdge.BottomLeft, Pad(l, b));
        yield return (ResizeEdge.Left, Pad(l, cy));
    }

    private static bool IsResizable(UiNode node) => node.Type is not (UiWidgetType.Tab or UiWidgetType.Tile);

    #endregion

    #region Hit testing

    /// <summary>
    /// The topmost node under a device-pixel point. A node counts only where it is also inside all
    /// of its ancestors - a tab view lays out its inactive pages beside the visible one and clips
    /// them, and those pages must not catch clicks meant for what is actually drawn there.
    /// </summary>
    public UiNode? HitTest(double x, double y)
    {
        if (_preview is null || _session is null) return null;

        var bounds = new Dictionary<UiNode, CoreRect>(ReferenceEqualityComparer.Instance);
        foreach (var (node, rect) in _preview.AllBounds()) bounds[node] = rect;

        foreach (var node in _session.Document.Descendants().Reverse())
        {
            if (!bounds.TryGetValue(node, out var rect) || !rect.Contains(x, y)) continue;
            if (node.Hidden) continue;

            var visible = true;
            for (var parent = _session.Document.ParentOf(node); parent is not null; parent = _session.Document.ParentOf(parent))
            {
                if (!bounds.TryGetValue(parent, out var parentRect) || !parentRect.Contains(x, y) || parent.Hidden)
                {
                    visible = false;
                    break;
                }
            }

            if (visible) return (UiNode)node;
        }

        return null;
    }

    private ResizeEdge PadAt(Point canvas)
    {
        if (_session?.Selection is not { } selection || !IsResizable(selection)) return ResizeEdge.None;
        if (CurrentFootprint(selection) is not { } device) return ResizeEdge.None;

        foreach (var (edge, pad) in Pads(ToCanvas(device)))
        {
            if (pad.Inflate(3).Contains(canvas)) return edge;
        }

        return ResizeEdge.None;
    }

    private static StandardCursorType CursorFor(ResizeEdge edge) => edge switch
    {
        ResizeEdge.TopLeft or ResizeEdge.BottomRight => StandardCursorType.TopLeftCorner,
        ResizeEdge.TopRight or ResizeEdge.BottomLeft => StandardCursorType.TopRightCorner,
        ResizeEdge.Left or ResizeEdge.Right => StandardCursorType.SizeWestEast,
        ResizeEdge.Top or ResizeEdge.Bottom => StandardCursorType.SizeNorthSouth,
        _ => StandardCursorType.Arrow,
    };

    #endregion

    #region Gestures

    /// <summary>A move or resize in progress.</summary>
    private sealed class Gesture
    {
        public required UiNode Node { get; init; }
        public required ResizeEdge Edge { get; init; }
        public required Point StartDevice { get; init; }
        public required CoreRect StartRect { get; init; }
        public required int StartX { get; init; }
        public required int StartY { get; init; }
        public required string Key { get; init; }
        public CoreRect Current { get; set; }
        public (int X, int Y) Applied { get; set; }
        public bool Moved { get; set; }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();

        if (_session is null) return;
        var point = e.GetCurrentPoint(this);

        // Middle button, or space held, pans.
        if (point.Properties.IsMiddleButtonPressed)
        {
            _panning = true;
            _panStart = point.Position;
            _panOrigin = _pan;
            e.Pointer.Capture(this);
            return;
        }

        if (!point.Properties.IsLeftButtonPressed) return;

        var device = ToDevice(point.Position);
        var edge = PadAt(point.Position);

        UiNode? target;
        if (edge != ResizeEdge.None)
        {
            target = _session.Selection;
        }
        else
        {
            target = HitTest(device.X, device.Y);

            // Clicking inside the current selection keeps it, even where a child would win the hit
            // test - otherwise a container could never be dragged by its own body.
            if (_session.Selection is { } current && _preview?.GetBounds(current) is { } currentRect &&
                currentRect.Contains(device.X, device.Y) && target is not null && IsInside(target, current))
            {
                target = current;
            }

            NodePicked?.Invoke(this, target);
        }

        if (target is null || _preview?.GetBounds(target) is not { } start) return;
        if (target.Type is UiWidgetType.Tab or UiWidgetType.Tile) return;

        _gesture = new Gesture
        {
            Node = target,
            Edge = edge,
            StartDevice = device,
            StartRect = start,
            StartX = target.X,
            StartY = target.Y,
            Key = $"gesture-{++_gestureId}",
            Current = start,
        };

        e.Pointer.Capture(this);
        e.Handled = true;
    }

    private bool IsInside(UiNode node, UiNode ancestor)
    {
        for (var parent = _session!.Document.ParentOf(node); parent is not null; parent = _session.Document.ParentOf(parent))
        {
            if (ReferenceEquals(parent, ancestor)) return true;
        }

        return false;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_session is null) return;

        var position = e.GetPosition(this);

        if (_panning)
        {
            _pan = _panOrigin + (position - _panStart);
            InvalidateVisual();
            return;
        }

        if (_gesture is not { } gesture)
        {
            var device = ToDevice(position);
            var hover = HitTest(device.X, device.Y);
            if (!ReferenceEquals(hover, _hover))
            {
                _hover = hover;
                InvalidateVisual();
            }

            Cursor = new Cursor(CursorFor(PadAt(position)));
            return;
        }

        var now = ToDevice(position);
        var dx = now.X - gesture.StartDevice.X;
        var dy = now.Y - gesture.StartDevice.Y;

        // A click should select, not nudge: ignore jitter until the pointer really moves.
        if (!gesture.Moved && Math.Abs(dx) < 3 / _zoom && Math.Abs(dy) < 3 / _zoom) return;
        gesture.Moved = true;

        var snap = SnapToGrid != e.KeyModifiers.HasFlag(KeyModifiers.Alt) ? Grid : 1;

        if (gesture.Edge == ResizeEdge.None)
        {
            // Snap the moving edge, not the delta, so a widget lands on the grid wherever it began.
            var left = ResizeMath.Snap(gesture.StartRect.X + dx, snap);
            var top = ResizeMath.Snap(gesture.StartRect.Y + dy, snap);
            var total = ((int)(left - gesture.StartRect.X), (int)(top - gesture.StartRect.Y));

            if (total != gesture.Applied)
            {
                _session.MoveBy(gesture.Node, total.Item1 - gesture.Applied.X, total.Item2 - gesture.Applied.Y, gesture.Key);
                gesture.Applied = total;
            }

            gesture.Current = gesture.StartRect with { X = gesture.StartRect.X + total.Item1, Y = gesture.StartRect.Y + total.Item2 };
        }
        else
        {
            var target = ResizeMath.Apply(gesture.StartRect, gesture.Edge, dx, dy);
            target = SnapRect(target, gesture.Edge, snap);

            gesture.Current = target;
            _session.ResizeTo(gesture.Node, gesture.StartX, gesture.StartY, gesture.StartRect, target, gesture.Key);
        }

        InvalidateVisual();
    }

    private static CoreRect SnapRect(CoreRect rect, ResizeEdge edge, double grid)
    {
        double left = rect.X, top = rect.Y, right = rect.Right, bottom = rect.Bottom;

        if (edge.HasFlag(ResizeEdge.Left)) left = ResizeMath.Snap(left, grid);
        if (edge.HasFlag(ResizeEdge.Right)) right = ResizeMath.Snap(right, grid);
        if (edge.HasFlag(ResizeEdge.Top)) top = ResizeMath.Snap(top, grid);
        if (edge.HasFlag(ResizeEdge.Bottom)) bottom = ResizeMath.Snap(bottom, grid);

        return new CoreRect(left, top, Math.Max(ResizeMath.MinimumSize, right - left), Math.Max(ResizeMath.MinimumSize, bottom - top));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        _panning = false;
        _gesture = null;
        e.Pointer.Capture(null);
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hover = null;
        InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            // Plain wheel pans vertically, Shift+wheel horizontally - the convention of every canvas tool.
            var delta = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? new Vector(e.Delta.Y * 40, 0) : new Vector(e.Delta.X * 40, e.Delta.Y * 40);
            _pan += delta;
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        // Zoom about the pointer, so the point under it stays put.
        var pointer = e.GetPosition(this);
        var before = ToDevice(pointer);

        SetZoom(_zoom * (e.Delta.Y > 0 ? 1.15 : 1 / 1.15));

        var after = ToDevice(pointer);
        _pan += new Vector((after.X - before.X) * _zoom, (after.Y - before.Y) * _zoom);
        InvalidateVisual();
        e.Handled = true;
    }

    #endregion
}
