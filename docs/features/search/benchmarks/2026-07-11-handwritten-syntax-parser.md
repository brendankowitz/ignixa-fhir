```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.8737/25H2/2025Update/HudsonValley2)
Intel Core i7-14700K 3.40GHz, 1 CPU, 28 logical and 20 physical cores
.NET SDK 10.0.109
  [Host]    : .NET 10.0.9 (10.0.9, 10.0.926.27113), X64 RyuJIT x86-64-v3
  .NET 10.0 : .NET 10.0.9 (10.0.9, 10.0.926.27113), X64 RyuJIT x86-64-v3

Job=.NET 10.0  Runtime=.NET 10.0  IterationCount=15
LaunchCount=1  WarmupCount=5

```
| Method | Case               | Mean     | Error    | StdDev   | Gen0   | Allocated |
|------- |------------------- |---------:|---------:|---------:|-------:|----------:|
| **Parse**  | **Simple**             | **113.4 ns** |  **7.42 ns** |  **6.19 ns** | **0.0324** |     **560 B** |
| **Parse**  | **Modified**           | **134.2 ns** |  **2.10 ns** |  **1.96 ns** | **0.0379** |     **656 B** |
| **Parse**  | **TypedChain**         | **298.2 ns** | **24.05 ns** | **22.50 ns** | **0.0601** |    **1040 B** |
| **Parse**  | **NestedReverseChain** | **641.4 ns** |  **4.64 ns** |  **4.34 ns** | **0.1316** |    **2280 B** |
| **Parse**  | **EscapedAlternative** | **490.9 ns** |  **6.65 ns** |  **6.22 ns** | **0.1249** |    **2160 B** |
| **Parse**  | **Composite**          | **793.7 ns** |  **6.46 ns** |  **6.05 ns** | **0.1926** |    **3336 B** |
