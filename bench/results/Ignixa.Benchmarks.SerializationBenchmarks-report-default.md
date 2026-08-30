
BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9106/25H2/2025Update/HudsonValley2) (Hyper-V)
AMD EPYC 7763 2.44GHz, 1 CPU, 16 logical and 8 physical cores
.NET SDK 10.0.303
  [Host]    : .NET 10.0.11 (10.0.11, 10.0.1126.37416), X64 RyuJIT x86-64-v3
  .NET 10.0 : .NET 10.0.11 (10.0.11, 10.0.1126.37416), X64 RyuJIT x86-64-v3

Job=.NET 10.0  Runtime=.NET 10.0  

 Method                                              | Mean         | Error       | StdDev      | Median       | Rank | Gen0     | Gen1    | Allocated  |
---------------------------------------------------- |-------------:|------------:|------------:|-------------:|-----:|---------:|--------:|-----------:|
 'Ignixa: Parse large Bundle (JsonSerializer)'       |   157.073 μs |   3.6648 μs |   9.9704 μs |   154.171 μs |    7 |   2.9297 |  0.2441 |   48.49 KB |
 'Firely: Parse large Bundle (FhirJsonNode)'         |   265.061 μs |   7.8023 μs |  21.8785 μs |   259.732 μs |    9 |  25.1465 | 15.8691 |  411.28 KB |
 'Firely: Parse large Bundle (POCO)'                 | 2,331.142 μs | 136.3304 μs | 401.9733 μs | 2,293.098 μs |   10 | 125.0000 | 31.2500 | 2069.29 KB |
 'Ignixa: Parse medium Observation (JsonSerializer)' |    16.041 μs |   0.8573 μs |   2.5279 μs |    15.439 μs |    3 |   0.3662 |       - |    6.21 KB |
 'Firely: Parse medium Observation (FhirJsonNode)'   |    17.131 μs |   0.4327 μs |   1.2416 μs |    17.002 μs |    3 |   1.6785 |  0.1526 |   27.63 KB |
 'Firely: Parse medium Observation (POCO)'           |   137.948 μs |   8.6665 μs |  25.5533 μs |   139.120 μs |    6 |   6.8359 |       - |  124.87 KB |
 'Ignixa: Parse small Patient (JsonSerializer)'      |     6.033 μs |   0.1899 μs |   0.5448 μs |     5.861 μs |    1 |   0.2213 |       - |    3.63 KB |
 'Firely: Parse small Patient (FhirJsonNode)'        |     6.166 μs |   0.1815 μs |   0.5265 μs |     6.011 μs |    1 |   0.8392 |  0.0381 |   13.77 KB |
 'Firely: Parse small Patient (POCO)'                |    37.526 μs |   0.7471 μs |   0.7994 μs |    37.268 μs |    4 |   2.6855 |       - |   47.52 KB |
 'Ignixa: Serialize large Bundle'                    |   200.836 μs |   3.7629 μs |  10.1731 μs |   198.476 μs |    8 |   4.8828 |       - |   80.91 KB |
 'Firely: Serialize large Bundle (POCO)'             | 2,345.025 μs | 138.6475 μs | 408.8053 μs | 2,165.121 μs |   10 | 140.6250 | 31.2500 | 2372.09 KB |
 'Ignixa: Serialize small Patient'                   |     7.474 μs |   0.1797 μs |   0.5039 μs |     7.366 μs |    2 |   0.2441 |       - |    4.52 KB |
 'Firely: Serialize small Patient (POCO)'            |    45.658 μs |   0.9046 μs |   1.0054 μs |    45.587 μs |    5 |   3.4180 |       - |   58.42 KB |
