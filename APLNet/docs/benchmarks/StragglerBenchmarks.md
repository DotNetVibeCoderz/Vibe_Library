```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9711/25H2/2025Update/HudsonValley2)
Intel Core i7-8650U CPU 1.90GHz (Max: 2.11GHz) (Kaby Lake R), 1 CPU, 8 logical and 4 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  Job-IRXWSX : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

Runtime=.NET 10.0  MaxIterationCount=30  MinIterationCount=10  
WarmupCount=5  

```
| Method              | BusyCore | Mean       | StdDev    | Ratio        | Allocated | 
|-------------------- |--------- |-----------:|----------:|-------------:|----------:|
| ParallelFor         | False    | 4,612.2 μs | 302.25 μs |     baseline |    3117 B | 
| AplStatic           | False    | 5,869.6 μs | 677.86 μs | 1.28x slower |         - | 
| AplStatic4PerWorker | False    | 3,754.9 μs | 176.46 μs | 1.23x faster |         - | 
| AplWorkStealing     | False    | 3,286.1 μs |  23.40 μs | 1.40x faster |         - | 
|                     |          |            |           |              |           | 
| ParallelFor         | True     | 4,678.2 μs |  64.88 μs |     baseline |    3022 B | 
| AplStatic           | True     | 4,910.2 μs |  20.90 μs | 1.05x slower |         - | 
| AplStatic4PerWorker | True     | 4,950.5 μs | 259.76 μs | 1.06x slower |         - | 
| AplWorkStealing     | True     | 3,809.7 μs | 115.81 μs | 1.23x faster |         - | 
