using Lvgl.Interop;

namespace Lvgl.Widgets;

/// <summary>A scrolling picker, well suited to touch input.</summary>
public sealed class LvRoller : LvObject
{
    private string[] _options = [];
    private LvRollerMode _mode = LvRollerMode.Normal;

    /// <summary>Creates a roller on <paramref name="parent"/>.</summary>
    public LvRoller(LvObject? parent) : base(LvglNative.lv_roller_create(ResolveParent(parent))) { }

    /// <summary>Creates a roller with options.</summary>
    public LvRoller(LvObject? parent, params string[] options) : this(parent) => Options = options;

    /// <summary>The selectable entries.</summary>
    public IReadOnlyList<string> Options
    {
        get => _options;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _options = value.ToArray();
            Apply();
        }
    }

    /// <summary>Whether the list wraps around when scrolled past its ends.</summary>
    public LvRollerMode Mode
    {
        get => _mode;
        set
        {
            _mode = value;
            Apply();
        }
    }

    /// <summary>Index of the selected entry.</summary>
    public int SelectedIndex => (int)LvglNative.lv_roller_get_selected(Handle);

    /// <summary>The selected entry, or an empty string when the list is empty.</summary>
    public string SelectedOption
    {
        get
        {
            var index = SelectedIndex;
            return (uint)index < (uint)_options.Length ? _options[index] : string.Empty;
        }
    }

    private void Apply() => LvglNative.lv_roller_set_options(Handle, string.Join('\n', _options), (int)_mode);
}
