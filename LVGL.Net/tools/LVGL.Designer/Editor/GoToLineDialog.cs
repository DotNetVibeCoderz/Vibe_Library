using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Lvgl.Designer.Editor;

/// <summary>
/// The go-to-line prompt.
/// </summary>
/// <remarks>
/// Built in code rather than XAML: it is one label, one text box and two buttons, and a second
/// XAML file plus its generated partial would be more ceremony than the thing it asks for.
/// </remarks>
internal sealed class GoToLineDialog : Window
{
    private readonly TextBox _input;

    private GoToLineDialog(Window owner, int currentLine, int totalLines)
    {
        Owner = owner;
        Title = "Go to line";
        Width = 320;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Background = new SolidColorBrush(Color.FromRgb(0x1B, 0x1F, 0x27));

        _input = new TextBox
        {
            Text = currentLine.ToString(CultureInfo.InvariantCulture),
            Margin = new Thickness(0, 8, 0, 12),
        };

        var ok = new Button { Content = "Go", IsDefault = true, MinWidth = 76 };
        ok.Click += (_, _) => { DialogResult = true; };

        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 76, Margin = new Thickness(0) };
        cancel.Click += (_, _) => { DialogResult = false; };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var layout = new StackPanel { Margin = new Thickness(14) };
        layout.Children.Add(new TextBlock
        {
            Text = $"Line number (1 to {totalLines}):",
            Foreground = new SolidColorBrush(Color.FromRgb(0xE7, 0xED, 0xF5)),
        });
        layout.Children.Add(_input);
        layout.Children.Add(buttons);

        Content = layout;

        Loaded += (_, _) =>
        {
            _input.SelectAll();
            _input.Focus();
        };
    }

    /// <summary>Asks for a line number. Returns null when cancelled or unparseable.</summary>
    public static int? Ask(Window owner, int currentLine, int totalLines)
    {
        var dialog = new GoToLineDialog(owner, currentLine, totalLines);
        if (dialog.ShowDialog() != true) return null;

        return int.TryParse(dialog._input.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var line)
            ? line
            : null;
    }
}
