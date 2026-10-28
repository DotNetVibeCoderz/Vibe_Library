```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9711/25H2/2025Update/HudsonValley2)
Intel Core i7-8650U CPU 1.90GHz (Max: 2.11GHz) (Kaby Lake R), 1 CPU, 8 logical and 4 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  Job-IRXWSX : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

Runtime=.NET 10.0  MaxIterationCount=30  MinIterationCount=10  
WarmupCount=5  Alloc Ratio=NA  

```
| Method               | Mean        | StdDev      | Median     | Ratio        | Allocated | 
|--------------------- |------------:|------------:|-----------:|-------------:|----------:|
| ExpScalar            | 10,994.7 μs | 5,355.70 μs | 7,284.9 μs |     baseline |         - | 
| ExpSimd              |  4,416.8 μs |   215.68 μs | 4,468.0 μs | 2.50x faster |         - | 
| ExpParallelSimd      |  1,971.4 μs |    46.33 μs | 1,984.0 μs | 5.58x faster |         - | 
| LogScalar            |  6,468.3 μs |   299.79 μs | 6,441.8 μs | 1.70x faster |         - | 
| LogSimd              |  3,025.2 μs |   145.18 μs | 3,013.3 μs | 3.64x faster |         - | 
| MultiplyAddSimd      |  1,995.8 μs | 1,176.67 μs | 1,266.0 μs | 7.16x faster |         - | 
| FusedMultiplyAddSimd |  1,467.1 μs |   478.92 μs | 1,254.8 μs | 8.24x faster |         - | 
