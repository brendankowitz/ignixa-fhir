```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9106/25H2/2025Update/HudsonValley2) (Hyper-V)
AMD EPYC 7763 2.44GHz, 1 CPU, 16 logical and 8 physical cores
.NET SDK 10.0.303
  [Host]    : .NET 10.0.11 (10.0.11, 10.0.1126.37416), X64 RyuJIT x86-64-v3
  .NET 10.0 : .NET 10.0.11 (10.0.11, 10.0.1126.37416), X64 RyuJIT x86-64-v3

Job=.NET 10.0  Runtime=.NET 10.0  

```
| Method                                             | Mean         | Error       | StdDev      | Median       | Rank | Gen0   | Allocated |
|--------------------------------------------------- |-------------:|------------:|------------:|-------------:|-----:|-------:|----------:|
| &#39;Ignixa: Access array element (JsonNode direct)&#39;   |   178.738 ns |   9.6240 ns |  28.3765 ns |   184.081 ns |    8 |      - |         - |
| &#39;Ignixa: Access array element (IElement)&#39;          |   277.166 ns |   5.3008 ns |   5.8918 ns |   278.112 ns |    9 | 0.0019 |      32 B |
| &#39;Firely: Access array element (POCO)&#39;              |    18.883 ns |   0.6117 ns |   1.8035 ns |    19.152 ns |    3 |      - |         - |
| &#39;Firely: Access array element (ITypedElement)&#39;     | 7,955.698 ns | 261.2070 ns | 770.1748 ns | 7,831.089 ns |   12 | 0.6866 |   11536 B |
| &#39;Ignixa: Convert to ISourceNavigator&#39;              |     1.421 ns |   0.1594 ns |   0.4549 ns |     1.369 ns |    1 |      - |         - |
| &#39;Firely: Already ISourceNode (no-op)&#39;              |     1.250 ns |   0.1386 ns |   0.3977 ns |     1.130 ns |    1 |      - |         - |
| &#39;Ignixa: Convert to IElement&#39;                      |   114.055 ns |   3.0024 ns |   8.8056 ns |   112.707 ns |    6 | 0.0086 |     144 B |
| &#39;Firely: Convert to ITypedElement&#39;                 |   133.846 ns |   2.5956 ns |   6.3181 ns |   132.716 ns |    7 | 0.0148 |     248 B |
| &#39;Ignixa: Access nested object (JsonNode direct)&#39;   |   118.151 ns |   4.6468 ns |  13.5550 ns |   113.916 ns |    6 | 0.0043 |      72 B |
| &#39;Ignixa: Access nested object (IElement)&#39;          |   166.331 ns |   6.6488 ns |  19.6040 ns |   171.151 ns |    8 |      - |         - |
| &#39;Firely: Access nested object (POCO)&#39;              |    12.213 ns |   0.5415 ns |   1.5882 ns |    11.736 ns |    2 |      - |         - |
| &#39;Firely: Access nested object (ITypedElement)&#39;     | 6,651.419 ns | 291.6512 ns | 859.9401 ns | 6,502.185 ns |   11 | 0.6180 |   10464 B |
| &#39;Ignixa: Access simple property (JsonNode direct)&#39; |    77.785 ns |   3.3585 ns |   9.8500 ns |    75.195 ns |    5 | 0.0038 |      64 B |
| &#39;Ignixa: Access simple property (IElement)&#39;        |    48.569 ns |   1.0552 ns |   2.9933 ns |    47.580 ns |    4 |      - |         - |
| &#39;Firely: Access simple property (POCO)&#39;            |    11.158 ns |   0.2905 ns |   0.3229 ns |    11.012 ns |    2 | 0.0014 |      24 B |
| &#39;Firely: Access simple property (ITypedElement)&#39;   | 3,089.647 ns |  97.5485 ns | 281.4498 ns | 3,015.851 ns |   10 | 0.2823 |    4784 B |
