# NativeAOT

[Bahasa Indonesia](../id/native-aot.md) · [Index](README.md)

APL.Net is built with `IsAotCompatible` and `IsTrimmable`. The hot path contains no reflection,
no `Reflection.Emit` and no `Activator.CreateInstance`. Struct bodies are resolved by generic
specialisation at compile time, and the source generator runs in the compiler.

## Verified

`tests/APL.Net.AotTests` exercises every public API against a sequential reference: all three
partitioners, `For`/`ForRange`/`ForEach`/`Reduce`, generated bodies, SIMD over
`float`/`double`/`int`/`long`, the pointer tier, cancellation, exception aggregation and
`ForEachAsync`. It is published with `PublishAot=true`, and trim/AOT warnings are errors:

```sh
dotnet publish tests/APL.Net.AotTests -c Release -r win-x64 -o out/aot
out/aot/APL.Net.AotTests            # exit code 0 = every check passed
```

On the reference machine, this produces a 3.5 MB executable with zero trim or AOT warnings, and
every check passes. CI repeats the check on Linux.

## Vector width under AOT

A JIT picks instructions for the machine it runs on. An AOT compiler has to choose at build time.
By default, .NET 10's NativeAOT targets a conservative x64 baseline, so `SimdCapabilities` reports
**Vector128** even on an AVX2 machine. To use wider vectors, tell the compiler which CPUs you
target:

```xml
<PropertyGroup>
  <!-- AVX2-era CPUs (Haswell, 2013+ / Zen): Vector256 -->
  <IlcInstructionSet>x86-x64-v3</IlcInstructionSet>
  <!-- or: native (the build machine), x86-x64-v4 (AVX-512) -->
</PropertyGroup>
```

A binary built for `x86-x64-v3` will not start on a CPU without AVX2.

## Windows prerequisites

NativeAOT links with the MSVC toolchain. Install the "Desktop development with C++" workload. If
publishing fails with `'vswhere.exe' is not recognized`, add
`C:\Program Files (x86)\Microsoft Visual Studio\Installer` to `PATH`.

## Unit tests under AOT?

The xunit.v3 test runner discovers tests through reflection, so the xunit suite runs under the
JIT. The AOT half of the definition of done is covered by the check app above.
