// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using Avalonia.Data.Converters;
using Avalonia.Media;

namespace ActorNet.Samples.Avalonia.ViewModels;

/// <summary>The handful of value conversions the views need.</summary>
public static class Converters
{
    /// <summary>
    /// Labels the stream button with the action it performs, not the state it is in.
    /// </summary>
    /// <remarks>
    /// A control says what happens when it is used. "Stop streaming" while running is right;
    /// "Streaming" would be a status label wearing a button's clothes.
    /// </remarks>
    public static readonly IValueConverter StreamLabel =
        new FuncValueConverter<bool, string>(streaming => streaming ? "Stop streaming" : "Start streaming");

    /// <summary>
    /// Colours a saga's status by outcome, so a failed order is findable in a long list without
    /// reading every row.
    /// </summary>
    /// <remarks>
    /// Rose means failure everywhere in this application and nothing else uses it, which is what
    /// makes it readable at a glance.
    /// </remarks>
    public static readonly IValueConverter OrderStatusBrush =
        new FuncValueConverter<string?, IBrush>(status => status switch
        {
            "Completed" => new SolidColorBrush(Color.Parse("#4FB8A8")),
            "Failed" => new SolidColorBrush(Color.Parse("#E06A72")),
            _ => new SolidColorBrush(Color.Parse("#E0A458")),
        });
}
