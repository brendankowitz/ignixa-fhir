```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9106/25H2/2025Update/HudsonValley2) (Hyper-V)
AMD EPYC 7763 2.44GHz, 1 CPU, 16 logical and 8 physical cores
.NET SDK 10.0.303
  [Host]    : .NET 10.0.11 (10.0.11, 10.0.1126.37416), X64 RyuJIT x86-64-v3
  .NET 10.0 : .NET 10.0.11 (10.0.11, 10.0.1126.37416), X64 RyuJIT x86-64-v3

Job=.NET 10.0  Runtime=.NET 10.0  

```
| Method                                            | Mean      | Error    | StdDev   | Ratio | RatioSD | Rank | Gen0    | Allocated | Alloc Ratio |
|-------------------------------------------------- |----------:|---------:|---------:|------:|--------:|-----:|--------:|----------:|------------:|
| &#39;Ignixa: Extract search parameters (Patient)&#39;     |  57.58 μs | 1.148 μs | 2.041 μs |  1.00 |    0.05 |    1 |  4.3945 |  77.15 KB |        1.00 |
| &#39;Ignixa: Extract search parameters (Observation)&#39; | 180.82 μs | 3.554 μs | 4.621 μs |  3.14 |    0.13 |    2 | 13.6719 | 229.72 KB |        2.98 |
