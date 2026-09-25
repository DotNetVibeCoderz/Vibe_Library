```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9457/25H2/2025Update/HudsonValley2)
Intel Core i7-8650U CPU 1.90GHz (Max: 2.11GHz) (Kaby Lake R), 1 CPU, 8 logical and 4 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  Job-IRXWSX : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

Runtime=.NET 10.0  MaxIterationCount=30  MinIterationCount=10  
WarmupCount=5  Alloc Ratio=NA  

```
| Method         | N      | Mean        | StdDev     | Ratio        | Allocated | 
|--------------- |------- |------------:|-----------:|-------------:|----------:|
| Sequential     | 100000 | 88,217.7 μs | 2,567.7 μs |     baseline |         - | 
| ParallelFor    | 100000 | 31,062.2 μs |   481.7 μs | 2.84x faster |    3167 B | 
| AplForDelegate | 100000 | 32,835.0 μs | 1,615.9 μs | 2.69x faster |      96 B | 
| AplForStruct   | 100000 | 31,582.6 μs |   514.8 μs | 2.79x faster |         - | 
