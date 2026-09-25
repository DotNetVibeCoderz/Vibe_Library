```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9457/25H2/2025Update/HudsonValley2)
Intel Core i7-8650U CPU 1.90GHz (Max: 2.11GHz) (Kaby Lake R), 1 CPU, 8 logical and 4 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  Job-IRXWSX : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

Runtime=.NET 10.0  MaxIterationCount=30  MinIterationCount=10  
WarmupCount=5  Alloc Ratio=NA  

```
| Method          | N     | Layout   | Mean        | StdDev     | Median      | Ratio        | Allocated | 
|---------------- |------ |--------- |------------:|-----------:|------------:|-------------:|----------:|
| Sequential      | 10000 | Sorted   | 38,624.2 μs |   367.8 μs | 38,524.7 μs |     baseline |         - | 
| ParallelFor     | 10000 | Sorted   | 12,867.2 μs |   219.2 μs | 12,792.9 μs | 3.00x faster |    3214 B | 
| AplStatic       | 10000 | Sorted   | 31,323.7 μs |   720.0 μs | 31,133.0 μs | 1.23x faster |         - | 
| AplStriped      | 10000 | Sorted   | 17,662.7 μs |   269.8 μs | 17,586.5 μs | 2.19x faster |         - | 
| AplWorkStealing | 10000 | Sorted   |  8,942.2 μs |   432.0 μs |  8,932.5 μs | 4.33x faster |         - | 
|                 |       |          |             |            |             |              |           | 
| Sequential      | 10000 | Shuffled | 44,477.7 μs | 6,922.8 μs | 41,351.2 μs |     baseline |         - | 
| ParallelFor     | 10000 | Shuffled | 10,116.1 μs |   143.5 μs | 10,083.3 μs | 4.40x faster |    3264 B | 
| AplStatic       | 10000 | Shuffled | 13,205.4 μs | 1,816.8 μs | 12,145.7 μs | 3.42x faster |         - | 
| AplStriped      | 10000 | Shuffled | 17,141.7 μs | 1,673.9 μs | 17,721.5 μs | 2.62x faster |         - | 
| AplWorkStealing | 10000 | Shuffled | 14,978.4 μs | 1,666.8 μs | 14,898.1 μs | 3.00x faster |         - | 
