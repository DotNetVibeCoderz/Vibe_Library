using Lvgl.Interop;

namespace Lvgl.Widgets;

/// <summary>
/// An on-screen keyboard that types into an <see cref="LvTextArea"/> - text entry on a touch
/// panel with no physical keyboard.
/// </summary>
/// <remarks>
/// <para>
/// A common pattern is one keyboard per screen, hidden until a text area is focused:
/// </para>
/// <code>
/// var keyboard = new LvKeyboard(screen) { IsHidden = true };
/// field.AddHandler(LvEventCode.Focused, (_, _) => { keyboard.TextArea = field; keyboard.IsHidden = false; });
/// keyboard.AddHandler(LvEventCode.Ready, (_, _) => keyboard.IsHidden = true);
/// </code>
/// <para>
/// The keyboard raises <see cref="LvEventCode.Ready"/> for its OK key and
/// <see cref="LvEventCode.Cancel"/> for its close key.
/// </para>
/// </remarks>
public sealed class LvKeyboard : LvObject
{
    private LvTextArea? _textArea;

    /// <summary>Creates a keyboard on <paramref name="parent"/>, docked to the bottom by LVGL's theme.</summary>
    public LvKeyboard(LvObject? parent) : base(LvglNative.lv_keyboard_create(ResolveParent(parent))) { }

    /// <summary>The field the keys type into; null detaches the keyboard.</summary>
    public LvTextArea? TextArea
    {
        get => _textArea is { IsAlive: true } ? _textArea : null;
        set
        {
            LvglNative.lv_keyboard_set_textarea(Handle, value?.Handle ?? 0);
            _textArea = value;
        }
    }

    /// <summary>Key layout: letters, symbols or digits.</summary>
    public LvKeyboardMode Mode
    {
        get => (LvKeyboardMode)LvglNative.lv_keyboard_get_mode(Handle);
        set => LvglNative.lv_keyboard_set_mode(Handle, (int)value);
    }

    /// <summary>Shows an enlarged key above the finger while pressing - helpful on small panels.</summary>
    public bool ShowPopovers
    {
        set => LvglNative.lv_keyboard_set_popovers(Handle, value);
    }

    protected override void OnDeleted() => _textArea = null;
}
