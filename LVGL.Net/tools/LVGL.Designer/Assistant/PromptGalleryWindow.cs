using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Lvgl.Assistant;

namespace Lvgl.Designer.Assistant;

/// <summary>
/// A picker for the built-in prompt templates.
/// </summary>
/// <remarks>
/// Built in code rather than XAML: it is a list and a preview pane, and a second XAML file plus
/// its generated partial would be more ceremony than the thing it shows.
/// </remarks>
internal sealed class PromptGalleryWindow : Window
{
    private readonly ListBox _list;
    private readonly TextBox _preview;

    private PromptGalleryWindow(Window owner)
    {
        Owner = owner;
        Title = "Prompt templates";
        Width = 900;
        Height = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0x1B, 0x1F, 0x27));

        _list = new ListBox
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = (Brush)owner.FindResource("Ink"),
            Margin = new Thickness(0, 0, 10, 0),
            ItemsSource = PromptTemplates.All,
            DisplayMemberPath = null,
        };

        _list.ItemTemplate = BuildItemTemplate();
        _list.SelectionChanged += (_, _) => ShowSelected();

        _preview = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
        };

        var insert = new Button { Content = "Use this prompt", IsDefault = true, MinWidth = 130 };
        insert.Click += (_, _) => { DialogResult = true; };

        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 90, Margin = new Thickness(0) };
        cancel.Click += (_, _) => { DialogResult = false; };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
        };
        buttons.Children.Add(insert);
        buttons.Children.Add(cancel);

        var right = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        right.Children.Add(buttons);
        right.Children.Add(_preview);

        var grid = new Grid { Margin = new Thickness(14) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        Grid.SetColumn(_list, 0);
        Grid.SetColumn(right, 1);
        grid.Children.Add(_list);
        grid.Children.Add(right);

        Content = grid;

        Loaded += (_, _) =>
        {
            if (_list.Items.Count > 0) _list.SelectedIndex = 0;
        };
    }

    private static DataTemplate BuildItemTemplate()
    {
        // Built in code to match the window; a XAML resource would need its own dictionary.
        var panel = new FrameworkElementFactory(typeof(StackPanel));
        panel.SetValue(MarginProperty, new Thickness(2, 4, 2, 4));

        var title = new FrameworkElementFactory(typeof(TextBlock));
        title.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(PromptTemplate.Title)));
        title.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);

        var description = new FrameworkElementFactory(typeof(TextBlock));
        description.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(PromptTemplate.Description)));
        description.SetValue(TextBlock.FontSizeProperty, 10.0);
        description.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        description.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(0x95, 0xA3, 0xB5)));

        panel.AppendChild(title);
        panel.AppendChild(description);

        return new DataTemplate { VisualTree = panel };
    }

    private void ShowSelected()
    {
        _preview.Text = _list.SelectedItem is PromptTemplate template
            ? $"[{template.Category}] {template.Title}\n\n{template.Prompt}"
            : string.Empty;
    }

    /// <summary>Shows the gallery and returns the chosen template, or null if cancelled.</summary>
    public static PromptTemplate? Pick(Window owner)
    {
        var window = new PromptGalleryWindow(owner);
        return window.ShowDialog() == true ? window._list.SelectedItem as PromptTemplate : null;
    }
}
