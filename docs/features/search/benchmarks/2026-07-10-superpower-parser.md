```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.8737/25H2/2025Update/HudsonValley2)
Intel Core i7-14700K 3.40GHz, 1 CPU, 28 logical and 20 physical cores
.NET SDK 10.0.109
  [Host]    : .NET 10.0.9 (10.0.9, 10.0.926.27113), X64 RyuJIT x86-64-v3
  .NET 10.0 : .NET 10.0.9 (10.0.9, 10.0.926.27113), X64 RyuJIT x86-64-v3

Job=.NET 10.0  Runtime=.NET 10.0  IterationCount=15  
LaunchCount=1  WarmupCount=5  

```
| Method | Case               | Mean     | Error     | StdDev    | Gen0   | Allocated |
|------- |------------------- |---------:|----------:|----------:|-------:|----------:|
| **Parse**  | **Simple**             | **1.774 μs** | **0.0069 μs** | **0.0058 μs** | **0.2594** |   **4.49 KB** |
| **Parse**  | **Modified**           | **2.100 μs** | **0.0139 μs** | **0.0130 μs** | **0.3052** |   **5.25 KB** |
| **Parse**  | **TypedChain**         | **2.949 μs** | **0.0362 μs** | **0.0339 μs** | **0.4272** |   **7.26 KB** |
| **Parse**  | **NestedReverseChain** | **6.886 μs** | **0.0380 μs** | **0.0355 μs** | **0.7629** |  **13.19 KB** |
| **Parse**  | **EscapedAlternative** | **3.485 μs** | **0.0284 μs** | **0.0252 μs** | **0.4730** |   **8.07 KB** |
| **Parse**  | **Composite**          | **4.859 μs** | **0.0362 μs** | **0.0339 μs** | **0.6714** |  **11.76 KB** |
