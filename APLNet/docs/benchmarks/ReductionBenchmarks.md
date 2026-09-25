```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9457/25H2/2025Update/HudsonValley2)
Intel Core i7-8650U CPU 1.90GHz (Max: 2.11GHz) (Kaby Lake R), 1 CPU, 8 logical and 4 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  Job-IRXWSX : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

Runtime=.NET 10.0  MaxIterationCount=30  MinIterationCount=10  
WarmupCount=5  Alloc Ratio=NA  

```
| Method                    | N        | Mean        | StdDev      | Ratio        | Allocated | 
|-------------------------- |--------- |------------:|------------:|-------------:|----------:|
| LinqSum                   | 1000000  |  1,112.0 μs |    14.72 μs |     baseline |         - | 
| SequentialLoop            | 1000000  |  1,098.8 μs |     6.40 μs | 1.01x faster |         - | 
| ParallelForLocalSubtotals | 1000000  |  1,103.1 μs |    15.01 μs | 1.01x faster |    3520 B | 
| AplSimdSingleThread       | 1000000  |    303.6 μs |    50.45 μs | 3.75x faster |         - | 
| AplParallelSimd           | 1000000  |    194.3 μs |    41.84 μs | 6.04x faster |         - | 
|                           |          |             |             |              |           | 
| LinqSum                   | 10000000 | 13,116.0 μs |   852.47 μs |     baseline |         - | 
| SequentialLoop            | 10000000 | 13,481.1 μs | 1,884.67 μs | 1.03x slower |         - | 
| ParallelForLocalSubtotals | 10000000 | 10,150.9 μs |    97.75 μs | 1.29x faster |    3492 B | 
| AplSimdSingleThread       | 10000000 |  5,041.2 μs |   115.69 μs | 2.60x faster |         - | 
| AplParallelSimd           | 10000000 |  5,061.8 μs |   145.61 μs | 2.59x faster |         - | 
