using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Lvgl.Designer;

/// <summary>
/// A one-field modal prompt, built in code because a XAML window for a single text box would be
/// more ceremony than the thing it asks for.
/// </summary>
internal sealed class PromptWindow : Window
{
    private readonly TextBox _input;

    private PromptWindow(string title, string message, string initialValue)
    {
        Title = title;
        Width = 420;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Background = new SolidColorBrush(Color.FromRgb(0x1B, 0x1F, 0x27));

        _input = new TextBox { Text = initialValue, Margin = new Thickness(0, 8, 0, 12) };

        var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 80 };
        ok.Click += (_, _) => { DialogResult = true; };

        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 80, Margin = new Thickness(0) };
        cancel.Click += (_, _) => { DialogResult = false; };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var layout = new StackPanel { Margin = new Thickness(16) };
        layout.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
        layout.Children.Add(_input);
        layout.Children.Add(buttons);

        Content = layout;

        Loaded += (_, _) =>
        {
            _input.SelectAll();
            _input.Focus();
        };
    }

    /// <summary>
    /// Shows the prompt.
    /// </summary>
    /// <returns>The entered text, or null when the user cancelled.</returns>
    public static string? Show(Window owner, string title, string message, string initialValue = "")
    {
        var window = new PromptWindow(title, message, initialValue) { Owner = owner };
        return window.ShowDialog() == true ? window._input.Text : null;
    }
}
