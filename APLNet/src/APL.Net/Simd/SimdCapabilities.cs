// APL.Net - Another Parallel Library for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;

namespace AplNet.Simd;

/// <summary>What the SIMD kernels can use on this machine, as the runtime reports it.</summary>
public static class SimdCapabilities
{
    /// <summary>128-bit vectors are hardware accelerated (SSE on x86/x64, AdvSimd on Arm64).</summary>
    public static bool IsVector128Accelerated => Vector128.IsHardwareAccelerated;

    /// <summary>256-bit vectors are hardware accelerated (AVX2).</summary>
    public static bool IsVector256Accelerated => Vector256.IsHardwareAccelerated;

    /// <summary>
    /// 512-bit vectors are hardware accelerated (AVX-512). The runtime may report <c>false</c> on a
    /// CPU that has AVX-512 if it judges 512-bit execution slower there, or when
    /// <c>DOTNET_PreferredVectorBitWidth</c> caps it.
    /// </summary>
    public static bool IsVector512Accelerated => Vector512.IsHardwareAccelerated;

    /// <summary>The CPU supports AVX2.</summary>
    public static bool IsAvx2Supported => Avx2.IsSupported;

    /// <summary>The CPU supports AVX-512 Foundation.</summary>
    public static bool IsAvx512FSupported => Avx512F.IsSupported;

    /// <summary>The CPU supports Arm AdvSimd (NEON).</summary>
    public static bool IsAdvSimdSupported => AdvSimd.IsSupported;

    /// <summary>The widest vector the kernels will use, in bits, or 0 when they run scalar.</summary>
    public static int PreferredVectorWidth =>
        Vector512.IsHardwareAccelerated ? 512 :
        Vector256.IsHardwareAccelerated ? 256 :
        Vector128.IsHardwareAccelerated ? 128 : 0;

    /// <summary>A one-line summary, e.g. <c>"Vector256 (AVX2) - 8 x float per vector"</c>.</summary>
    public static string Describe() => PreferredVectorWidth switch
    {
        512 => $"Vector512 (AVX-512) - {Vector512<float>.Count} x float per vector",
        256 => $"Vector256 ({(Avx2.IsSupported ? "AVX2" : "256-bit")}) - {Vector256<float>.Count} x float per vector",
        128 => $"Vector128 ({(AdvSimd.IsSupported ? "AdvSimd" : "SSE")}) - {Vector128<float>.Count} x float per vector",
        _ => "No SIMD acceleration - scalar fallback",
    };
}
