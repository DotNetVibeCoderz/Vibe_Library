using Lvgl.Interop;

namespace Lvgl.Events;

/// <summary>Payload handed to LVGL event handlers.</summary>
public sealed class LvEventArgs(LvEventCode code, nint nativeEvent, nint target) : EventArgs
{
    /// <summary>Which event fired.</summary>
    public LvEventCode Code { get; } = code;

    /// <summary>
    /// Native <c>lv_event_t*</c>. Valid only for the duration of the handler; useful when calling
    /// an LVGL API that LVGL.Net does not wrap yet.
    /// </summary>
    public nint NativeEvent { get; } = nativeEvent;

    /// <summary>Native handle of the object the event is delivered to.</summary>
    public nint Target { get; } = target;
}

/// <summary>
/// Translates between LVGL.Net's stable <see cref="LvEventCode"/> values and the numeric
/// <c>lv_event_code_t</c> of the loaded LVGL build.
/// </summary>
/// <remarks>
/// LVGL inserted new members into the middle of <c>lv_event_code_t</c> during the 9.x series, so
/// its ordinals are not a stable ABI. The mapping is therefore resolved once at run time through
/// the native shim rather than baked into this assembly.
/// </remarks>
internal static class LvEventCodes
{
    private static readonly Lock Gate = new();
    private static Dictionary<int, LvEventCode>? _fromNative;
    private static int[]? _toNative;

    /// <summary>Native code for a stable identifier, or -1 when this build lacks it.</summary>
    internal static int ToNative(LvEventCode code)
    {
        EnsureBuilt();
        var index = (int)code;
        return (uint)index < (uint)_toNative!.Length ? _toNative[index] : -1;
    }

    internal static bool TryFromNative(int nativeCode, out LvEventCode code)
    {
        EnsureBuilt();
        return _fromNative!.TryGetValue(nativeCode, out code);
    }

    private static void EnsureBuilt()
    {
        if (_toNative is not null) return;

        lock (Gate)
        {
            if (_toNative is not null) return;

            var values = Enum.GetValues<LvEventCode>();
            var max = 0;
            foreach (var value in values) max = Math.Max(max, (int)value);

            var toNative = new int[max + 1];
            Array.Fill(toNative, -1);
            var fromNative = new Dictionary<int, LvEventCode>(values.Length);

            foreach (var value in values)
            {
                var native = LvglNative.lvn_event_code((int)value);
                toNative[(int)value] = native;

                // LV_EVENT_ALL is 0 and overlaps nothing; later duplicates would only occur if a
                // future LVGL merged two codes, in which case first-wins is the safer choice.
                if (native >= 0) fromNative.TryAdd(native, value);
            }

            _fromNative = fromNative;
            _toNative = toNative;
        }
    }
}
