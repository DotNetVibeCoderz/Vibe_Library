// APL.Net - Another Parallel Library for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Collections.Immutable;
using AplNet.SourceGen;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace AplNet.Tests;

/// <summary>Bodies generated for real by the analyzer reference - these compile only if the generator works.</summary>
internal static class GeneratedBodies
{
    [AplBody]
    internal static void Affine(int i, double[] source, double[] destination, double scale) =>
        destination[i] = source[i] * scale + 1;

    [AplRangeBody]
    internal static void Square(int fromInclusive, int toExclusive, int[] data)
    {
        Span<int> span = data.AsSpan(fromInclusive, toExclusive - fromInclusive);
        for (int k = 0; k < span.Length; k++)
            span[k] *= span[k];
    }

    [AplBody]
    public static void Touch(int i, int[]? hits)
    {
        if (hits is not null)
            Interlocked.Increment(ref hits[i]);
    }

    internal static class Nested
    {
        [AplBody]
        internal static void Negate(int i, float[] data) => data[i] = -data[i];
    }
}

public class SourceGeneratorTests
{
    [Fact]
    public void GeneratedElementBodyRunsAsAStructInvoker()
    {
        double[] source = Enumerable.Range(0, 10_000).Select(i => (double)i).ToArray();
        var destination = new double[source.Length];

        var body = AplGen.Affine(source, destination, 3.0);
        Assert.True(body.GetType().IsValueType);
        Assert.IsAssignableFrom<Core.IWorkBody>(body);

        Apl.For(0, source.Length, body);
        Assert.Equal(source.Select(v => v * 3 + 1).ToArray(), destination);
    }

    [Fact]
    public void GeneratedRangeBodyAndNestedTypesWork()
    {
        int[] data = Enumerable.Range(0, 5000).ToArray();
        Apl.ForRange(0, data.Length, AplGen.Square(data));
        Assert.Equal(Enumerable.Range(0, 5000).Select(v => v * v).ToArray(), data);

        float[] floats = [1, -2, 3];
        Apl.For(0, floats.Length, AplGen.Negate(floats));
        Assert.Equal([-1f, 2f, -3f], floats);

        var hits = new int[100];
        Apl.For(0, hits.Length, AplGen.Touch(hits));
        Assert.All(hits, h => Assert.Equal(1, h));
    }

    [Theory]
    [InlineData("[AplBody] internal void M(int i) { }", "APL001")]
    [InlineData("[AplBody] internal static int M(int i) => i;", "APL002")]
    [InlineData("[AplBody] internal static void M(long i) { }", "APL003")]
    [InlineData("[AplBody] internal static void M() { }", "APL003")]
    [InlineData("[AplRangeBody] internal static void M(int from, long to) { }", "APL004")]
    [InlineData("[AplBody] internal static void M<T>(int i) { }", "APL005")]
    [InlineData("[AplBody] private static void M(int i) { }", "APL006")]
    [InlineData("[AplBody] internal static void M(int i, ref int x) { }", "APL007")]
    [InlineData("[AplBody] internal static void M(int i, System.Span<int> x) { }", "APL007")]
    [InlineData("[AplBody] internal static void M(int i, int[] x) { } [AplBody] internal static void M(int i, int[] y, int z) { } internal static class Other { [AplBody] internal static void M(int i, int[] x) { } }", "APL008")]
    public void InvalidBodiesAreReported(string members, string expectedId)
    {
        ImmutableArray<Diagnostic> diagnostics = RunGenerator($"using AplNet; internal static class C {{ {members} }}", out _);
        Assert.Contains(diagnostics, d => d.Id == expectedId);
    }

    [Fact]
    public void AValidBodyProducesCompilableCodeAndNoDiagnostics()
    {
        const string source = """
            using AplNet;
            namespace App
            {
                internal static class Kernels
                {
                    [AplBody] internal static void Scale(int i, double[] data, double factor) => data[i] *= factor;
                    [AplRangeBody] internal static void Clear(int from, int to, int[] data) => System.Array.Clear(data, from, to - from);
                }

                internal static class Use
                {
                    internal static void Run(double[] d, int[] x)
                    {
                        Apl.For(0, d.Length, AplGen.Scale(d, 2.0));
                        Apl.ForRange(0, x.Length, AplGen.Clear(x));
                    }
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = RunGenerator(source, out Compilation output);
        Assert.Empty(diagnostics);
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    private static ImmutableArray<Diagnostic> RunGenerator(string source, out Compilation output)
    {
        string runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        MetadataReference[] references =
        [
            .. Directory.GetFiles(runtimeDir, "System*.dll").Where(IsManaged).Select(p => MetadataReference.CreateFromFile(p)),
            MetadataReference.CreateFromFile(typeof(Apl).Assembly.Location),
        ];

        var compilation = CSharpCompilation.Create(
            "GeneratorTest",
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest))],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new AplBodyGenerator());
        driver.RunGeneratorsAndUpdateCompilation(compilation, out output, out ImmutableArray<Diagnostic> diagnostics);
        return diagnostics;
    }

    private static bool IsManaged(string path)
    {
        try
        {
            System.Reflection.AssemblyName.GetAssemblyName(path);
            return true;
        }
        catch (BadImageFormatException)
        {
            return false;
        }
    }
}
