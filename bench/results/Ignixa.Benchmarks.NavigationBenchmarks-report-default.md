
BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.6725)
Intel Core i7-14700K, 1 CPU, 28 logical and 20 physical cores
.NET SDK 9.0.201
  [Host]   : .NET 9.0.3 (9.0.325.11113), X64 RyuJIT AVX2
  .NET 9.0 : .NET 9.0.3 (9.0.325.11113), X64 RyuJIT AVX2

Job=.NET 9.0  Runtime=.NET 9.0  

 Method                                             | Mean          | Error      | StdDev     | Rank | Gen0   | Gen1   | Allocated |
--------------------------------------------------- |--------------:|-----------:|-----------:|-----:|-------:|-------:|----------:|
 'Ignixa: Access array element (JsonNode direct)'   |    45.4391 ns |  0.1049 ns |  0.0930 ns |    7 |      - |      - |         - |
 'Ignixa: Access array element (ITypedElement)'     |   293.4296 ns |  1.0220 ns |  0.9060 ns |   12 | 0.0443 |      - |     768 B |
 'Firely: Access array element (POCO)'              |     7.9425 ns |  0.0214 ns |  0.0200 ns |    5 |      - |      - |         - |
 'Firely: Access array element (ITypedElement)'     | 4,009.0398 ns | 21.2646 ns | 17.7569 ns |   15 | 0.6714 | 0.0076 |   11664 B |
 'Ignixa: Convert to ISourceNode'                   |     0.8749 ns |  0.0120 ns |  0.0107 ns |    2 |      - |      - |         - |
 'Firely: Already ISourceNode (no-op)'              |     0.5437 ns |  0.0123 ns |  0.0102 ns |    1 |      - |      - |         - |
 'Ignixa: Convert to ITypedElement'                 |     5.6935 ns |  0.1301 ns |  0.1277 ns |    3 | 0.0023 |      - |      40 B |
 'Firely: Convert to ITypedElement'                 |    64.7614 ns |  0.3254 ns |  0.2885 ns |    9 | 0.0143 |      - |     248 B |
 'Ignixa: Access nested object (JsonNode direct)'   |    49.2744 ns |  0.0525 ns |  0.0438 ns |    8 | 0.0042 |      - |      72 B |
 'Ignixa: Access nested object (ITypedElement)'     |   245.3421 ns |  1.7679 ns |  1.4763 ns |   11 | 0.0486 |      - |     840 B |
 'Firely: Access nested object (POCO)'              |     6.1009 ns |  0.0296 ns |  0.0277 ns |    4 |      - |      - |         - |
 'Firely: Access nested object (ITypedElement)'     | 3,381.4469 ns | 22.6514 ns | 20.0799 ns |   14 | 0.6065 | 0.0038 |   10528 B |
 'Ignixa: Access simple property (JsonNode direct)' |    33.8593 ns |  0.2102 ns |  0.1966 ns |    6 | 0.0037 |      - |      64 B |
 'Ignixa: Access simple property (ITypedElement)'   |   111.4390 ns |  0.2982 ns |  0.2644 ns |   10 | 0.0185 |      - |     320 B |
 'Firely: Access simple property (POCO)'            |     5.8899 ns |  0.0516 ns |  0.0458 ns |    3 | 0.0014 |      - |      24 B |
 'Firely: Access simple property (ITypedElement)'   | 1,490.4520 ns | 10.0386 ns |  9.3901 ns |   13 | 0.2804 | 0.0019 |    4848 B |
