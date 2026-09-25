// APL.Net - Another Parallel Library for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

namespace AplNet;

/// <summary>
/// Marks a static method as a loop body. The APL.Net source generator emits a
/// <c>readonly struct</c> implementing <see cref="Core.IWorkBody"/> that calls it, plus a factory
/// method on <c>AplNet.AplGen</c> named after the method.
/// </summary>
/// <remarks>
/// <para>The method must be <c>static</c>, return <c>void</c>, take the loop index as its first
/// parameter (<c>int</c>), and be at least <c>internal</c>. Every further parameter becomes a field of
/// the generated struct and a parameter of the factory:</para>
/// <code>
/// [AplBody]
/// internal static void Scale(int i, double[] data, double factor) =&gt; data[i] *= factor;
///
/// Apl.For(0, data.Length, AplGen.Scale(data, 2.0));
/// </code>
/// <para>This gives the struct-invoker's zero-allocation, inlined dispatch without writing the struct
/// by hand, and without runtime reflection - it is all resolved at compile time, so it works under
/// NativeAOT.</para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class AplBodyAttribute : Attribute;

/// <summary>
/// Like <see cref="AplBodyAttribute"/>, for a method whose first two parameters are a range
/// (<c>int fromInclusive, int toExclusive</c>). The generated struct implements
/// <see cref="Core.IRangeWorkBody"/> and is used with <see cref="Apl.ForRange{TBody}"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class AplRangeBodyAttribute : Attribute;
