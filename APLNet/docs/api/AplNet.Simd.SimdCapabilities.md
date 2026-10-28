# SimdCapabilities class

Namespace: `AplNet.Simd` · Assembly: `APL.Net` · [Index](README.md)

```csharp
public static class SimdCapabilities
```

What the SIMD kernels can use on this machine, as the runtime reports it.

## Properties

### IsAdvSimdSupported

```csharp
public static bool IsAdvSimdSupported { get; }
```

The CPU supports Arm AdvSimd (NEON).

### IsAvx2Supported

```csharp
public static bool IsAvx2Supported { get; }
```

The CPU supports AVX2.

### IsAvx512FSupported

```csharp
public static bool IsAvx512FSupported { get; }
```

The CPU supports AVX-512 Foundation.

### IsVector128Accelerated

```csharp
public static bool IsVector128Accelerated { get; }
```

128-bit vectors are hardware accelerated (SSE on x86/x64, AdvSimd on Arm64).

### IsVector256Accelerated

```csharp
public static bool IsVector256Accelerated { get; }
```

256-bit vectors are hardware accelerated (AVX2).

### IsVector512Accelerated

```csharp
public static bool IsVector512Accelerated { get; }
```

512-bit vectors are hardware accelerated (AVX-512). The runtime may report `false` on a CPU that has AVX-512 if it judges 512-bit execution slower there, or when `DOTNET_PreferredVectorBitWidth` caps it.

### PreferredVectorWidth

```csharp
public static int PreferredVectorWidth { get; }
```

The widest vector the kernels will use, in bits, or 0 when they run scalar.

## Methods

### Describe()

```csharp
public static string Describe();
```

A one-line summary, e.g. `"Vector256 (AVX2) - 8 x float per vector"`.
