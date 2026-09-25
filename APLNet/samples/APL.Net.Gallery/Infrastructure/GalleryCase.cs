// APL.Net - Another Parallel Library for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using Avalonia.Media.Imaging;

namespace AplNet.Gallery.Infrastructure;

/// <summary>Who a timed variant belongs to; decides its colour everywhere in the app.</summary>
public enum VariantKind
{
    Sequential,
    Tpl,
    Apl,
}

/// <summary>One way of doing the case's work, timed against the others.</summary>
public sealed record Variant(string Name, VariantKind Kind, Action Run);

/// <summary>One variant run with timestamps recorded, for the lane chart.</summary>
public sealed record LaneRun(string Name, VariantKind Kind, Action<LaneRecorder> Run);

/// <summary>A sidebar group, marked with the APL glyph for the idea it holds.</summary>
public sealed record CaseCategory(string Glyph, string NameEn, string NameId, string GlyphMeaningEn, string GlyphMeaningId)
{
    public static readonly CaseCategory Loops = new(
        "⍳", "Loops", "Loop",
        "⍳ (iota) generates the indices 0…n-1 in APL — the index range every loop here walks.",
        "⍳ (iota) di APL menghasilkan indeks 0…n-1 — rentang indeks yang dijalani setiap loop di sini.");

    public static readonly CaseCategory Simd = new(
        "⍤", "SIMD", "SIMD",
        "⍤ (rank) applies a function to cells of an array — here, to vector-sized cells of 8 or 16 lanes at once.",
        "⍤ (rank) menerapkan fungsi ke sel-sel array — di sini, ke sel seukuran vektor berisi 8 atau 16 lane sekaligus.");

    public static readonly CaseCategory Reduce = new(
        "+/", "Reductions", "Reduksi",
        "+/ is APL's plus-reduce: fold an array into one value with +. Every case here is a fold.",
        "+/ adalah plus-reduce di APL: melipat array menjadi satu nilai dengan +. Setiap kasus di sini adalah lipatan.");

    public static readonly CaseCategory Partitioning = new(
        "⊆", "Scheduling", "Penjadwalan",
        "⊆ (partition) splits an array into pieces in APL — how the work is divided decides who waits.",
        "⊆ (partition) memecah array menjadi potongan di APL — cara kerja dibagi menentukan siapa yang menunggu.");

    public static readonly CaseCategory LowLevel = new(
        "⎕", "Low level", "Tingkat rendah",
        "⎕ (quad) is APL's door to the system — here, raw pointers and compile-time code generation.",
        "⎕ (quad) adalah pintu APL ke sistem — di sini, pointer mentah dan pembangkitan kode saat kompilasi.");
}

/// <summary>A use case in the gallery: its data, its competing implementations, and how to show it.</summary>
public abstract class GalleryCase
{
    public abstract string Id { get; }

    public abstract CaseCategory Category { get; }

    public abstract string TitleEn { get; }

    public abstract string TitleId { get; }

    public abstract string SummaryEn { get; }

    public abstract string SummaryId { get; }

    /// <summary>What to look for in the result - the lesson, stated honestly, including when TPL wins.</summary>
    public abstract string TakeawayEn { get; }

    public abstract string TakeawayId { get; }

    public abstract string SizeEn { get; }

    public abstract string SizeId { get; }

    public abstract string CodeApl { get; }

    public abstract string CodeTpl { get; }

    /// <summary>Allocates inputs. Called on a worker thread before the first run; idempotent.</summary>
    public abstract void Setup();

    /// <summary>Drops large buffers when the case is left, so browsing the gallery does not hold gigabytes.</summary>
    public virtual void Release()
    {
    }

    public abstract IReadOnlyList<Variant> CreateVariants();

    /// <summary>Runs the sequential reference and the main APL.Net variant and compares their output.</summary>
    public abstract bool Verify();

    public virtual bool HasPreview => false;

    /// <summary>An image of the result, built after the runs. Called on the UI thread.</summary>
    public virtual Bitmap? CreatePreview() => null;

    public virtual IReadOnlyList<LaneRun> CreateLaneRuns() => [];

    /// <summary>The variant the headline compares against TPL; defaults to the fastest APL.Net variant.</summary>
    public virtual string? HeadlineVariant => null;
}
