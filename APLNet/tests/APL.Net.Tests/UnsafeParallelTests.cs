// APL.Net - Another Parallel Library for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Runtime.InteropServices;
using AplNet.Unsafe;

namespace AplNet.Tests;

public unsafe class UnsafeParallelTests
{
    private static void Affine(double* data, int i) => data[i] = data[i] * 2 + 1;

    private static void AffineRange(float* data, int from, int to)
    {
        for (int i = from; i < to; i++)
            data[i] = data[i] * 2 + 1;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AddContext
    {
        public int* X;
        public int* Y;
        public int* Destination;
    }

    private static void Add(void* context, int from, int to)
    {
        var c = (AddContext*)context;
        for (int i = from; i < to; i++)
            c->Destination[i] = c->X[i] + c->Y[i];
    }

    private static void Throw(double* data, int i)
    {
        if (i == 77)
            throw new InvalidOperationException("pointer body");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(100_001)]
    public void ForPtrMatchesSequential(int length)
    {
        using var buffer = new NativeBuffer<double>(length);
        for (int i = 0; i < length; i++)
            buffer[i] = i * 0.5;

        UnsafeParallel.ForPtr(buffer.Pointer, length, &Affine, maxDegreeOfParallelism: 4);

        for (int i = 0; i < length; i++)
            Assert.Equal(i * 0.5 * 2 + 1, buffer[i]);
    }

    [Fact]
    public void GenericAndRangeFormsMatchSequential()
    {
        float[] data = Enumerable.Range(0, 50_000).Select(i => (float)i).ToArray();
        fixed (float* p = data)
        {
            UnsafeParallel.ForRangePtr(p, data.Length, &AffineRange, new AplOptions { MaxDegreeOfParallelism = 3 });
        }

        for (int i = 0; i < data.Length; i++)
            Assert.Equal(i * 2f + 1, data[i]);

        using var d = new NativeBuffer<double>(1000);
        d.Span.Fill(1);
        UnsafeParallel.ForPtr<double>(d.Pointer, d.Length, &Affine);
        Assert.All(d.Span.ToArray(), v => Assert.Equal(3.0, v));
    }

    [Fact]
    public void ContextFormPassesSeveralBuffers()
    {
        int[] x = Enumerable.Range(0, 10_000).ToArray();
        int[] y = x.Select(v => v * 3).ToArray();
        var destination = new int[x.Length];
        fixed (int* px = x, py = y, pd = destination)
        {
            var context = new AddContext { X = px, Y = py, Destination = pd };
            UnsafeParallel.For(&context, 0, x.Length, &Add, new AplOptions { MaxDegreeOfParallelism = 4 });
        }

        Assert.Equal(x.Select(v => v * 4).ToArray(), destination);
    }

    [Fact]
    public void ManagedExceptionsFromPointerBodiesAreStillAggregated()
    {
        using var buffer = new NativeBuffer<double>(1000);
        var ex = Assert.Throws<AggregateException>(() => UnsafeParallel.ForPtr(buffer.Pointer, buffer.Length, &Throw, 4));
        Assert.IsType<InvalidOperationException>(Assert.Single(ex.InnerExceptions));
    }

    [Fact]
    public void ArgumentsAreChecked()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => UnsafeParallel.ForPtr((double*)1, -1, &Affine));
        Assert.Throws<ArgumentNullException>(() => UnsafeParallel.ForPtr((double*)null, 10, &Affine));
        Assert.Throws<ArgumentNullException>(() => UnsafeParallel.ForPtr((double*)1, 10, null));
        UnsafeParallel.ForPtr((double*)null, 0, &Affine);
    }

    [Fact]
    public void NativeBufferIsAlignedAndUsableWithTheSafeApis()
    {
        using var buffer = new NativeBuffer<float>(100_000);
        Assert.Equal(0, (long)buffer.Pointer % NativeBuffer<float>.Alignment);
        Assert.All(buffer.Span.ToArray(), v => Assert.Equal(0f, v));

        buffer.Span.Fill(2);
        Assert.Equal(200_000f, Simd.SimdOps.ParallelSum<float>(buffer.Memory, new AplOptions { MinChunkSize = 1000 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer[100_000]);
    }

    [Fact]
    public void ADisposedBufferRefusesAccess()
    {
        var buffer = new NativeBuffer<int>(10);
        buffer.Dispose();
        buffer.Dispose();
        Assert.Throws<ObjectDisposedException>(() => buffer.Span.Length);
        Assert.Throws<ObjectDisposedException>(() => (long)buffer.Pointer);
    }
}
