```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.6725)
Intel Core i7-14700K, 1 CPU, 28 logical and 20 physical cores
.NET SDK 9.0.201
  [Host]   : .NET 9.0.3 (9.0.325.11113), X64 RyuJIT AVX2
  .NET 9.0 : .NET 9.0.3 (9.0.325.11113), X64 RyuJIT AVX2

Job=.NET 9.0  Runtime=.NET 9.0  

```
| Method                                              | Mean         | Error     | StdDev    | Rank | Gen0     | Gen1    | Allocated  |
|---------------------------------------------------- |-------------:|----------:|----------:|-----:|---------:|--------:|-----------:|
| &#39;Ignixa: Parse large Bundle (JsonSerializer)&#39;       |    70.543 μs | 0.3103 μs | 0.2903 μs |    9 |   2.6855 |       - |   47.05 KB |
| &#39;Firely: Parse large Bundle (FhirJsonNode)&#39;         |   135.833 μs | 1.9942 μs | 1.8654 μs |   11 |  24.1699 | 15.8691 |  411.28 KB |
| &#39;Firely: Parse large Bundle (POCO)&#39;                 | 1,010.586 μs | 4.2605 μs | 3.5577 μs |   12 | 121.0938 | 29.2969 | 2070.08 KB |
| &#39;Ignixa: Parse medium Observation (JsonSerializer)&#39; |     5.799 μs | 0.0238 μs | 0.0223 μs |    4 |   0.2136 |       - |    3.61 KB |
| &#39;Firely: Parse medium Observation (FhirJsonNode)&#39;   |     8.290 μs | 0.0540 μs | 0.0451 μs |    5 |   1.6327 |  0.1526 |   27.63 KB |
| &#39;Firely: Parse medium Observation (POCO)&#39;           |    60.676 μs | 0.3766 μs | 0.3523 μs |    8 |   7.3242 |  0.2441 |     125 KB |
| &#39;Ignixa: Parse small Patient (JsonSerializer)&#39;      |     2.178 μs | 0.0097 μs | 0.0091 μs |    1 |   0.0801 |       - |    1.36 KB |
| &#39;Firely: Parse small Patient (FhirJsonNode)&#39;        |     3.114 μs | 0.0382 μs | 0.0357 μs |    3 |   0.8163 |  0.0381 |   13.77 KB |
| &#39;Firely: Parse small Patient (POCO)&#39;                |    22.800 μs | 0.2180 μs | 0.2039 μs |    6 |   2.8076 |       - |   47.52 KB |
| &#39;Ignixa: Serialize large Bundle&#39;                    |    90.981 μs | 0.3904 μs | 0.3652 μs |   10 |   4.6387 |       - |   79.47 KB |
| &#39;Firely: Serialize large Bundle (POCO)&#39;             | 1,140.588 μs | 8.3045 μs | 7.7680 μs |   13 | 140.6250 | 35.1563 | 2372.87 KB |
| &#39;Ignixa: Serialize small Patient&#39;                   |     2.897 μs | 0.0147 μs | 0.0130 μs |    2 |   0.1297 |       - |    2.24 KB |
| &#39;Firely: Serialize small Patient (POCO)&#39;            |    26.080 μs | 0.2344 μs | 0.2192 μs |    7 |   3.4180 |       - |   58.41 KB |
