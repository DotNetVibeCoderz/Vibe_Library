// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using RLNet.Environments.Classic;
using RLNet.Environments.Control;
using RLNet.Environments.Domain;
using RLNet.Environments.MultiAgent;

namespace RLNet.Visualizer.Controls;

/// <summary>
/// Draws whatever environment the session is running.
/// </summary>
/// <remarks>
/// <para>
/// One control with a switch rather than a class per environment. Each renderer is a few dozen
/// lines of drawing against public state the environment already exposes, and the alternative —
/// a renderer interface, a registry, a factory — would be more indirection than drawing code.
/// </para>
/// <para>
/// Everything is drawn in <see cref="Render"/> rather than assembled from shapes. The old
/// version of this app rebuilt a canvas full of shape controls every frame, which is thousands
/// of visuals created and thrown away a second; drawing straight into the context has no such
/// cost and is what makes the console usable at high step rates.
/// </para>
/// </remarks>
public sealed class WorldView : Control
{
    public static readonly StyledProperty<object?> WorldProperty =
        AvaloniaProperty.Register<WorldView, object?>(nameof(World));

    static WorldView() => AffectsRender<WorldView>(WorldProperty);

    /// <summary>The environment to draw.</summary>
    public object? World
    {
        get => GetValue(WorldProperty);
        set => SetValue(WorldProperty, value);
    }

    private static readonly Color Amber = Color.Parse("#F0A93B");
    private static readonly Color Teal = Color.Parse("#4FC3B0");
    private static readonly Color Rust = Color.Parse("#E0674F");
    private static readonly Color Violet = Color.Parse("#8B7FD4");

    private static readonly IBrush AmberBrush = new SolidColorBrush(Amber);
    private static readonly IBrush TealBrush = new SolidColorBrush(Teal);
    private static readonly IBrush RustBrush = new SolidColorBrush(Rust);
    private static readonly IBrush InkBrush = new SolidColorBrush(Color.Parse("#DCE6F0"));
    private static readonly IBrush DimBrush = new SolidColorBrush(Color.Parse("#7E92A8"));
    private static readonly IBrush RuleBrush = new SolidColorBrush(Color.Parse("#253243"));
    private static readonly IBrush GroundBrush = new SolidColorBrush(Color.Parse("#101A25"));

    private static readonly Pen RulePen = new(RuleBrush, 1);

    private static readonly Typeface LabelTypeface = new(
        new FontFamily("Inter, Segoe UI Variable Text, Segoe UI, SF Pro Text, Ubuntu, sans-serif"),
        FontStyle.Normal, FontWeight.SemiBold);

    private static readonly Typeface ReadoutTypeface = new(
        new FontFamily("Cascadia Mono, Consolas, SF Mono, DejaVu Sans Mono, monospace"));

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        if (bounds.Width < 8 || bounds.Height < 8) return;

        context.FillRectangle(GroundBrush, bounds);

