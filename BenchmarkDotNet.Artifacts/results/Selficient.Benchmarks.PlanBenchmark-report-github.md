```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9550/25H2/2025Update/HudsonValley2)
Intel Core i9-14900HX 2.20GHz, 1 CPU, 32 logical and 24 physical cores
.NET SDK 10.0.403
  [Host]   : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3
  ShortRun : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method         | Mean     | Error     | StdDev    | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|--------------- |---------:|----------:|----------:|------:|--------:|-------:|-------:|----------:|------------:|
| GeneratePlan   | 7.281 μs | 3.2030 μs | 0.1756 μs |  1.00 |    0.03 | 1.2436 | 0.0381 |  22.87 KB |        1.00 |
| TickEverything | 1.856 μs | 0.2347 μs | 0.0129 μs |  0.26 |    0.01 | 0.2480 |      - |   4.56 KB |        0.20 |
