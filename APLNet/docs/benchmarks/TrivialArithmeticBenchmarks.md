```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9457/25H2/2025Update/HudsonValley2)
Intel Core i7-8650U CPU 1.90GHz (Max: 2.11GHz) (Kaby Lake R), 1 CPU, 8 logical and 4 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  Job-IRXWSX : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

Runtime=.NET 10.0  MaxIterationCount=30  MinIterationCount=10  
WarmupCount=5  Alloc Ratio=NA  

```
| Method                          | N         | Mean           | StdDev        | Ratio        | Gen0   | Allocated | 
|-------------------------------- |---------- |---------------:|--------------:|-------------:|-------:|----------:|
| Sequential                      | 10000     |       5.328 μs |     0.1385 μs |     baseline |      - |         - | 
| ParallelFor                     | 10000     |      25.437 μs |     0.4275 μs | 4.78x slower | 0.7324 |    3023 B | 
| ParallelForEachRangePartitioner | 10000     |      14.808 μs |     0.3373 μs | 2.78x slower | 1.1139 |    4639 B | 
| AplForDelegate                  | 10000     |      15.356 μs |     1.0355 μs | 2.88x slower | 0.0153 |      96 B | 
| AplForStruct                    | 10000     |       6.526 μs |     0.2022 μs | 1.23x slower |      - |         - | 
| AplForRangeStruct               | 10000     |       5.810 μs |     0.0758 μs | 1.09x slower |      - |         - | 
|                                 |           |                |               |              |        |           | 
| Sequential                      | 1000000   |   1,197.361 μs |    47.6598 μs |     baseline |      - |         - | 
| ParallelFor                     | 1000000   |   1,383.152 μs |    12.0660 μs | 1.16x slower |      - |    3085 B | 
| ParallelForEachRangePartitioner | 1000000   |   1,177.519 μs |    63.5703 μs | 1.02x faster |      - |    4474 B | 
| AplForDelegate                  | 1000000   |   1,176.023 μs |    70.9582 μs | 1.02x faster |      - |      96 B | 
| AplForStruct                    | 1000000   |   1,405.743 μs |   119.4316 μs | 1.18x slower |      - |         - | 
| AplForRangeStruct               | 1000000   |   1,254.285 μs |    83.6450 μs | 1.05x slower |      - |         - | 
|                                 |           |                |               |              |        |           | 
| Sequential                      | 100000000 | 181,873.611 μs | 2,004.0613 μs |     baseline |      - |         - | 
| ParallelFor                     | 100000000 | 166,696.194 μs | 5,246.7536 μs | 1.09x faster |      - |    3776 B | 
| ParallelForEachRangePartitioner | 100000000 | 162,548.505 μs | 1,149.0330 μs | 1.12x faster |      - |    6648 B | 
| AplForDelegate                  | 100000000 | 165,947.553 μs | 5,291.0198 μs | 1.10x faster |      - |      96 B | 
| AplForStruct                    | 100000000 | 167,674.433 μs | 2,725.3739 μs | 1.08x faster |      - |         - | 
| AplForRangeStruct               | 100000000 | 169,176.104 μs | 1,730.4709 μs | 1.08x faster |      - |         - | 
