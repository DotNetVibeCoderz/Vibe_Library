using Lvgl.Interop;

namespace Lvgl.Widgets;

/// <summary>
/// A dialog with a title, text and buttons.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Show"/> creates a <b>modal</b> box: LVGL puts it on the top layer behind a dimmed
/// backdrop that swallows clicks, and the box closes itself (deleting itself and the backdrop)
/// when <see cref="Close"/> is called or its close button is pressed.
/// </para>
/// <para>
/// Message boxes are runtime UI, not layout: they are created in response to something
/// happening, so the designer and layout documents do not include them.
/// </para>
/// </remarks>
public sealed class LvMessageBox : LvObject
{
    private LvMessageBox(nint handle) : base(handle) { }

    /// <summary>Creates a non-modal message box inside <paramref name="parent"/>.</summary>
    public LvMessageBox(LvObject parent) : this(LvglNative.lv_msgbox_create(parent?.Handle ?? throw new ArgumentNullException(nameof(parent)))) { }

    /// <summary>
    /// Shows a modal message box with the given footer buttons.
    /// </summary>
    /// <param name="title">Heading; null or empty for none.</param>
    /// <param name="text">Body text.</param>
    /// <param name="buttons">Footer button captions, left to right.</param>
    /// <example>
    /// <code>
    /// var box = LvMessageBox.Show("Delete", "Remove this profile?", "Cancel", "Delete");
    /// box.Buttons[1].Clicked += (_, _) => { DeleteProfile(); box.Close(); };
    /// box.Buttons[0].Clicked += (_, _) => box.Close();
    /// </code>
    /// </example>
    public static LvMessageBox Show(string? title, string text, params string[] buttons)
    {
        LvglRuntime.EnsureUiThread();

        // NULL parent is what makes LVGL build the modal backdrop on the top layer.
        var box = new LvMessageBox(LvglNative.lv_msgbox_create(0));
        if (!string.IsNullOrEmpty(title)) box.AddTitle(title);
        if (!string.IsNullOrEmpty(text)) box.AddText(text);
        foreach (var caption in buttons) box.AddButton(caption);
        return box;
    }

    /// <summary>Footer buttons in the order they were added.</summary>
    public IReadOnlyList<LvObject> Buttons => _buttons;

    private readonly List<LvObject> _buttons = [];

    /// <summary>Sets the heading.</summary>
    public LvLabel AddTitle(string title) => new(LvglNative.lv_msgbox_add_title(Handle, title ?? string.Empty));

    /// <summary>Adds a paragraph to the body.</summary>
    public LvLabel AddText(string text) => new(LvglNative.lv_msgbox_add_text(Handle, text ?? string.Empty));

    /// <summary>Adds a footer button.</summary>
    public LvObject AddButton(string caption)
    {
        var button = Wrap(LvglNative.lv_msgbox_add_footer_button(Handle, caption ?? string.Empty));
        _buttons.Add(button);
        return button;
    }

    /// <summary>Adds the "X" button in the header that closes the box.</summary>
    public LvObject AddCloseButton() => Wrap(LvglNative.lv_msgbox_add_close_button(Handle));

    /// <summary>The body container, for adding custom widgets.</summary>
    public LvObject Content => Wrap(LvglNative.lv_msgbox_get_content(Handle));

    /// <summary>Closes the box, deleting it (and its modal backdrop, if any).</summary>
    public void Close()
    {
        if (!IsAlive) return;
        LvglNative.lv_msgbox_close(Handle);
    }

    protected override void OnDeleted() => _buttons.Clear();
}
