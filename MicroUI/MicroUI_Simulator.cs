using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using MicroUI.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MicroUI.Simulator
{
    // Custom Avalonia Control that acts as the Framebuffer
    public class SimulatorDisplay : Control
    {
        private MControl _root;
        
        public SimulatorDisplay(MControl root)
        {
            _root = root;
            
            // Enable Input
            this.Focusable = true;
            this.PointerPressed += OnPointerPressed;
            this.PointerReleased += OnPointerReleased;
            this.PointerMoved += OnPointerMoved;
            this.KeyDown += OnKeyDown;
            this.TextInput += OnTextInput;

            // Animation Loop (60 FPS)
            DispatcherTimer.Run(() => {
                _root.Update();
                InvalidateVisual(); // Request redraw
                return true;
            }, TimeSpan.FromMilliseconds(16));
        }

        private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            var point = e.GetPosition(this);
            _root.HandleInput(point.X, point.Y, TouchState.Down);
            this.Focus(); // Ensure Avalonia control has focus for keyboard input
        }

        private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            var point = e.GetPosition(this);
            _root.HandleInput(point.X, point.Y, TouchState.Up);
        }

        private void OnPointerMoved(object? sender, PointerEventArgs e)
        {
            var point = e.GetPosition(this);
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                _root.HandleInput(point.X, point.Y, TouchState.Down); // Treat drag as Down with move
        }
        
        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Back) _root.HandleKey("Back");
            if (e.Key == Key.Enter) _root.HandleKey("Enter");
        }

        private void OnTextInput(object? sender, TextInputEventArgs e)
        {
            if (!string.IsNullOrEmpty(e.Text))
                _root.HandleKey(e.Text);
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            // Draw Background
            context.FillRectangle(Brushes.Black, new Rect(0, 0, Bounds.Width, Bounds.Height));
            
            // Recursively draw MicroUI controls
            DrawControl(context, _root, 0, 0);
        }

        private void DrawControl(DrawingContext context, MControl control, double offsetX, double offsetY)
        {
            if (!control.Visible) return;

            double absX = offsetX + control.X;
            double absY = offsetY + control.Y;
            var rect = new Rect(absX, absY, control.Width, control.Height);

            // Convert Core Color to Avalonia Brush
            var brush = new SolidColorBrush(Avalonia.Media.Color.FromUInt32(
                (uint)((control.Background.A << 24) | (control.Background.R << 16) | (control.Background.G << 8) | control.Background.B)));

            // 1. Draw Standard Background
            if (!(control is MCheckBox || control is MRadioButton || control is MToggleSwitch || control is MSlider))
            {
                 context.FillRectangle(brush, rect);
            }
            
            // Border if focused
            if (control.IsFocused)
            {
                context.DrawRectangle(null, new Pen(Brushes.Yellow, 2), rect);
            }

            // 2. Draw Specifics
            if (control is MButton btn)
            {
                context.DrawRectangle(null, new Pen(Brushes.White, 2), rect);
                DrawTextCentered(context, btn.Text, rect, 16, Brushes.White);
            }
            else if (control is MLabel lbl)
            {
                 var text = new FormattedText(lbl.Text, System.Globalization.CultureInfo.CurrentCulture, 
                    FlowDirection.LeftToRight, Typeface.Default, lbl.FontSize, 
                    new SolidColorBrush(Avalonia.Media.Color.FromRgb(ThemeManager.TextColor.R, ThemeManager.TextColor.G, ThemeManager.TextColor.B)));
                 context.DrawText(text, new Point(absX, absY));
            }
            else if (control is MTextBlock txtBlock)
            {
                 var text = new FormattedText(txtBlock.Text, System.Globalization.CultureInfo.CurrentCulture, 
                    FlowDirection.LeftToRight, Typeface.Default, txtBlock.FontSize, 
                    new SolidColorBrush(Avalonia.Media.Color.FromRgb(ThemeManager.TextColor.R, ThemeManager.TextColor.G, ThemeManager.TextColor.B)));
                 text.MaxTextWidth = txtBlock.Width;
                 text.MaxTextHeight = txtBlock.Height;
                 context.DrawText(text, new Point(absX, absY));
            }
            else if (control is MCheckBox cb)
            {
                // Box
                var boxRect = new Rect(absX, absY + (cb.Height - 20)/2, 20, 20);
                context.DrawRectangle(Brushes.White, new Pen(Brushes.Gray, 1), boxRect);
                if (cb.Checked)
                {
                    context.DrawLine(new Pen(Brushes.Black, 2), boxRect.TopLeft + new Vector(4,4), boxRect.BottomRight - new Vector(4,4));
                    context.DrawLine(new Pen(Brushes.Black, 2), boxRect.TopRight + new Vector(-4,4), boxRect.BottomLeft - new Vector(-4,4));
                }
                
                // Text
                var textRect = new Rect(absX + 25, absY, cb.Width - 25, cb.Height);
                DrawTextLeft(context, cb.Text, textRect, 14, Brushes.White);
            }
            else if (control is MRadioButton rb)
            {
                // Circle
                double r = 10;
                var center = new Point(absX + r, absY + rb.Height/2);
                context.DrawEllipse(Brushes.White, new Pen(Brushes.Gray, 1), center, r, r);
                if (rb.Checked)
                {
                    context.DrawEllipse(Brushes.Black, null, center, r-4, r-4);
                }
                var textRect = new Rect(absX + 25, absY, rb.Width - 25, rb.Height);
                DrawTextLeft(context, rb.Text, textRect, 14, Brushes.White);
            }
            else if (control is MToggleSwitch ts)
            {
                // Track
                var trackRect = new Rect(absX, absY + 5, 50, 20);
                context.FillRectangle(ts.Checked ? Brushes.LightGreen : Brushes.Gray, trackRect, 10);
                
                // Thumb
                double thumbX = ts.Checked ? absX + 30 : absX;
                context.FillRectangle(Brushes.White, new Rect(thumbX, absY + 5, 20, 20), 10);
            }
            else if (control is MSlider sl)
            {
                // Track Line
                double midY = absY + sl.Height / 2;
                context.DrawLine(new Pen(Brushes.Gray, 4), new Point(absX, midY), new Point(absX + sl.Width, midY));
                
                // Thumb
                double pct = (sl.Value - sl.Min) / (sl.Max - sl.Min);
                double thumbX = absX + (pct * sl.Width);
                context.FillRectangle(Brushes.White, new Rect(thumbX - 10, midY - 10, 20, 20), 10);
            }
            else if (control is MProgressBar pb)
            {
                // Background
                context.FillRectangle(Brushes.Gray, rect);
                // Fill
                double pct = (pb.Value) / (pb.Max);
                if (pct > 1) pct = 1; if (pct < 0) pct = 0;
                context.FillRectangle(Brushes.LightGreen, new Rect(absX, absY, pb.Width * pct, pb.Height));
            }
            else if (control is MTextBox tb)
            {
                // Border
                context.DrawRectangle(null, new Pen(Brushes.Gray, 1), rect);
                string display = tb.Text;
                if (control is MPasswordBox) display = new string('*', tb.Text.Length);
                
                // Cursor if focused
                if (tb.IsFocused && (DateTime.Now.Millisecond % 1000 < 500)) display += "|";
                
                DrawTextLeft(context, display, rect, 14, Brushes.Black);
            }
            else if (control is MImage img)
            {
                // Placeholder for Image
                context.FillRectangle(Brushes.DarkGray, rect);
                context.DrawRectangle(null, new Pen(Brushes.White, 2), rect);
                DrawTextCentered(context, "IMG: " + img.Source, rect, 12, Brushes.White);
            }
            else if (control is MListBox lb)
            {
                context.DrawRectangle(null, new Pen(Brushes.Gray, 1), rect);
                int itemHeight = 25;
                // Clip needed in real engine, here we just don't draw outside roughly
                for (int i=0; i<lb.Items.Count; i++)
                {
                    double iy = absY + (i * itemHeight) - lb.ScrollOffset;
                    if (iy >= absY && iy < absY + lb.Height)
                    {
                        var itemRect = new Rect(absX, iy, lb.Width, itemHeight);
                        if (i == lb.SelectedIndex) context.FillRectangle(Brushes.Blue, itemRect);
                        DrawTextLeft(context, lb.Items[i], itemRect, 12, i == lb.SelectedIndex ? Brushes.White : Brushes.Black);
                    }
                }
            }
            else if (control is MComboBox cmb)
            {
                // Main Box
                context.DrawRectangle(null, new Pen(Brushes.Gray, 1), rect);
                string text = cmb.SelectedIndex >= 0 && cmb.SelectedIndex < cmb.Items.Count ? cmb.Items[cmb.SelectedIndex] : "";
                DrawTextLeft(context, text, rect, 14, Brushes.Black);
                // Arrow
                context.DrawLine(new Pen(Brushes.Black, 2), new Point(absX + cmb.Width - 20, absY + 10), new Point(absX + cmb.Width - 10, absY + 10));
                context.DrawLine(new Pen(Brushes.Black, 2), new Point(absX + cmb.Width - 15, absY + 20), new Point(absX + cmb.Width - 10, absY + 10)); // simple mark
            }
            else if (control is MGauge gauge)
            {
                 // Re-using previous gauge code logic
                context.DrawEllipse(null, new Pen(Brushes.Gray, 5), new Point(absX + gauge.Width/2, absY + gauge.Height/2), gauge.Width/2 - 10, gauge.Height/2 - 10);
                double angle = (gauge.Value / (gauge.Max - gauge.Min)) * 180;
                double rad = (angle + 180) * Math.PI / 180;
                double cx = absX + gauge.Width/2;
                double cy = absY + gauge.Height/2;
                double r = gauge.Width/2 - 20;
                double ex = cx + r * Math.Cos(rad);
                double ey = cy + r * Math.Sin(rad);
                context.DrawLine(new Pen(Brushes.Red, 3), new Point(cx, cy), new Point(ex, ey));
                 var text = new FormattedText(gauge.Title + $": {gauge.Value:F0}", System.Globalization.CultureInfo.CurrentCulture, 
                    FlowDirection.LeftToRight, Typeface.Default, 12, Brushes.White);
                 context.DrawText(text, new Point(cx - text.Width/2, cy + 20));
            }
            else if (control is MChart chart)
            {
                context.DrawLine(new Pen(Brushes.White, 1), new Point(absX, absY + chart.Height), new Point(absX + chart.Width, absY + chart.Height));
                context.DrawLine(new Pen(Brushes.White, 1), new Point(absX, absY), new Point(absX, absY + chart.Height));
                if (chart.DataPoints.Count > 1)
                {
                    double stepX = chart.Width / (chart.DataPoints.Count - 1);
                    double maxVal = chart.DataPoints.Max() == 0 ? 1 : chart.DataPoints.Max();
                    for (int i = 0; i < chart.DataPoints.Count - 1; i++)
                    {
                        double p1x = absX + (i * stepX);
                        double p1y = absY + chart.Height - ((chart.DataPoints[i] / maxVal) * (chart.Height - 10));
                        double p2x = absX + ((i + 1) * stepX);
                        double p2y = absY + chart.Height - ((chart.DataPoints[i + 1] / maxVal) * (chart.Height - 10));
                        context.DrawLine(new Pen(Brushes.Cyan, 2), new Point(p1x, p1y), new Point(p2x, p2y));
                    }
                }
            }

            // 3. Draw Children
            foreach (var child in control.Children)
            {
                DrawControl(context, child, absX, absY);
            }
            
            // 4. Overlay for ComboBox Expanded List (Simulated Z-Index by drawing last if expanded)
            if (control is MComboBox cmbOpen && cmbOpen.IsExpanded)
            {
                double dropY = absY + cmbOpen.Height;
                double dropHeight = cmbOpen.Items.Count * 25;
                var dropRect = new Rect(absX, dropY, cmbOpen.Width, dropHeight);
                
                context.FillRectangle(Brushes.White, dropRect);
                context.DrawRectangle(null, new Pen(Brushes.Black, 1), dropRect);
                
                for(int i=0; i<cmbOpen.Items.Count; i++)
                {
                    DrawTextLeft(context, cmbOpen.Items[i], new Rect(absX, dropY + (i*25), cmbOpen.Width, 25), 14, Brushes.Black);
                }
            }
        }
        
        private void DrawTextCentered(DrawingContext context, string text, Rect rect, double size, IBrush color)
        {
             var ft = new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture, 
                    FlowDirection.LeftToRight, Typeface.Default, size, color);
             context.DrawText(ft, new Point(rect.X + (rect.Width - ft.Width)/2, rect.Y + (rect.Height - ft.Height)/2));
        }

        private void DrawTextLeft(DrawingContext context, string text, Rect rect, double size, IBrush color)
        {
             var ft = new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture, 
                    FlowDirection.LeftToRight, Typeface.Default, size, color);
             context.DrawText(ft, new Point(rect.X + 5, rect.Y + (rect.Height - ft.Height)/2));
        }
    }

    public class SimulatorWindow : Window
    {
        private SimulatorDisplay _display;
        
        public SimulatorWindow(MControl rootUI)
        {
            this.Title = "MicroUI Simulator";
            this.Width = 800;
            this.Height = 480;
            this.Content = _display = new SimulatorDisplay(rootUI);
            this.Background = Brushes.Black;
        }

        public void SwitchScreen(MControl newRoot)
        {
            this.Content = _display = new SimulatorDisplay(newRoot);
        }
    }
}
