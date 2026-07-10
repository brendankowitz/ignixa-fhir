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
| **Parse**  | **Simple**             | **142.2 ns** |  **1.14 ns** |  **1.06 ns** | **0.0315** |     **544 B** |
| **Parse**  | **Modified**           | **157.5 ns** |  **1.34 ns** |  **1.25 ns** | **0.0350** |     **608 B** |
| **Parse**  | **TypedChain**         | **277.7 ns** |  **2.64 ns** |  **2.34 ns** | **0.0668** |    **1152 B** |
| **Parse**  | **NestedReverseChain** | **560.0 ns** |  **8.39 ns** |  **7.85 ns** | **0.1278** |    **2208 B** |
| **Parse**  | **EscapedAlternative** | **522.0 ns** |  **4.54 ns** |  **4.02 ns** | **0.1097** |    **1904 B** |
| **Parse**  | **Composite**          | **922.6 ns** | **13.05 ns** | **11.57 ns** | **0.2165** |    **3736 B** |
