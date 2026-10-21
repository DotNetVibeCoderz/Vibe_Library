// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using ActorNet.Cluster;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace ActorNet.Samples.Android.ViewModels;

/// <summary>The handful of value conversions the view needs.</summary>
public static class Converters
{
    private static readonly IBrush Teal = new SolidColorBrush(Color.Parse("#4FB8A8"));
    private static readonly IBrush Brass = new SolidColorBrush(Color.Parse("#E0A458"));
    private static readonly IBrush Rose = new SolidColorBrush(Color.Parse("#E06A72"));
    private static readonly IBrush Ink = new SolidColorBrush(Color.Parse("#E8E4DA"));
    private static readonly IBrush Muted = new SolidColorBrush(Color.Parse("#868CA0"));

    /// <summary>Labels a button with what pressing it does, not with the state it is in.</summary>
    public static readonly IValueConverter NodeLabel =
        new FuncValueConverter<bool, string>(running => running ? "Leave cluster" : "Start node");

    public static readonly IValueConverter StreamLabel =
        new FuncValueConverter<bool, string>(streaming => streaming ? "Stop stream" : "Stream readings");

    /// <summary>Up is teal, a suspected peer brass, a lost one rose - the same meaning everywhere.</summary>
    public static readonly IValueConverter StatusBrush =
        new FuncValueConverter<MemberStatus, IBrush>(status => status switch
        {
            MemberStatus.Up => Teal,
            MemberStatus.Unreachable or MemberStatus.Joining or MemberStatus.Leaving => Brass,
            _ => Rose,
        });

    /// <summary>Teal for an actor living on this phone, so the split across the cluster reads at a glance.</summary>
    public static readonly IValueConverter OwnerBrush =
        new FuncValueConverter<bool, IBrush>(local => local ? Teal : Brass);

    public static readonly IValueConverter AlarmBrush =
        new FuncValueConverter<bool, IBrush>(alarm => alarm ? Rose : Ink);

    public static readonly IValueConverter UnansweredBrush =
        new FuncValueConverter<bool, IBrush>(unanswered => unanswered ? Rose : Muted);
}
