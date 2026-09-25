```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9457/25H2/2025Update/HudsonValley2)
Intel Core i7-8650U CPU 1.90GHz (Max: 2.11GHz) (Kaby Lake R), 1 CPU, 8 logical and 4 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  Job-IRXWSX : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

Runtime=.NET 10.0  MaxIterationCount=30  MinIterationCount=10  
WarmupCount=5  Alloc Ratio=NA  

```
| Method                   | N        | Mean         | StdDev     | Median       | Ratio        | Allocated | 
|------------------------- |--------- |-------------:|-----------:|-------------:|-------------:|----------:|
| ScalarSequential         | 1000000  |    458.18 μs |  41.973 μs |    442.19 μs |     baseline |         - | 
| ParallelFor              | 1000000  |  1,138.42 μs |  42.665 μs |  1,131.01 μs | 2.50x slower |    3097 B | 
| AplForStructScalar       | 1000000  |    332.07 μs |  48.790 μs |    306.85 μs | 1.41x faster |         - | 
| AplSimdSingleThread      | 1000000  |    143.29 μs |  23.180 μs |    136.54 μs | 3.27x faster |         - | 
| AplParallelSimd          | 1000000  |     59.98 μs |   2.433 μs |     59.55 μs | 7.65x faster |         - | 
| AplParallelSimdDelegates | 1000000  |     89.73 μs |   2.137 μs |     89.28 μs | 5.11x faster |         - | 
|                          |          |              |            |              |              |           | 
| ScalarSequential         | 10000000 |  6,251.83 μs | 246.948 μs |  6,194.44 μs |     baseline |         - | 
| ParallelFor              | 10000000 | 11,571.94 μs | 384.523 μs | 11,495.96 μs | 1.85x slower |    3136 B | 
| AplForStructScalar       | 10000000 |  5,649.43 μs |  90.003 μs |  5,646.09 μs | 1.11x faster |         - | 
| AplSimdSingleThread      | 10000000 |  4,801.10 μs | 147.781 μs |  4,757.92 μs | 1.30x faster |         - | 
| AplParallelSimd          | 10000000 |  5,309.65 μs | 203.802 μs |  5,350.15 μs | 1.18x faster |         - | 
| AplParallelSimdDelegates | 10000000 |  5,336.21 μs |  90.036 μs |  5,318.38 μs | 1.17x faster |         - | 
