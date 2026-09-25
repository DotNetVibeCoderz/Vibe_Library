```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9457/25H2/2025Update/HudsonValley2)
Intel Core i7-8650U CPU 1.90GHz (Max: 2.11GHz) (Kaby Lake R), 1 CPU, 8 logical and 4 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  Job-IRXWSX : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

Runtime=.NET 10.0  MaxIterationCount=30  MinIterationCount=10  
WarmupCount=5  

```
| Method         | N     | Mean       | StdDev    | Ratio         | Gen0   | Allocated | 
|--------------- |------ |-----------:|----------:|--------------:|-------:|----------:|
| ParallelFor    | 1000  | 10.3514 μs | 0.1310 μs |      baseline | 0.6714 |    2862 B | 
| AplForDelegate | 1000  |  3.6638 μs | 0.0520 μs |  2.83x faster | 0.0229 |      96 B | 
| AplForStruct   | 1000  |  2.7904 μs | 0.0242 μs |  3.71x faster |      - |         - | 
| Sequential     | 1000  |  0.6564 μs | 0.1450 μs | 16.46x faster |      - |         - | 
|                |       |            |           |               |        |           | 
| ParallelFor    | 16000 | 37.1355 μs | 1.8496 μs |      baseline | 0.7324 |    3014 B | 
| AplForDelegate | 16000 | 21.9252 μs | 1.1515 μs |  1.70x faster |      - |      96 B | 
| AplForStruct   | 16000 |  9.5623 μs | 0.1449 μs |  3.88x faster |      - |         - | 
| Sequential     | 16000 |  7.8103 μs | 0.2766 μs |  4.76x faster |      - |         - | 
