```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9457/25H2/2025Update/HudsonValley2)
Intel Core i7-8650U CPU 1.90GHz (Max: 2.11GHz) (Kaby Lake R), 1 CPU, 8 logical and 4 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  Job-IRXWSX : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

Runtime=.NET 10.0  MaxIterationCount=30  MinIterationCount=10  
WarmupCount=5  Alloc Ratio=NA  

```
| Method         | N       | Mean        | StdDev      | Ratio        | Allocated | 
|--------------- |-------- |------------:|------------:|-------------:|----------:|
| Sequential     | 1000000 | 10,627.1 μs | 1,308.93 μs |     baseline |         - | 
| ParallelFor    | 1000000 |  3,985.7 μs |    35.37 μs | 2.67x faster |    3111 B | 
| AplForDelegate | 1000000 |  3,760.1 μs |   424.44 μs | 2.86x faster |      96 B | 
| AplForStruct   | 1000000 |  3,166.0 μs |    27.91 μs | 3.36x faster |         - | 
