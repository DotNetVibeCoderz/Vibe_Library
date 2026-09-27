using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace ScrapyGallery.Controls;

/// <summary>Draws the Scrapy.Net mark — an amber orb-web on ink — for the NuGet icon and docs.</summary>
public static class IconRenderer
{
    public static void Render(string path, int size = 256)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize(size, size), new Vector(96, 96));
        using (var ctx = bitmap.CreateDrawingContext())
        {
            var s = size;
            ctx.DrawRectangle(Palette.Brush(Palette.Ink), null, new Rect(0, 0, s, s), s * 0.18, s * 0.18);
            var c = new Point(s / 2.0, s / 2.0);
            var spokes = 12;
            var silk = new Pen(Palette.Brush(Palette.Silk, 0.55), s / 128.0);
            for (var i = 0; i < spokes; i++)
            {
                var a = i * Math.Tau / spokes;
                ctx.DrawLine(silk, c, new Point(c.X + Math.Cos(a) * s * 0.4, c.Y + Math.Sin(a) * s * 0.4));
            }
            for (var r = 1; r <= 3; r++)
            {
                var radius = s * 0.13 * r;
                var g = new StreamGeometry();
                using (var gc = g.Open())
                {
                    for (var i = 0; i <= spokes; i++)
                    {
                        var a = i * Math.Tau / spokes;
                        var p = new Point(c.X + Math.Cos(a) * radius, c.Y + Math.Sin(a) * radius);
                        if (i == 0) gc.BeginFigure(p, false);
                        else gc.LineTo(p);
                    }
                }
                ctx.DrawGeometry(null, r == 3 ? new Pen(Palette.Brush(Palette.Argiope), s / 48.0) : silk, g);
            }
            // The crawler at the hub, and three pages it reached.
            ctx.DrawEllipse(Palette.Brush(Palette.Argiope), null, c, s * 0.07, s * 0.07);
            foreach (var (i, ring) in new[] { (1, 2), (4, 3), (8, 1) })
            {
                var a = i * Math.Tau / spokes;
                var radius = s * 0.13 * ring;
                ctx.DrawEllipse(Palette.Brush(Palette.Moss), new Pen(Palette.Brush(Palette.Ink), s / 96.0),
                    new Point(c.X + Math.Cos(a) * radius, c.Y + Math.Sin(a) * radius), s * 0.035, s * 0.035);
            }
        }
        bitmap.Save(path);
    }
}
