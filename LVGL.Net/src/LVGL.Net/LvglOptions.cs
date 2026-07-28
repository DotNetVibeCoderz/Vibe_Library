namespace Lvgl;

/// <summary>Tuning knobs for <see cref="LvglApplication"/>.</summary>
public sealed class LvglOptions
{
    /// <summary>
    /// How LVGL hands rendered pixels to the backend. <see cref="LvRenderMode.Partial"/> keeps
    /// memory use to <see cref="PartialBufferLines"/> scan lines and is the right default for
    /// embedded targets; <see cref="LvRenderMode.Direct"/> trades a full-screen buffer for fewer
    /// blit calls per frame.
    /// </summary>
    public LvRenderMode RenderMode { get; set; } = LvRenderMode.Partial;

    /// <summary>
    /// Scan lines per draw buffer in partial mode. Two buffers of this size are allocated. The
    /// default of 0 means "one tenth of the screen height", LVGL's own recommendation.
    /// </summary>
    public int PartialBufferLines { get; set; }

    /// <summary>Create a pointer input device from the backend. Enabled by default.</summary>
    public bool EnablePointer { get; set; } = true;

    /// <summary>
    /// Create a keypad input device. Requires the backend to implement
    /// <see cref="Backends.ILvglKeyboardSource"/>.
    /// </summary>
    public bool EnableKeyboard { get; set; }

    /// <summary>
    /// Upper bound on how long the run loop sleeps between iterations, in milliseconds. LVGL may
    /// report that nothing is due for a long time; capping the sleep keeps input responsive.
    /// </summary>
    public int MaxIdleSleepMs { get; set; } = 16;

    /// <summary>
    /// Lower bound on the sleep between iterations. Zero busy-waits, which burns a core for
    /// nothing on a Pi, so the default yields for a millisecond instead.
    /// </summary>
    public int MinIdleSleepMs { get; set; } = 1;

    internal int ResolveBufferLines(int screenHeight) =>
        PartialBufferLines > 0 ? PartialBufferLines : Math.Max(10, screenHeight / 10);
}
