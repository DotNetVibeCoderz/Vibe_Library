using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace Lvgl.Designer.Views;

/// <summary>Small modal dialogs and file pickers, styled like the rest of the designer.</summary>
public static class Dialogs
{
    /// <summary>Asks a yes/no question. The confirm button names the action, not "Yes".</summary>
    public static async Task<bool> ConfirmAsync(Window owner, string title, string message, string confirm, string cancel = "Cancel", bool destructive = false)
    {
        var result = false;
        var window = Build(title, message, out var buttons);

        var cancelButton = new Button { Content = cancel, MinWidth = 90 };
        var confirmButton = new Button { Content = confirm, MinWidth = 90, IsDefault = true };
        confirmButton.Classes.Add(destructive ? "danger" : "primary");

        cancelButton.Click += (_, _) => window.Close();
        confirmButton.Click += (_, _) =>
        {
            result = true;
            window.Close();
        };

        buttons.Children.Add(cancelButton);
        buttons.Children.Add(confirmButton);

        await window.ShowDialog(owner);
        return result;
    }

    /// <summary>Shows a message with a single dismiss button.</summary>
    public static async Task InformAsync(Window owner, string title, string message)
    {
        var window = Build(title, message, out var buttons);
        var ok = new Button { Content = "Close", MinWidth = 90, IsDefault = true, IsCancel = true };
        ok.Classes.Add("primary");
        ok.Click += (_, _) => window.Close();
        buttons.Children.Add(ok);
        await window.ShowDialog(owner);
    }

    /// <summary>Asks for one line of text. Null when cancelled.</summary>
    public static async Task<string?> PromptAsync(Window owner, string title, string message, string initial, string confirm = "OK")
    {
        string? result = null;
        var window = Build(title, message, out var buttons);

        var input = new TextBox { Text = initial, Margin = new Thickness(0, 10, 0, 0) };
        ((StackPanel)((Border)window.Content!).Child!).Children.Insert(2, input);

        var cancelButton = new Button { Content = "Cancel", MinWidth = 90, IsCancel = true };
        var okButton = new Button { Content = confirm, MinWidth = 90, IsDefault = true };
        okButton.Classes.Add("primary");

        cancelButton.Click += (_, _) => window.Close();
        okButton.Click += (_, _) =>
        {
            result = input.Text ?? string.Empty;
            window.Close();
        };

        buttons.Children.Add(cancelButton);
        buttons.Children.Add(okButton);

        window.Opened += (_, _) =>
        {
            input.Focus();
            input.SelectAll();
        };

        await window.ShowDialog(owner);
        return result;
    }

    private static Window Build(string title, string message, out StackPanel buttons)
    {
        buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0),
        };

        var heading = new TextBlock { Text = title };
        heading.Classes.Add("title");

        var body = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
        body.Classes.Add("dim");

        var window = new Window
        {
            Title = title,
            Width = 440,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new Border
            {
                Padding = new Thickness(22, 18, 22, 18),
                Child = new StackPanel { Children = { heading, body, buttons } },
            },
        };

        window.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) window.Close();
        };

        return window;
    }

    /// <summary>Picks one existing file. Null when cancelled or when the file has no local path.</summary>
    public static async Task<string?> OpenFileAsync(Window owner, string title, params FilePickerFileType[] types)
    {
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = types,
        });

        return files.Count == 0 ? null : files[0].TryGetLocalPath();
    }

    /// <summary>Picks several existing files.</summary>
    public static async Task<IReadOnlyList<string>> OpenFilesAsync(Window owner, string title, params FilePickerFileType[] types)
    {
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = true,
            FileTypeFilter = types,
        });

        return files.Select(file => file.TryGetLocalPath()).OfType<string>().ToList();
    }

    /// <summary>Picks a file to write. Null when cancelled.</summary>
    public static async Task<string?> SaveFileAsync(Window owner, string title, string suggestedName, string extension, params FilePickerFileType[] types)
    {
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = extension,
            FileTypeChoices = types,
            ShowOverwritePrompt = true,
        });

        return file?.TryGetLocalPath();
    }

    public static FilePickerFileType Layouts { get; } = new("LVGL layout") { Patterns = ["*.lvgl.json", "*.json"] };

    public static FilePickerFileType CSharp { get; } = new("C# source") { Patterns = ["*.cs"] };

    public static FilePickerFileType Images { get; } = new("Images (PNG, BMP)") { Patterns = ["*.png", "*.bmp"] };

    public static FilePickerFileType Fonts { get; } = new("TrueType fonts") { Patterns = ["*.ttf", "*.otf"] };

    public static FilePickerFileType AnyImage { get; } = new("Images") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.gif", "*.webp", "*.bmp"] };

    public static FilePickerFileType Documents { get; } = new("Documents")
    {
        Patterns = ["*.pdf", "*.txt", "*.md", "*.json", "*.csv", "*.xml", "*.log", "*.cs", "*.c", "*.h", "*.yml", "*.yaml"],
    };
}
