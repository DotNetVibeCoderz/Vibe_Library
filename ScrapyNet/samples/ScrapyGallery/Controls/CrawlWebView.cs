using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ScrapyGallery.Infrastructure;

namespace ScrapyGallery.Controls;

/// <summary>
/// The crawl drawn as a spider's web: the start page at the hub, every followed page spun onto the
/// ring for its link depth, and a silk thread back to the page that linked it. Nodes take their
/// colour from the HTTP outcome and grow in when their response lands.
/// </summary>
public sealed class CrawlWebView : Control
{
    private const int Spokes = 24;
    private const double GrowSeconds = 0.45;

    private IReadOnlyList<WebNode> _nodes = [];
    private int _offset;

    public void Update(IReadOnlyList<WebNode> nodes)
    {
        _nodes = nodes;
        InvalidateVisual();
    }

    /// <summary>True while any node is still growing in (the window keeps redrawing until then).</summary>
    public bool IsGrowing
    {
        get
        {
            var now = Stopwatch.GetTimestamp();
            return _nodes.Count > 0 && _nodes.Any(n => Stopwatch.GetElapsedTime(n.ArrivedTicks, now).TotalSeconds < GrowSeconds);
        }
    }

    public override void Render(DrawingContext context)
    {
        var bounds = Bounds;
        var center = new Point(bounds.Width / 2, bounds.Height / 2);
        var maxRadius = Math.Min(bounds.Width, bounds.Height) / 2 - 18;
        if (maxRadius <= 10) return;

        // Many start pages get the first ring to themselves around an empty hub instead of crowding the centre.
        var roots = _nodes.Count(n => n.Depth == 0);
        _offset = roots > 3 ? 1 : 0;
        var maxDepth = Math.Max(1, (_nodes.Count == 0 ? 3 : _nodes.Max(n => n.Depth)) + _offset);
        var ringGap = maxRadius / (maxDepth + 0.35);

        DrawWeb(context, center, maxDepth, ringGap);
        if (_nodes.Count == 0)
        {
            var hint = new FormattedText("Press Run to spin the crawl web", System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, Fonts.Body, 13, Palette.Brush(Palette.InkSoft));
            context.DrawText(hint, new Point(center.X - hint.Width / 2, center.Y + ringGap * 0.35));
            return;
        }

        var positions = Layout(center, ringGap);
        if (_offset > 0) context.DrawEllipse(Palette.Brush(Palette.Thread), null, center, 3, 3);
        var threadPen = new Pen(Palette.Brush(Palette.Thread, 0.55), 0.8);
        if (_offset > 0)
            foreach (var root in _nodes.Where(n => n.Depth == 0))
                if (positions.TryGetValue(root.Url, out var rp)) context.DrawLine(threadPen, center, rp);
        foreach (var node in _nodes)
        {
            if (node.Parent is null || !positions.TryGetValue(node.Parent, out var from) || !positions.TryGetValue(node.Url, out var to)) continue;
            // A thread sags toward the hub, as silk does between radial lines.
            var mid = new Point((from.X + to.X) / 2, (from.Y + to.Y) / 2);
            var control = new Point(mid.X + (center.X - mid.X) * 0.22, mid.Y + (center.Y - mid.Y) * 0.22);
            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                g.BeginFigure(from, false);
                g.QuadraticBezierTo(control, to);
            }
            context.DrawGeometry(null, threadPen, geometry);
        }

        var now = Stopwatch.GetTimestamp();
        foreach (var node in _nodes)
        {
            if (!positions.TryGetValue(node.Url, out var p)) continue;
            var age = Stopwatch.GetElapsedTime(node.ArrivedTicks, now).TotalSeconds;
            var grow = Math.Clamp(age / GrowSeconds, 0, 1);
            var eased = 1 - Math.Pow(1 - grow, 3);
            var radius = (node.Depth == 0 ? (_offset == 0 ? 7 : 5) : node.Status == 0 ? 2.5 : 4.2) * (0.35 + 0.65 * eased);
            var color = Palette.ForStatus(node.Status);
            if (node.Depth == 0)
                context.DrawEllipse(null, new Pen(Palette.Brush(Palette.Argiope), _offset == 0 ? 2 : 1.4), p, radius + (_offset == 0 ? 4 : 2.5), radius + (_offset == 0 ? 4 : 2.5));
            context.DrawEllipse(Palette.Brush(color, node.Status == 0 ? 0.6 : 1), node.Cached ? new Pen(Palette.Brush(Palette.Argiope), 1.5) : null, p, radius, radius);
            // A fresh response flashes a halo that fades as the node settles.
            if (grow < 1 && node.Status != 0)
                context.DrawEllipse(null, new Pen(Palette.Brush(color, 0.5 * (1 - grow)), 1.2), p, radius + 8 * eased, radius + 8 * eased);
        }
    }

    private static void DrawWeb(DrawingContext context, Point center, int maxDepth, double ringGap)
    {
        var faint = new Pen(Palette.Brush(Palette.Thread, 0.28), 0.7);
        var outer = ringGap * (maxDepth + 0.3);
        for (var s = 0; s < Spokes; s++)
        {
            var a = s * Math.Tau / Spokes;
            context.DrawLine(faint, center, new Point(center.X + Math.Cos(a) * outer, center.Y + Math.Sin(a) * outer));
        }
        // Rings are polygons, not circles: that straight-between-spokes look is what makes it read as a web.
        for (var d = 1; d <= maxDepth; d++)
        {
            var r = ringGap * d;
            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                for (var s = 0; s <= Spokes; s++)
                {
                    var a = s * Math.Tau / Spokes;
                    var pt = new Point(center.X + Math.Cos(a) * r, center.Y + Math.Sin(a) * r);
                    if (s == 0) g.BeginFigure(pt, false);
                    else g.LineTo(pt);
                }
            }
            context.DrawGeometry(null, faint, geometry);
        }
    }

    /// <summary>
    /// Assigns each node an angle on its ring: nodes are ordered by their parent's angle, then by
    /// arrival, and spread evenly — so children sit near their parent without overlapping.
    /// </summary>
    private Dictionary<string, Point> Layout(Point center, double ringGap)
    {
        var angles = new Dictionary<string, double>(StringComparer.Ordinal);
        var positions = new Dictionary<string, Point>(StringComparer.Ordinal);
        foreach (var ring in _nodes.GroupBy(n => n.Depth).OrderBy(g => g.Key))
        {
            var members = ring.Select((n, i) => (Node: n, Order: i))
                .OrderBy(x => x.Node.Parent is not null && angles.TryGetValue(x.Node.Parent, out var pa) ? pa : 0)
                .ThenBy(x => x.Order)
                .ToList();
            for (var i = 0; i < members.Count; i++)
            {
                var node = members[i].Node;
                double angle;
                double radius;
                if (ring.Key == 0 && _offset == 0)
                {
                    // Several start URLs share the hub on a small inner circle.
                    angle = members.Count == 1 ? -Math.PI / 2 : i * Math.Tau / members.Count;
                    radius = members.Count == 1 ? 0 : ringGap * 0.3;
                }
                else
                {
                    angle = -Math.PI / 2 + i * Math.Tau / members.Count;
                    radius = ringGap * (ring.Key + _offset);
                }
                angles[node.Url] = angle;
                positions[node.Url] = new Point(center.X + Math.Cos(angle) * radius, center.Y + Math.Sin(angle) * radius);
            }
        }
        return positions;
    }
}
