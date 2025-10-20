
BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.6725)
Intel Core i7-14700K, 1 CPU, 28 logical and 20 physical cores
.NET SDK 9.0.201
  [Host]   : .NET 9.0.3 (9.0.325.11113), X64 RyuJIT AVX2
  .NET 9.0 : .NET 9.0.3 (9.0.325.11113), X64 RyuJIT AVX2

Job=.NET 9.0  Runtime=.NET 9.0  

 Method                                                  | Mean         | Error       | StdDev      | Rank | Gen0    | Gen1   | Allocated |
-------------------------------------------------------- |-------------:|------------:|------------:|-----:|--------:|-------:|----------:|
 'Ignixa: Array indexing (Patient.name[0].given)'        |     484.0 ns |     5.18 ns |     4.85 ns |    3 |  0.0911 |      - |   1.54 KB |
 'Firely: Array indexing (Patient.name[0].given)'        | 297,918.3 ns | 5,763.79 ns | 6,861.39 ns |    8 |  5.8594 | 2.9297 | 100.16 KB |
 'Ignixa: Compile FHIRPath expression'                   | 109,748.4 ns |   580.18 ns |   514.31 ns |    7 |  2.1973 |      - |  38.17 KB |
 'Ignixa: Complex navigation (where + first)'            |     675.9 ns |     6.51 ns |     6.09 ns |    4 |  0.1345 |      - |   2.27 KB |
 'Firely: Complex navigation (where + first)'            | 293,352.8 ns | 2,861.76 ns | 2,676.89 ns |    8 |  5.8594 | 2.9297 | 102.18 KB |
 'Ignixa: Scalar extraction (Patient.birthDate)'         |     244.3 ns |     4.71 ns |     4.41 ns |    1 |  0.1013 |      - |   1.71 KB |
 'Firely: Scalar extraction (Patient.birthDate)'         | 285,309.3 ns | 4,963.13 ns | 4,642.51 ns |    8 |  5.3711 | 2.4414 |  96.99 KB |
 'Ignixa: Search parameter extraction (component value)' |   1,508.7 ns |     7.65 ns |     6.78 ns |    5 |  0.2689 |      - |   4.56 KB |
 'Firely: Search parameter extraction (component value)' |  71,422.8 ns |   457.95 ns |   428.37 ns |    6 | 14.7705 | 3.2959 | 249.39 KB |
 'Ignixa: Simple FHIRPath (Patient.name.family)'         |     345.0 ns |     2.21 ns |     2.07 ns |    2 |  0.0658 |      - |   1.11 KB |
 'Firely: Simple FHIRPath (Patient.name.family)'         | 283,911.5 ns | 3,601.12 ns | 3,368.49 ns |    8 |  5.3711 | 2.4414 |  97.18 KB |

Method                                             | Mean           | Error      | StdDev     | Rank | Gen0   | Gen1   | Allocated |
--------------------------------------------------- |---------------:|-----------:|-----------:|-----:|-------:|-------:|----------:|
 'Ignixa: Access array element (JsonNode direct)'   |     45.1500 ns |  0.1134 ns |  0.1060 ns |    7 |      - |      - |         - |
 'Ignixa: Access array element (ITypedElement)'     |    297.3031 ns |  1.4152 ns |  1.3237 ns |   12 | 0.0443 |      - |     768 B |
 'Firely: Access array element (POCO)'              |      8.1334 ns |  0.0239 ns |  0.0224 ns |    5 |      - |      - |         - |
 'Firely: Access array element (ITypedElement)'     |  4,033.2086 ns | 44.7059 ns | 41.8179 ns |   15 | 0.6714 | 0.0076 |   11664 B |
 'Ignixa: Convert to ISourceNode'                   |      0.8017 ns |  0.0225 ns |  0.0211 ns |    2 |      - |      - |         - |
 'Firely: Already ISourceNode (no-op)'              |      0.5741 ns |  0.0230 ns |  0.0215 ns |    1 |      - |      - |         - |
 'Ignixa: Convert to ITypedElement'                 | 11,139.9935 ns | 26.9351 ns | 25.1951 ns |   16 | 1.0834 | 0.0458 |   18880 B |
 'Firely: Convert to ITypedElement'                 |     65.8152 ns |  0.6965 ns |  0.6515 ns |    9 | 0.0143 |      - |     248 B |
 'Ignixa: Access nested object (JsonNode direct)'   |     46.9281 ns |  0.3235 ns |  0.3026 ns |    8 | 0.0042 |      - |      72 B |
 'Ignixa: Access nested object (ITypedElement)'     |    239.8313 ns |  2.9427 ns |  2.7526 ns |   11 | 0.0486 |      - |     840 B |
 'Firely: Access nested object (POCO)'              |      6.3508 ns |  0.0333 ns |  0.0311 ns |    4 |      - |      - |         - |
 'Firely: Access nested object (ITypedElement)'     |  3,353.7403 ns | 24.1698 ns | 21.4259 ns |   14 | 0.6065 | 0.0038 |   10528 B |
 'Ignixa: Access simple property (JsonNode direct)' |     32.1506 ns |  0.1917 ns |  0.1793 ns |    6 | 0.0037 |      - |      64 B |
 'Ignixa: Access simple property (ITypedElement)'   |    111.5224 ns |  0.8173 ns |  0.7645 ns |   10 | 0.0185 |      - |     320 B |
 'Firely: Access simple property (POCO)'            |      6.0689 ns |  0.0773 ns |  0.0685 ns |    3 | 0.0014 |      - |      24 B |
 'Firely: Access simple property (ITypedElement)'   |  1,471.0983 ns |  9.0017 ns |  7.5168 ns |   13 | 0.2804 | 0.0019 |    4848 B |
