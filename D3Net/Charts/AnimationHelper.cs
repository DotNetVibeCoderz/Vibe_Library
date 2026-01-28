using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Styling;
using System;
using System.Threading.Tasks;

namespace D3Net.Charts
{
    /// <summary>
    /// Helper class untuk animasi sederhana
    /// </summary>
    public static class AnimationHelper
    {
        /// <summary>
        /// Fade in animation untuk control
        /// </summary>
        public static async Task FadeIn(Control control, int delayMs = 0, int durationMs = 400)
        {
            if (delayMs > 0)
                await Task.Delay(delayMs);

            control.Opacity = 0;
            
            // Simple timer-based animation
            var steps = 20;
            var stepDelay = durationMs / steps;
            
            for (int i = 0; i <= steps; i++)
            {
                control.Opacity = (double)i / steps;
                await Task.Delay(stepDelay);
            }
        }

        /// <summary>
        /// Scale animation untuk control
        /// </summary>
        public static async Task ScaleIn(Control control, int delayMs = 0, int durationMs = 600)
        {
            if (delayMs > 0)
                await Task.Delay(delayMs);

            control.Opacity = 0;
            control.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
            
            var steps = 20;
            var stepDelay = durationMs / steps;
            
            for (int i = 0; i <= steps; i++)
            {
                double progress = (double)i / steps;
                // Ease out effect
                double eased = 1 - Math.Pow(1 - progress, 3);
                control.Opacity = eased;
                await Task.Delay(stepDelay);
            }
        }
    }
}
