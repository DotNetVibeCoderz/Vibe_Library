// APL.Net - Another Parallel Library for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using AplNet.Gallery.Infrastructure;

namespace AplNet.Gallery.Cases;

public static class CaseCatalog
{
    public static IReadOnlyList<GalleryCase> All { get; } =
    [
        new TrivialArithmeticCase(),
        new MediumComputeCase(),
        new CallOverheadCase(),
        new ParticlesCase(),
        new ImageFilterCase(),
        new MatrixMultiplyCase(),
        new AxpyCase(),
        new SumCase(),
        new DotProductCase(),
        new MonteCarloPiCase(),
        new MandelbrotCase(),
        new ZipfWorkloadCase(),
        new UnsafePointerCase(),
        new SourceGeneratorCase(),
    ];

    public static IReadOnlyList<CaseCategory> Categories { get; } =
    [
        CaseCategory.Loops,
        CaseCategory.Simd,
        CaseCategory.Reduce,
        CaseCategory.Partitioning,
        CaseCategory.LowLevel,
    ];
}
