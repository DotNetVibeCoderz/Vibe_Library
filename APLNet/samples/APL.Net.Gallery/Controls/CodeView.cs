// APL.Net - Another Parallel Library for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using Avalonia;
using Avalonia.Controls;
using AplNet.Gallery.Infrastructure;

namespace AplNet.Gallery.Controls;

/// <summary>A selectable, syntax-coloured C# snippet.</summary>
public sealed class CodeView : SelectableTextBlock
{
    public static readonly StyledProperty<string?> CodeProperty =
        AvaloniaProperty.Register<CodeView, string?>(nameof(Code));

    protected override Type StyleKeyOverride => typeof(SelectableTextBlock);

    public string? Code
    {
        get => GetValue(CodeProperty);
        set => SetValue(CodeProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CodeProperty)
            Inlines = SyntaxHighlighter.Highlight(Code ?? string.Empty);
    }
}
