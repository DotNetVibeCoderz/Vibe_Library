using Lvgl.Interop;

namespace Lvgl.Widgets;

/// <summary>A drop-down list.</summary>
public sealed class LvDropdown : LvObject
{
    private string[] _options = [];

    /// <summary>Creates a drop-down on <paramref name="parent"/>.</summary>
    public LvDropdown(LvObject? parent) : base(LvglNative.lv_dropdown_create(ResolveParent(parent))) { }

    /// <summary>Creates a drop-down with options.</summary>
    public LvDropdown(LvObject? parent, params string[] options) : this(parent) => Options = options;

    /// <summary>
    /// The selectable entries. LVGL takes them as a single newline-separated string, so entries
    /// containing a newline are rejected rather than silently splitting into two rows.
    /// </summary>
    public IReadOnlyList<string> Options
    {
        get => _options;
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            var options = value.ToArray();
            foreach (var option in options)
            {
                if (option.Contains('\n'))
                {
                    throw new ArgumentException("A drop-down option cannot contain a newline.", nameof(value));
                }
            }

            _options = options;
            LvglNative.lv_dropdown_set_options(Handle, string.Join('\n', options));
        }
    }

    /// <summary>Index of the selected entry.</summary>
    public int SelectedIndex
    {
        get => (int)LvglNative.lv_dropdown_get_selected(Handle);
        set => LvglNative.lv_dropdown_set_selected(Handle, (uint)value);
    }

    /// <summary>The selected entry, or an empty string when the list is empty.</summary>
    public string SelectedOption
    {
        get
        {
            var index = SelectedIndex;
            return (uint)index < (uint)_options.Length ? _options[index] : string.Empty;
        }
    }
}
