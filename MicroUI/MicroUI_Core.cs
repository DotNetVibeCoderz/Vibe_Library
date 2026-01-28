using System;
using System.Collections.Generic;
using System.Linq;

namespace MicroUI.Core
{
    // ==========================================
    // ENUMS & TYPES
    // ==========================================
    public enum ThemeType { Metro, Material, Modern }
    public enum TouchState { None, Down, Up, Move }

    public struct Color { 
        public byte R, G, B, A; 
        public static Color FromArgb(byte a, byte r, byte g, byte b) => new Color { A = a, R = r, G = g, B = b };
        public static Color Black => FromArgb(255, 0, 0, 0);
        public static Color White => FromArgb(255, 255, 255, 255);
        public static Color Red => FromArgb(255, 255, 0, 0);
        public static Color Blue => FromArgb(255, 0, 0, 255);
        public static Color Green => FromArgb(255, 0, 255, 0);
        public static Color Gray => FromArgb(255, 128, 128, 128);
        public static Color DarkGray => FromArgb(255, 64, 64, 64);
        public static Color LightGray => FromArgb(255, 200, 200, 200);
        public static Color Transparent => FromArgb(0, 0, 0, 0);
    }

    // ==========================================
    // THEME MANAGER
    // ==========================================
    public static class ThemeManager
    {
        public static ThemeType CurrentTheme { get; set; } = ThemeType.Modern;

        public static Color PrimaryColor
        {
            get
            {
                switch (CurrentTheme)
                {
                    case ThemeType.Metro: return Color.FromArgb(255, 0, 120, 215); // Blue
                    case ThemeType.Material: return Color.FromArgb(255, 98, 0, 238); // Deep Purple
                    case ThemeType.Modern: return Color.FromArgb(255, 0, 150, 136); // Teal
                    default: return Color.Black;
                }
            }
        }

        public static Color BackgroundColor
        {
            get
            {
                switch (CurrentTheme)
                {
                    case ThemeType.Metro: return Color.White;
                    case ThemeType.Material: return Color.FromArgb(255, 250, 250, 250);
                    case ThemeType.Modern: return Color.FromArgb(255, 30, 30, 30);
                    default: return Color.White;
                }
            }
        }
        
        public static Color TextColor
        {
            get
            {
                return CurrentTheme == ThemeType.Modern ? Color.White : Color.Black;
            }
        }
    }

    // ==========================================
    // BASE CONTROL
    // ==========================================
    public abstract class MControl
    {
        public string Name { get; set; } = "";
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public Color Background { get; set; } = Color.Transparent;
        public bool Visible { get; set; } = true;
        public bool IsFocused { get; set; } = false;
        
        public List<MControl> Children { get; set; } = new List<MControl>();
        public MControl? Parent { get; set; }

        // Input Events
        public event Action<MControl>? OnClick;
        public event Action<MControl>? OnTouchDown;
        public event Action<MControl>? OnTouchUp;
        public event Action<MControl, string>? OnTextChanged;
        public event Action<MControl, bool>? OnCheckedChanged;
        public event Action<MControl, double>? OnValueChanged;
        
        // Animation properties
        public double TargetX { get; set; }
        public double TargetY { get; set; }
        private bool _isAnimating = false;

        public MControl()
        {
            // Default init
        }

        public virtual void Update()
        {
            if (_isAnimating)
            {
                double speed = 0.2;
                if (Math.Abs(X - TargetX) > 0.5) X += (TargetX - X) * speed;
                if (Math.Abs(Y - TargetY) > 0.5) Y += (TargetY - Y) * speed;
            }
            
            foreach(var child in Children.ToList()) // ToList to avoid modification errors
            {
                child.Update();
            }
        }

        public void AnimateTo(double x, double y)
        {
            TargetX = x;
            TargetY = y;
            _isAnimating = true;
        }

        // Global focus management
        public static MControl? FocusedControl { get; set; }

