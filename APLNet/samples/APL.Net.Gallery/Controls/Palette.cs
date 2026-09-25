// APL.Net - Another Parallel Library for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using Avalonia.Media;
using AplNet.Gallery.Infrastructure;

namespace AplNet.Gallery.Controls;

/// <summary>
/// The palette in one place, for the controls that draw themselves. APL.Net is ultramarine, TPL is
/// amber and sequential is slate everywhere - bars, lanes, legends - so a colour always means the same
/// library. Blue against orange also stays distinguishable for the common colour-vision deficiencies.
/// </summary>
public static class Palette
{
    public static readonly Color Ink = Color.Parse("#131926");
    public static readonly Color Muted = Color.Parse("#5A6477");
    public static readonly Color Line = Color.Parse("#D3D9E1");
    public static readonly Color Panel = Color.Parse("#F8F9FB");
    public static readonly Color Apl = Color.Parse("#2F3FD0");
    public static readonly Color Tpl = Color.Parse("#C77A22");
    public static readonly Color Sequential = Color.Parse("#98A1B2");

    public static readonly FontFamily Mono = new("avares://APL.Net.Gallery/Assets/Fonts#Martian Mono");
    public static readonly FontFamily Body = new("fonts:Inter#Inter");

    public static Color For(VariantKind kind) => kind switch
    {
        VariantKind.Apl => Apl,
        VariantKind.Tpl => Tpl,
        _ => Sequential,
    };
}
