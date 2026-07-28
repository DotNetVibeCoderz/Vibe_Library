using Lvgl.Interop;

namespace Lvgl.Widgets;

/// <summary>
/// A push button.
/// </summary>
/// <remarks>
/// LVGL buttons hold no text of their own - the convention is a centred child label. <see cref="Text"/>
/// creates and maintains that label so callers do not have to.
/// </remarks>
public sealed class LvButton : LvObject
{
    private LvLabel? _label;

    /// <summary>Creates a button on <paramref name="parent"/>.</summary>
    public LvButton(LvObject? parent) : base(LvglNative.lv_button_create(ResolveParent(parent))) { }

    /// <summary>Creates a button with a caption.</summary>
    public LvButton(LvObject? parent, string text) : this(parent) => Text = text;

    /// <summary>Caption text. Setting it creates a centred child label on first use.</summary>
    public string Text
    {
        get => _label?.Text ?? string.Empty;
        set
        {
            if (_label is null)
            {
                _label = new LvLabel(this);
                _label.Center();
            }
            _label.Text = value ?? string.Empty;
        }
    }

    /// <summary>The child label backing <see cref="Text"/>, or <see langword="null"/> if unused.</summary>
    public LvLabel? Label => _label;

    /// <summary>
    /// Makes the button latch between checked and unchecked instead of firing once per press.
    /// Read the result through <see cref="LvObject.IsChecked"/>.
    /// </summary>
    public bool IsToggle
    {
        get => HasFlag(LvObjFlag.Checkable);
        set => SetFlag(LvObjFlag.Checkable, value);
    }

    protected override void OnDeleted() => _label = null;
}
