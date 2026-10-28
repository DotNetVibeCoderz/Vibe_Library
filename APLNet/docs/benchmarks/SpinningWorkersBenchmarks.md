```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9711/25H2/2025Update/HudsonValley2)
Intel Core i7-8650U CPU 1.90GHz (Max: 2.11GHz) (Kaby Lake R), 1 CPU, 8 logical and 4 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  Job-IRXWSX : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

Runtime=.NET 10.0  MaxIterationCount=30  MinIterationCount=10  
WarmupCount=5  

```
| Method                     | N     | Mean       | StdDev    | Ratio         | Gen0   | Allocated | 
|--------------------------- |------ |-----------:|----------:|--------------:|-------:|----------:|
| ParallelFor                | 1000  | 10.3217 μs | 0.1629 μs |      baseline | 0.7019 |    2914 B | 
| AplForStructPool           | 1000  |  3.1272 μs | 0.1288 μs |  3.31x faster |      - |         - | 
| AplForStructSpinning       | 1000  |  2.2289 μs | 0.0162 μs |  4.63x faster |      - |         - | 
| AplForStructSpinningPinned | 1000  |  2.2065 μs | 0.0654 μs |  4.68x faster |      - |         - | 
| Sequential                 | 1000  |  0.4525 μs | 0.0163 μs | 22.84x faster |      - |         - | 
|                            |       |            |           |               |        |           | 
| ParallelFor                | 16000 | 34.1522 μs | 2.0135 μs |      baseline | 0.7324 |    3050 B | 
| AplForStructPool           | 16000 |  8.6194 μs | 0.2726 μs |  3.97x faster |      - |         - | 
| AplForStructSpinning       | 16000 |  5.4930 μs | 0.1258 μs |  6.22x faster |      - |         - | 
| AplForStructSpinningPinned | 16000 |  5.9882 μs | 0.4851 μs |  5.74x faster |      - |         - | 
| Sequential                 | 16000 |  7.7962 μs | 0.3825 μs |  4.39x faster |      - |         - | 