        public virtual void HandleInput(double tx, double ty, TouchState state)
        {
            if (!Visible) return;

            // Check collision
            bool hit = (tx >= X && tx <= X + Width && ty >= Y && ty <= Y + Height);

            if (hit)
            {
                if (state == TouchState.Down)
                {
                    // Focus Management
                    if (FocusedControl != null && FocusedControl != this) FocusedControl.IsFocused = false;
                    FocusedControl = this;
                    IsFocused = true;

                    OnTouchDown?.Invoke(this);
                } 
                if (state == TouchState.Up)
                {
                    OnTouchUp?.Invoke(this);
                    OnClick?.Invoke(this);
                }
            }
            else if (state == TouchState.Down && IsFocused)
            {
                // Click outside loses focus
                IsFocused = false;
                if (FocusedControl == this) FocusedControl = null;
            }

            // Propagate to children (reverse order to hit top-most first)
            for (int i = Children.Count - 1; i >= 0; i--)
            {
                Children[i].HandleInput(tx - X, ty - Y, state); // Relative coords
            }
        }

        public virtual void HandleKey(string key)
        {
            // Propagate to focused child or handle if self is focused
            if (IsFocused) 
            {
                OnKeyDown(key);
            }
            foreach(var child in Children) child.HandleKey(key);
        }

        protected virtual void OnKeyDown(string key) { }

        public void Add(MControl child)
        {
            child.Parent = this;
            Children.Add(child);
        }
    }

    // ==========================================
    // CONTROLS IMPLEMENTATION
    // ==========================================
    
    public class MPanel : MControl
    {
        public MPanel() { Background = ThemeManager.BackgroundColor; }
    }

    public class MButton : MControl
    {
        public string Text { get; set; } = "Button";
        public MButton() 
        { 
            Width = 100; Height = 40; 
            Background = ThemeManager.PrimaryColor;
        }
    }

    public class MLabel : MControl
    {
        public string Text { get; set; } = "Label";
        public double FontSize { get; set; } = 14;
        public MLabel() { Background = Color.Transparent; Height = 25; Width = 100; }
    }
    
    public class MTextBlock : MControl
    {
        public string Text { get; set; } = "TextBlock content...";
        public double FontSize { get; set; } = 14;
        public MTextBlock() { Background = Color.Transparent; Height = 100; Width = 200; }
    }

    public class MGauge : MControl
    {
        public double Value { get; set; } = 0;
        public double Min { get; set; } = 0;
        public double Max { get; set; } = 100;
        public string Title { get; set; } = "Gauge";
        public MGauge() { Width = 150; Height = 150; Background = Color.Transparent; }
    }

    public class MChart : MControl
    {
        public List<double> DataPoints { get; set; } = new List<double>();
        public string Title { get; set; } = "Chart";
        public MChart() { Width = 200; Height = 150; Background = Color.FromArgb(20, 0,0,0); }
    }

    public class MCheckBox : MControl
    {
        public string Text { get; set; } = "Check";
        public bool Checked { get; set; } = false;
        public MCheckBox() { Width = 120; Height = 30; Background = Color.Transparent; }
        
        public override void HandleInput(double tx, double ty, TouchState state)
        {
            base.HandleInput(tx, ty, state);
            if (state == TouchState.Up && tx >= 0 && tx <= Width && ty >= 0 && ty <= Height)
            {
                Checked = !Checked;
                // Raise Event
            }
        }
    }

    public class MRadioButton : MControl
    {
        public string Text { get; set; } = "Radio";
        public bool Checked { get; set; } = false;
        public string GroupName { get; set; } = "Group1";
        public MRadioButton() { Width = 120; Height = 30; Background = Color.Transparent; }

        public override void HandleInput(double tx, double ty, TouchState state)
        {
            base.HandleInput(tx, ty, state);
             if (state == TouchState.Up && tx >= 0 && tx <= Width && ty >= 0 && ty <= Height)
            {
                if (!Checked)
                {
                    Checked = true;
                    // Uncheck others in parent
                    if (Parent != null)
                    {
                        foreach(var child in Parent.Children)
                        {
                            if (child is MRadioButton rb && rb != this && rb.GroupName == this.GroupName)
                            {
                                rb.Checked = false;
                            }
                        }
                    }
                }
            }
        }
    }

    public class MTextBox : MControl
    {
        public string Text { get; set; } = "";
        public MTextBox() { Width = 150; Height = 30; Background = Color.White; }

        protected override void OnKeyDown(string key)
        {
            if (key == "Back")
            {
                if (Text.Length > 0) Text = Text.Substring(0, Text.Length - 1);
            }
            else if (key.Length == 1) // Simple char input
            {
                Text += key;
            }
        }
    }

    public class MPasswordBox : MTextBox
    {
        public MPasswordBox() { Background = Color.White; }
    }

