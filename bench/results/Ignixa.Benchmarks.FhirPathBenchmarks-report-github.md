```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.6725)
Intel Core i7-14700K, 1 CPU, 28 logical and 20 physical cores
.NET SDK 9.0.201
  [Host]   : .NET 9.0.3 (9.0.325.11113), X64 RyuJIT AVX2
  .NET 9.0 : .NET 9.0.3 (9.0.325.11113), X64 RyuJIT AVX2

Job=.NET 9.0  Runtime=.NET 9.0  

```
| Method                                                  | Mean         | Error       | StdDev      | Rank | Gen0    | Gen1   | Allocated |
|-------------------------------------------------------- |-------------:|------------:|------------:|-----:|--------:|-------:|----------:|
| &#39;Ignixa: Array indexing (Patient.name[0].given)&#39;        |     484.0 ns |     5.18 ns |     4.85 ns |    3 |  0.0911 |      - |   1.54 KB |
| &#39;Firely: Array indexing (Patient.name[0].given)&#39;        | 297,918.3 ns | 5,763.79 ns | 6,861.39 ns |    8 |  5.8594 | 2.9297 | 100.16 KB |
| &#39;Ignixa: Compile FHIRPath expression&#39;                   | 109,748.4 ns |   580.18 ns |   514.31 ns |    7 |  2.1973 |      - |  38.17 KB |
| &#39;Ignixa: Complex navigation (where + first)&#39;            |     675.9 ns |     6.51 ns |     6.09 ns |    4 |  0.1345 |      - |   2.27 KB |
| &#39;Firely: Complex navigation (where + first)&#39;            | 293,352.8 ns | 2,861.76 ns | 2,676.89 ns |    8 |  5.8594 | 2.9297 | 102.18 KB |
| &#39;Ignixa: Scalar extraction (Patient.birthDate)&#39;         |     244.3 ns |     4.71 ns |     4.41 ns |    1 |  0.1013 |      - |   1.71 KB |
| &#39;Firely: Scalar extraction (Patient.birthDate)&#39;         | 285,309.3 ns | 4,963.13 ns | 4,642.51 ns |    8 |  5.3711 | 2.4414 |  96.99 KB |
| &#39;Ignixa: Search parameter extraction (component value)&#39; |   1,508.7 ns |     7.65 ns |     6.78 ns |    5 |  0.2689 |      - |   4.56 KB |
| &#39;Firely: Search parameter extraction (component value)&#39; |  71,422.8 ns |   457.95 ns |   428.37 ns |    6 | 14.7705 | 3.2959 | 249.39 KB |
| &#39;Ignixa: Simple FHIRPath (Patient.name.family)&#39;         |     345.0 ns |     2.21 ns |     2.07 ns |    2 |  0.0658 |      - |   1.11 KB |
| &#39;Firely: Simple FHIRPath (Patient.name.family)&#39;         | 283,911.5 ns | 3,601.12 ns | 3,368.49 ns |    8 |  5.3711 | 2.4414 |  97.18 KB |
