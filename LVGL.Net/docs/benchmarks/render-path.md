```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9711/25H2/2025Update/HudsonValley2)
Intel Core i7-8650U CPU 1.90GHz (Max: 2.11GHz) (Kaby Lake R), 1 CPU, 8 logical and 4 physical cores
.NET SDK 10.0.401
  [Host]   : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  ShortRun : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method             | Mean          | Error          | StdDev       | Allocated |
|------------------- |--------------:|---------------:|-------------:|----------:|
| ChartAddPoint      |     129.20 ns |      52.360 ns |     2.870 ns |         - |
| SetBarValue        |     132.97 ns |      55.192 ns |     3.025 ns |         - |
| SetBackgroundColor |     271.85 ns |     134.878 ns |     7.393 ns |         - |
| RunFrameIdle       |      71.34 ns |       8.162 ns |     0.447 ns |         - |
| RedrawFullScreen   | 544,342.09 ns | 150,000.944 ns | 8,222.058 ns |         - |
| SetLabelText       |     300.44 ns |     119.327 ns |     6.541 ns |         - |