    public class MProgressBar : MControl
    {
        public double Value { get; set; } = 50;
        public double Max { get; set; } = 100;
        public MProgressBar() { Width = 200; Height = 20; Background = Color.LightGray; }
    }

    public class MSlider : MControl
    {
        public double Value { get; set; } = 0;
        public double Min { get; set; } = 0;
        public double Max { get; set; } = 100;
        private bool _dragging = false;

        public MSlider() { Width = 200; Height = 30; Background = Color.Transparent; }

        public override void HandleInput(double tx, double ty, TouchState state)
        {
            base.HandleInput(tx, ty, state);
            
            bool hit = (tx >= 0 && tx <= Width && ty >= 0 && ty <= Height);
            
            if (state == TouchState.Down && hit) _dragging = true;
            if (state == TouchState.Up) _dragging = false;

            if (_dragging)
            {
                double pct = Math.Clamp(tx / Width, 0, 1);
                Value = Min + (pct * (Max - Min));
            }
        }
    }

    public class MToggleSwitch : MControl
    {
        public bool Checked { get; set; } = false;
        public MToggleSwitch() { Width = 60; Height = 30; Background = Color.Transparent; }

        public override void HandleInput(double tx, double ty, TouchState state)
        {
             base.HandleInput(tx, ty, state);
             if (state == TouchState.Up && tx >= 0 && tx <= Width && ty >= 0 && ty <= Height)
             {
                 Checked = !Checked;
             }
        }
    }
    
    public class MImage : MControl
    {
        public string Source { get; set; } = "";
        public MImage() { Width = 100; Height = 100; Background = Color.Gray; }
    }

    public class MListBox : MControl
    {
        public List<string> Items { get; set; } = new List<string>();
        public int SelectedIndex { get; set; } = -1;
        public double ScrollOffset { get; set; } = 0;

        public MListBox() { Width = 150; Height = 100; Background = Color.White; }

        public override void HandleInput(double tx, double ty, TouchState state)
        {
            base.HandleInput(tx, ty, state);
            if (state == TouchState.Up && tx >= 0 && tx <= Width && ty >= 0 && ty <= Height)
            {
                // Simple selection logic
                int itemHeight = 25;
                int idx = (int)((ty + ScrollOffset) / itemHeight);
                if (idx >= 0 && idx < Items.Count) SelectedIndex = idx;
            }
        }
    }

    public class MComboBox : MControl
    {
        public List<string> Items { get; set; } = new List<string>();
        public int SelectedIndex { get; set; } = -1;
        public bool IsExpanded { get; set; } = false;

        public MComboBox() { Width = 150; Height = 30; Background = Color.White; }

        public override void HandleInput(double tx, double ty, TouchState state)
        {
            // If expanded, check click on dropdown list (simulated by extending height check roughly)
            if (IsExpanded && state == TouchState.Up)
            {
                // Check if click was on the list part
                if (ty > Height && ty < Height + (Items.Count * 25)) 
                {
                    int idx = (int)((ty - Height) / 25);
                    if (idx >= 0 && idx < Items.Count) 
                    {
                        SelectedIndex = idx;
                        IsExpanded = false;
                        return;
                    }
                }
            }

            base.HandleInput(tx, ty, state);
            
            if (state == TouchState.Up && tx >= 0 && tx <= Width && ty >= 0 && ty <= Height)
            {
                IsExpanded = !IsExpanded;
                // If expanded, bring to front? In simple renderer, we just draw last.
            }
        }
    }

    // Modal Message Box Helper
    public static class MessageBox
    {
        public static void Show(string title, string message, MControl root, Action? onClose = null)
        {
            var overlay = new MPanel { X = 0, Y = 0, Width = 800, Height = 480, Background = Color.FromArgb(100, 0, 0, 0) };
            
            var box = new MPanel { Width = 300, Height = 150, Background = Color.White };
            box.X = (800 - 300) / 2;
            box.Y = (480 - 150) / 2;
            
            box.Add(new MLabel { Text = title, X = 10, Y = 10, Width = 280, FontSize = 16 });
            box.Add(new MLabel { Text = message, X = 10, Y = 50, Width = 280 });
            
            var btn = new MButton { Text = "OK", X = 100, Y = 100, Width = 100 };
            btn.OnClick += (s) => {
                root.Children.Remove(overlay);
                onClose?.Invoke();
            };
            
            box.Add(btn);
            overlay.Add(box);
            root.Add(overlay);
        }
    }
}
