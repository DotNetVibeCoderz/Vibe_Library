using BenchmarkDotNet.Running;

BenchmarkSwitcher.FromAssembly(typeof(Lvgl.Benchmarks.RenderPathBenchmarks).Assembly).Run(args);