        switch (World)
        {
            case GridWorldEnvironment grid: DrawGridWorld(context, bounds, grid); break;
            case CartPoleEnvironment cartPole: DrawCartPole(context, bounds, cartPole); break;
            case MountainCarEnvironment car: DrawMountainCar(context, bounds, car); break;
            case LunarLanderEnvironment lander: DrawLunarLander(context, bounds, lander); break;
            case PendulumEnvironment pendulum: DrawPendulum(context, bounds, pendulum); break;
            case ReacherEnvironment reacher: DrawReacher(context, bounds, reacher); break;
            case TradingEnvironment trading: DrawTrading(context, bounds, trading); break;
            case SupplyChainEnvironment supply: DrawSupplyChain(context, bounds, supply); break;
            case PredatorPreyEnvironment pursuit: DrawPredatorPrey(context, bounds, pursuit); break;
        }
    }

    // --- Classic ------------------------------------------------------------------------------

    private static void DrawGridWorld(DrawingContext context, Rect bounds, GridWorldEnvironment grid)
    {
        var area = Square(bounds, 0.82);
        double cell = area.Width / grid.Width;

        for (int y = 0; y < grid.Height; y++)
        {
            for (int x = 0; x < grid.Width; x++)
            {
                var rect = new Rect(area.X + x * cell, area.Y + y * cell, cell, cell);

                IBrush fill = grid.CellAt(x, y) switch
                {
                    GridCell.Goal => new SolidColorBrush(Teal, 0.22),
                    GridCell.Trap => new SolidColorBrush(Rust, 0.22),
                    GridCell.Wall => RuleBrush,
                    _ => Brushes.Transparent,
                };

                context.FillRectangle(fill, rect);
                context.DrawRectangle(RulePen, rect);
            }
        }

        var agent = new Rect(
            area.X + grid.AgentX * cell + cell * 0.22,
            area.Y + grid.AgentY * cell + cell * 0.22,
            cell * 0.56, cell * 0.56);

        context.DrawEllipse(AmberBrush, null, agent.Center, agent.Width / 2, agent.Height / 2);

        Caption(context, bounds, "GOAL", Teal, "TRAP", Rust, "AGENT", Amber);
    }

    private static void DrawCartPole(DrawingContext context, Rect bounds, CartPoleEnvironment cartPole)
    {
        const double worldWidth = 5.0;
        double scale = bounds.Width * 0.86 / worldWidth;
        double centreX = bounds.Width / 2;
        double trackY = bounds.Height * 0.68;

        // The track, with the failure bounds marked. Showing where the episode ends turns a
        // moving rectangle into a task with visible stakes.
        context.DrawLine(RulePen, new Point(bounds.Width * 0.05, trackY), new Point(bounds.Width * 0.95, trackY));

        foreach (double edge in new[] { -CartPoleEnvironment.PositionThreshold, CartPoleEnvironment.PositionThreshold })
        {
            double x = centreX + edge * scale;
            context.DrawLine(new Pen(RustBrush, 1, new DashStyle([3, 4], 0)),
                new Point(x, trackY - 40), new Point(x, trackY + 12));
        }

        double cartX = centreX + cartPole.CartPosition * scale;
        var cart = new Rect(cartX - 26, trackY - 15, 52, 22);
        context.FillRectangle(InkBrush, cart, 2);

        double poleLength = bounds.Height * 0.26;
        double tipX = cartX + Math.Sin(cartPole.PoleAngle) * poleLength;
        double tipY = trackY - 15 - Math.Cos(cartPole.PoleAngle) * poleLength;

        // The pole takes the amber: it is the thing under control, and colour follows meaning.
        context.DrawLine(new Pen(AmberBrush, 5, lineCap: PenLineCap.Round),
            new Point(cartX, trackY - 15), new Point(tipX, tipY));
        context.DrawEllipse(AmberBrush, null, new Point(tipX, tipY), 5, 5);

        Readout(context, bounds,
            $"x {cartPole.CartPosition,6:F2}   angle {cartPole.PoleAngle * 180 / Math.PI,6:F1}°");
    }

    private static void DrawMountainCar(DrawingContext context, Rect bounds, MountainCarEnvironment car)
    {
        const double minPosition = -1.2, maxPosition = 0.6;
        double left = bounds.Width * 0.07, right = bounds.Width * 0.93;
        double top = bounds.Height * 0.20, bottom = bounds.Height * 0.82;

        Point Project(double position)
        {
            double t = (position - minPosition) / (maxPosition - minPosition);
            double height = MountainCarEnvironment.TrackHeight(position);
            return new Point(left + t * (right - left), bottom - height * (bottom - top));
        }

        var hill = new StreamGeometry();
        using (var sink = hill.Open())
        {
            sink.BeginFigure(Project(minPosition), isFilled: false);
            for (double p = minPosition; p <= maxPosition; p += 0.01) sink.LineTo(Project(p));
            sink.EndFigure(false);
        }
        context.DrawGeometry(null, new Pen(RuleBrush, 2), hill);

        // The flag: the only thing in this environment worth reaching, and invisible in the
        // reward signal until it is touched.
        var goal = Project(0.5);
        context.DrawLine(new Pen(TealBrush, 2), goal, new Point(goal.X, goal.Y - 34));
        context.FillRectangle(TealBrush, new Rect(goal.X, goal.Y - 34, 16, 10));

        var position = Project(car.Position);
        context.DrawEllipse(AmberBrush, null, position, 7, 7);

        Readout(context, bounds, $"position {car.Position,6:F2}   velocity {car.Velocity,7:F4}");
    }

    private static void DrawLunarLander(DrawingContext context, Rect bounds, LunarLanderEnvironment lander)
    {
        double surfaceY = bounds.Height * 0.86;
        double scaleX = bounds.Width * 0.30;
        double scaleY = bounds.Height * 0.46;
        double centreX = bounds.Width / 2;

        context.DrawLine(new Pen(RuleBrush, 2), new Point(0, surfaceY), new Point(bounds.Width, surfaceY));

        double padHalf = LunarLanderEnvironment.PadHalfWidth * scaleX;
        context.DrawLine(new Pen(TealBrush, 3),
            new Point(centreX - padHalf, surfaceY), new Point(centreX + padHalf, surfaceY));

        double x = centreX + lander.X * scaleX;
        double y = surfaceY - lander.Y * scaleY;

        var transform = Matrix.CreateRotation(lander.Angle) * Matrix.CreateTranslation(x, y);
        using (context.PushTransform(transform))
        {
            context.FillRectangle(InkBrush, new Rect(-11, -9, 22, 18), 2);
            context.DrawLine(new Pen(DimBrush, 2), new Point(-9, 9), new Point(-14, 17));
            context.DrawLine(new Pen(DimBrush, 2), new Point(9, 9), new Point(14, 17));

            // Engine plumes only while firing: this is the clearest read on what the policy is
            // actually doing, moment to moment.
            if (lander.MainEngineFiring)
                context.FillRectangle(AmberBrush, new Rect(-4, 9, 8, 14), 2);
            if (lander.LeftThrusterFiring)
                context.FillRectangle(AmberBrush, new Rect(-19, -3, 8, 6), 2);
            if (lander.RightThrusterFiring)
                context.FillRectangle(AmberBrush, new Rect(11, -3, 8, 6), 2);
        }

        Readout(context, bounds,
            $"alt {lander.Y,5:F2}   tilt {lander.Angle * 180 / Math.PI,6:F1}°");
    }

    // --- Continuous control -------------------------------------------------------------------

    private static void DrawPendulum(DrawingContext context, Rect bounds, PendulumEnvironment pendulum)
    {
        var pivot = new Point(bounds.Width / 2, bounds.Height * 0.48);
        double length = Math.Min(bounds.Width, bounds.Height) * 0.28;

        // Upright is the target, so it gets a marker: without it the pendulum's angle is a number
        // with no visible reference.
        context.DrawLine(new Pen(RuleBrush, 1, new DashStyle([3, 4], 0)),
            pivot, new Point(pivot.X, pivot.Y - length - 16));

        // Angle 0 is straight up, and screen y grows downward.
        double bobX = pivot.X + Math.Sin(pendulum.Angle) * length;
        double bobY = pivot.Y - Math.Cos(pendulum.Angle) * length;

        context.DrawLine(new Pen(AmberBrush, 6, lineCap: PenLineCap.Round), pivot, new Point(bobX, bobY));
        context.DrawEllipse(AmberBrush, null, new Point(bobX, bobY), 13, 13);
        context.DrawEllipse(InkBrush, null, pivot, 4, 4);

        DrawTorqueArc(context, pivot, length, pendulum.LastTorque);

        Readout(context, bounds,
            $"angle {pendulum.Angle * 180 / Math.PI,7:F1}°   torque {pendulum.LastTorque,6:F2}");
    }

    /// <summary>
    /// Draws the applied torque as an arc around the pivot.
    /// </summary>
    /// <remarks>
    /// A continuous action has no natural shape, and a bare number does not convey that the
    /// policy is pushing gently rather than slamming the limit. The arc's length is the
    /// magnitude and its side is the sign, which is the whole action in one mark.
    /// </remarks>
    private static void DrawTorqueArc(DrawingContext context, Point pivot, double length, double torque)
    {
        if (Math.Abs(torque) < 0.02) return;

        double radius = length * 0.42;
        double sweep = Math.Clamp(torque / 2.0, -1, 1) * Math.PI * 0.6;

        var geometry = new StreamGeometry();
        using (var sink = geometry.Open())
        {
            sink.BeginFigure(new Point(pivot.X, pivot.Y - radius), isFilled: false);
            for (double t = 0; Math.Abs(t) <= Math.Abs(sweep); t += Math.Sign(sweep) * 0.05)
                sink.LineTo(new Point(pivot.X + Math.Sin(t) * radius, pivot.Y - Math.Cos(t) * radius));
            sink.EndFigure(false);
        }

        context.DrawGeometry(null, new Pen(new SolidColorBrush(Violet), 3, lineCap: PenLineCap.Round), geometry);
    }

    private static void DrawReacher(DrawingContext context, Rect bounds, ReacherEnvironment reacher)
    {
        var origin = new Point(bounds.Width / 2, bounds.Height / 2);
        double scale = Math.Min(bounds.Width, bounds.Height) * 0.36;

        Point Project(double x, double y) => new(origin.X + x * scale, origin.Y - y * scale);

        // The reachable annulus, so a target near the edge reads as hard rather than arbitrary.
        context.DrawEllipse(null, new Pen(RuleBrush, 1, new DashStyle([2, 5], 0)), origin, scale, scale);

        var (elbowX, elbowY) = reacher.ElbowPosition;
        var (tipX, tipY) = reacher.FingertipPosition;

        var elbow = Project(elbowX, elbowY);
        var tip = Project(tipX, tipY);

        context.DrawLine(new Pen(InkBrush, 6, lineCap: PenLineCap.Round), origin, elbow);
        context.DrawLine(new Pen(AmberBrush, 5, lineCap: PenLineCap.Round), elbow, tip);

        context.DrawEllipse(DimBrush, null, origin, 5, 5);
        context.DrawEllipse(DimBrush, null, elbow, 4, 4);
        context.DrawEllipse(AmberBrush, null, tip, 5, 5);

        var target = Project(reacher.TargetX, reacher.TargetY);
        context.DrawEllipse(null, new Pen(TealBrush, 2), target, 9, 9);
        context.DrawEllipse(TealBrush, null, target, 2.5, 2.5);

        Readout(context, bounds, $"distance {reacher.DistanceToTarget,6:F3}");
    }

    // --- Domain -------------------------------------------------------------------------------

    private static void DrawTrading(DrawingContext context, Rect bounds, TradingEnvironment trading)
    {
        var prices = trading.Prices;
        int visible = Math.Max(2, trading.CurrentStep + 1);

        double left = 14, right = bounds.Width - 14;
        double top = bounds.Height * 0.12, bottom = bounds.Height * 0.72;

        double low = double.MaxValue, high = double.MinValue;
        for (int i = 0; i < visible; i++)
        {
            low = Math.Min(low, prices[i]);
            high = Math.Max(high, prices[i]);
        }
        if (high - low < 1e-6) high = low + 1;

        var geometry = new StreamGeometry();
        using (var sink = geometry.Open())
        {
            for (int i = 0; i < visible; i++)
            {
                double x = left + (right - left) * i / (prices.Length - 1.0);
                double y = bottom - (prices[i] - low) / (high - low) * (bottom - top);
                if (i == 0) sink.BeginFigure(new Point(x, y), isFilled: false);
                else sink.LineTo(new Point(x, y));
            }
            sink.EndFigure(false);
        }
        context.DrawGeometry(null, new Pen(DimBrush, 1.4), geometry);

        double markerX = left + (right - left) * (visible - 1) / (prices.Length - 1.0);
        double markerY = bottom - (prices[visible - 1] - low) / (high - low) * (bottom - top);

        // Amber when holding stock, hollow when flat: position is the state that matters and it
        // is otherwise buried in a number.
        bool holding = trading.Shares > 0;
        context.DrawEllipse(holding ? AmberBrush : null, holding ? null : new Pen(AmberBrush, 2),
            new Point(markerX, markerY), 5, 5);

        // The benchmark is the point of the whole environment, so it is on the panel, not in a
        // log: beating buy-and-hold is the only result that means anything here.
        float edge = trading.NetWorth - trading.BuyAndHoldValue;
        Readout(context, bounds,
            $"net worth {trading.NetWorth,10:N0}   buy & hold {trading.BuyAndHoldValue,10:N0}   edge {edge,+9:N0}",
            edge >= 0 ? Teal : Rust);
    }

    private static void DrawSupplyChain(DrawingContext context, Rect bounds, SupplyChainEnvironment supply)
    {
        double baseline = bounds.Height * 0.68;
        double centreX = bounds.Width / 2;

        // Inventory as a column, the deliveries in transit queued behind it. The lead time is the
        // whole difficulty of the task, so it is drawn as physical distance.
        double stockHeight = Math.Min(bounds.Height * 0.42, supply.Inventory * 1.7);
        var stock = new Rect(centreX - 42, baseline - stockHeight, 84, stockHeight);
        context.FillRectangle(new SolidColorBrush(Amber, 0.85), stock, 2);

        context.DrawLine(new Pen(RuleBrush, 1),
            new Point(centreX - 150, baseline), new Point(centreX + 190, baseline));

        var pipeline = supply.Pipeline;
        for (int i = 0; i < pipeline.Length; i++)
        {
            double height = Math.Min(60, pipeline[i] * 1.1);
            var crate = new Rect(centreX + 62 + i * 34, baseline - height, 26, height);
            context.FillRectangle(new SolidColorBrush(Violet, 0.55), crate, 2);

            var label = new FormattedText(
                $"+{i + 1}d", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                LabelTypeface, 9, DimBrush);
            context.DrawText(label, new Point(crate.X + 2, baseline + 5));
        }

        // Lost sales are the failure mode this task exists to teach, so they get their own mark.
        if (supply.LastLostSales > 0)
        {
            var lost = new Rect(centreX - 150, baseline - supply.LastLostSales * 2.2, 26, supply.LastLostSales * 2.2);
            context.FillRectangle(new SolidColorBrush(Rust, 0.8), lost, 2);
        }

        Readout(context, bounds,
            $"day {supply.Day,3}   stock {supply.Inventory,4}   demand {supply.LastDemand,3}   " +
            $"lost {supply.LastLostSales,3}   profit {supply.CumulativeProfit,8:N0}",
            supply.LastLostSales > 0 ? Rust : Teal);
    }

    // --- Multi-agent --------------------------------------------------------------------------

    private static void DrawPredatorPrey(DrawingContext context, Rect bounds, PredatorPreyEnvironment pursuit)
    {
        var area = Square(bounds, 0.80);
        double cell = area.Width / pursuit.GridSize;

        for (int y = 0; y <= pursuit.GridSize; y++)
        {
            double v = area.Y + y * cell;
            context.DrawLine(RulePen, new Point(area.X, v), new Point(area.Right, v));
        }
        for (int x = 0; x <= pursuit.GridSize; x++)
        {
            double h = area.X + x * cell;
            context.DrawLine(RulePen, new Point(h, area.Y), new Point(h, area.Bottom));
        }

        var (preyX, preyY) = pursuit.Prey;
        var preyCentre = new Point(area.X + (preyX + 0.5) * cell, area.Y + (preyY + 0.5) * cell);
        context.DrawEllipse(TealBrush, null, preyCentre, cell * 0.26, cell * 0.26);

        for (int i = 0; i < 3 && i < 8; i++)
        {
            var (px, py) = pursuit.PredatorAt(i);
            var centre = new Point(area.X + (px + 0.5) * cell, area.Y + (py + 0.5) * cell);
            context.DrawEllipse(AmberBrush, null, centre, cell * 0.22, cell * 0.22);
        }

        Readout(context, bounds, $"captures this episode: {pursuit.Captures}");
        Caption(context, bounds, "PREDATOR", Amber, "PREY", Teal, null, default);
    }

    // --- Shared -------------------------------------------------------------------------------

    private static Rect Square(Rect bounds, double fraction)
    {
        double size = Math.Min(bounds.Width, bounds.Height) * fraction;
        return new Rect((bounds.Width - size) / 2, (bounds.Height - size) / 2, size, size);
    }

    // The two bottom overlays share the viewport's lower-left corner, so their rows are fixed
    // here rather than each computing its own - which is how they came to overlap.
    private const double ReadoutBaseline = 26;   // from the bottom edge
    private const double CaptionBaseline = 46;

    /// <summary>Draws the live state line along the bottom of the viewport.</summary>
    private static void Readout(DrawingContext context, Rect bounds, string text, Color? accent = null)
    {
        var brush = accent is null ? DimBrush : new SolidColorBrush(accent.Value);

        var formatted = new FormattedText(
            text, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            ReadoutTypeface, 12, brush);

        context.DrawText(formatted, new Point(14, bounds.Height - ReadoutBaseline));
    }

    /// <summary>Draws a small colour key, for the environments where colour carries meaning.</summary>
    private static void Caption(
        DrawingContext context, Rect bounds,
        string first, Color firstColor,
        string second, Color secondColor,
        string? third, Color thirdColor)
    {
        double x = 14;
        double y = bounds.Height - CaptionBaseline;

        void Entry(string label, Color color)
        {
            context.FillRectangle(new SolidColorBrush(color), new Rect(x, y + 3, 8, 8), 1);

            var text = new FormattedText(
                label, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                LabelTypeface, 9, DimBrush);

            context.DrawText(text, new Point(x + 13, y));
            x += 13 + text.Width + 18;
        }

        Entry(first, firstColor);
        Entry(second, secondColor);
        if (third is not null) Entry(third, thirdColor);
    }
}
